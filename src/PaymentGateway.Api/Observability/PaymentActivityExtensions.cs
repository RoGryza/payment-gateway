using System.Diagnostics;

using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Observability;

public static class PaymentActivityExtensions
{
    public static Activity? SetPaymentId(this Activity? activity, Guid id) =>
        activity?.SetTag("payment.id", id);

    public static Activity? SetPaymentStatus(this Activity? activity, string status) =>
        activity?.SetTag("payment.status", status);

    public static Activity? SetPaymentCurrency(this Activity? activity, string currency) =>
        activity?.SetTag("payment.currency", currency);

    public static Activity? SetPaymentAmount(this Activity? activity, int amount) =>
        activity?.SetTag("payment.amount", amount);

    public static Activity? SetIdempotencyKey(this Activity? activity, Guid? idempotencyKey) =>
        activity?.SetTag("payment.idempotency_key", idempotencyKey);

    public static Activity? SetRetryOutcome(this Activity? activity, string outcome) =>
        activity?.SetTag("payment.retry_outcome", outcome);

    public static Activity? SetPaymentTags(this Activity? activity, Payment payment) =>
        activity
            .SetPaymentId(payment.Id)
            .SetPaymentStatus(payment.Status.ToString())
            .SetPaymentCurrency(payment.Currency)
            .SetPaymentAmount(payment.Amount)
            .SetIdempotencyKey(payment.IdempotencyKey);
}