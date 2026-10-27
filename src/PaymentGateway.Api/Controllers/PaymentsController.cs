using System.Diagnostics;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly PaymentService _paymentService;

    public PaymentsController(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<GetPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentService.Get(id);

        if (payment is null)
        {
            return NotFound();
        }

        return Ok(new GetPaymentResponse
        {
            Id = payment.Id,
            // The service only returns resolved payments, which always have a public status.
            Status = ToPublicStatus(payment)!.Value,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        });
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPayment(PostPaymentRequest? request, CancellationToken cancellationToken)
    {
        // Model binding failed (e.g. non-numeric amount), or the request lacks what we need to even record it.
        // A request failing validation instead is recorded as a Rejected payment, see README.
        if (request is null)
        {
            // In reality we'd want to return actionable errors here.
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Invalid request");
        }

        if (request.IdempotencyKey == Guid.Empty)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "idempotencyKey must not be empty when provided.");
        }

        if (request.CardNumber is null || request.ExpiryMonth is null || request.ExpiryYear is null ||
            request.Currency is null || request.Amount is null || request.Cvv is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "cardNumber, expiryMonth, expiryYear, currency, amount and cvv are required.");
        }

        var submission = new PaymentSubmission
        {
            IdempotencyKey = request.IdempotencyKey,
            CardNumber = request.CardNumber,
            ExpiryMonth = request.ExpiryMonth.Value,
            ExpiryYear = request.ExpiryYear.Value,
            Currency = request.Currency,
            Amount = request.Amount.Value,
            Cvv = request.Cvv
        };

        var result = await _paymentService.SubmitAsync(submission, ModelState.IsValid, cancellationToken);

        if (result.Outcome != SubmissionOutcome.Processed)
        {
            var retryOutcome = result.Outcome.ToString();
            Activity.Current.SetRetryOutcome(retryOutcome);
            HttpContext.Features.Get<IHttpMetricsTagsFeature>()?.Tags
                .Add(new KeyValuePair<string, object?>("retry_outcome", retryOutcome));
        }

        if (result.Outcome == SubmissionOutcome.Conflict)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "This idempotency key was already used for a different payment.");
        }

        if (BuildResponse(result.Payment) is not { } response)
        {
            // We can't guarantee the unresolved payment hasn't been executed yet.
            // Return 409 to signal the idempotency key is used.
            return Problem(statusCode: StatusCodes.Status409Conflict,
                detail: "A previous attempt with this idempotency key has an unknown outcome.");
        }

        return Ok(response);
    }

    // null means payment.Status is Unresolved, its only status with no public representation.
    private static PaymentStatus? ToPublicStatus(Payment payment) => payment.Status switch
    {
        PaymentProcessingStatus.Authorized => PaymentStatus.Authorized,
        PaymentProcessingStatus.Declined => PaymentStatus.Declined,
        PaymentProcessingStatus.Rejected => PaymentStatus.Rejected,
        _ => null
    };

    private static PostPaymentResponse? BuildResponse(Payment payment)
    {
        if (ToPublicStatus(payment) is not { } status)
        {
            return null;
        }

        return new PostPaymentResponse
        {
            Id = payment.Id,
            Status = status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };
    }
}