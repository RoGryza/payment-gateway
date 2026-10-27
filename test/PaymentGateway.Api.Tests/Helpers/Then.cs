using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Helpers;

public static class Then
{
    public static void PaymentMatches(PaymentFixture payment, PostPaymentResponse actual, PaymentStatus expectedStatus) =>
        PaymentMatches(payment, expectedStatus, actual.Id, actual.Status, actual.CardNumberLastFour, actual.ExpiryMonth, actual.ExpiryYear, actual.Currency, actual.Amount);

    public static void PaymentMatches(PaymentFixture payment, GetPaymentResponse actual, PaymentStatus expectedStatus) =>
        PaymentMatches(payment, expectedStatus, actual.Id, actual.Status, actual.CardNumberLastFour, actual.ExpiryMonth, actual.ExpiryYear, actual.Currency, actual.Amount);

    private static void PaymentMatches(PaymentFixture payment, PaymentStatus expectedStatus, Guid id, PaymentStatus status, string cardNumberLastFour, int expiryMonth, int expiryYear, string currency, int amount)
    {
        Assert.True(payment.Id.HasValue, "fixture has no Id - was it created yet?");
        Assert.Equal(payment.Id.Value, id);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(payment.CardNumber[^4..], cardNumberLastFour);
        Assert.Equal(payment.ExpiryMonth, expiryMonth);
        Assert.Equal(payment.ExpiryYear, expiryYear);
        Assert.Equal(payment.Currency, currency);
        Assert.Equal(payment.Amount, amount);
    }

    public static void PaymentIsStored(PaymentsRepository repository, PaymentFixture payment, PaymentStatus expectedStatus)
    {
        Assert.True(payment.Id.HasValue, "fixture has no Id - was it created yet?");
        var stored = repository.Get(payment.Id.Value);
        Assert.NotNull(stored);
        // The internal status is a superset of the public one, with the same member names.
        Assert.Equal(Enum.Parse<PaymentProcessingStatus>(expectedStatus.ToString()), stored.Status);
        Assert.Equal(payment.IdempotencyKey, stored.IdempotencyKey);
        Assert.Equal(payment.CardNumber[^4..], stored.CardNumberLastFour);
        Assert.Equal(payment.ExpiryMonth, stored.ExpiryMonth);
        Assert.Equal(payment.ExpiryYear, stored.ExpiryYear);
        Assert.Equal(payment.Currency, stored.Currency);
        Assert.Equal(payment.Amount, stored.Amount);
    }

    public static void PaymentIsUnresolved(PaymentsRepository repository, PaymentFixture payment)
    {
        var stored = repository.GetByIdempotencyKey(payment.IdempotencyKey!.Value);
        Assert.NotNull(stored);
        Assert.Equal(PaymentProcessingStatus.Unresolved, stored.Status);
    }
}