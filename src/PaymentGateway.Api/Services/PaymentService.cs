using System.Diagnostics;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Observability;

namespace PaymentGateway.Api.Services;

public record PaymentSubmission
{
    public Guid? IdempotencyKey { get; init; }
    public required string CardNumber { get; init; }
    public required int ExpiryMonth { get; init; }
    public required int ExpiryYear { get; init; }
    public required string Currency { get; init; }
    public required int Amount { get; init; }
    public required string Cvv { get; init; }
}

public enum SubmissionOutcome
{
    // First time we've seen this idempotency key; the payment has been resolved.
    Processed,
    // The key was used before for the same payment. The returned payment may still be Unresolved.
    Replayed,
    // The key was used before for a different payment.
    Conflict
}

public record SubmissionResult(SubmissionOutcome Outcome, Payment Payment);

public class PaymentService
{
    private readonly PaymentsRepository _paymentsRepository;
    private readonly BankClient _bankClient;
    private readonly PaymentMetrics _metrics;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(PaymentsRepository paymentsRepository, BankClient bankClient, PaymentMetrics metrics, ILogger<PaymentService> logger)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SubmissionResult> SubmitAsync(PaymentSubmission submission, bool isValid, CancellationToken cancellationToken)
    {
        var payment = new Payment()
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = submission.IdempotencyKey,
            Status = PaymentProcessingStatus.Unresolved,
            CardNumberLastFour = LastFourDigitsOf(submission.CardNumber),
            ExpiryMonth = submission.ExpiryMonth,
            ExpiryYear = submission.ExpiryYear,
            Currency = submission.Currency,
            Amount = submission.Amount
        };

        Activity.Current.SetPaymentTags(payment);

        var (inserted, existing) = _paymentsRepository.TryInsert(payment);
        if (!inserted)
        {
            return HandleRetry(submission, existing);
        }

        if (!isValid)
        {
            return Finalize(payment, PaymentProcessingStatus.Rejected);
        }

        _logger.LogInformation("Sending payment {PaymentId} to the acquiring bank", payment.Id);
        var outcome = await _bankClient.AuthorizeAsync(submission, cancellationToken);

        var status = outcome == BankAuthorizationOutcome.Authorized ? PaymentProcessingStatus.Authorized : PaymentProcessingStatus.Declined;
        return Finalize(payment, status);
    }

    public Payment? Get(Guid id)
    {
        Activity.Current.SetPaymentId(id);

        var payment = _paymentsRepository.Get(id);
        if (payment is null)
        {
            return null;
        }
        Activity.Current.SetPaymentTags(payment);

        if (payment.Status == PaymentProcessingStatus.Unresolved)
        {
            // No legitimate client can ever hold this id - a submission that leaves a payment Unresolved
            // never returns one (no response body for that case).
            throw new InvalidOperationException($"Payment {id} is Unresolved and has no valid representation to return.");
        }

        return payment;
    }

    private SubmissionResult HandleRetry(PaymentSubmission submission, Payment existing)
    {
        if (!PaymentMatches(existing, submission))
        {
            _logger.LogWarning("Idempotency key already used by payment {PaymentId} with different data", existing.Id);
            return new SubmissionResult(SubmissionOutcome.Conflict, existing);
        }

        Activity.Current.SetPaymentTags(existing);
        if (existing.Status == PaymentProcessingStatus.Unresolved)
        {
            _logger.LogInformation("Retry of payment {PaymentId} while its outcome is unresolved", existing.Id);
        }
        else
        {
            _logger.LogInformation("Replaying payment {PaymentId} for a retried request", existing.Id);
        }

        return new SubmissionResult(SubmissionOutcome.Replayed, existing);
    }

    private SubmissionResult Finalize(Payment payment, PaymentProcessingStatus status)
    {
        var resolved = payment with { Status = status };
        _paymentsRepository.Update(resolved);
        Activity.Current.SetPaymentTags(resolved);
        _metrics.RecordPayment(status, resolved.Currency);
        _logger.LogInformation("Payment {PaymentId} finished as {Status}", resolved.Id, status);
        return new SubmissionResult(SubmissionOutcome.Processed, resolved);
    }

    private static bool PaymentMatches(Payment payment, PaymentSubmission submission) =>
        payment.CardNumberLastFour == LastFourDigitsOf(submission.CardNumber) &&
        payment.ExpiryMonth == submission.ExpiryMonth &&
        payment.ExpiryYear == submission.ExpiryYear &&
        payment.Currency == submission.Currency &&
        payment.Amount == submission.Amount;

    // A rejected submission's card number isn't guaranteed to be at least 4 characters long.
    private static string LastFourDigitsOf(string cardNumber) =>
        cardNumber.Length >= 4 ? cardNumber[^4..] : cardNumber;
}