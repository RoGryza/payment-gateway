using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest : IValidatableObject
{
    // Optional. Without one the request is processed with no idempotency guarantee. An explicit empty
    // Guid is rejected by the controller. The other fields are required (non-null), also enforced by the
    // controller since a request lacking them cannot be recorded.
    public Guid? IdempotencyKey { get; set; }

    [RegularExpression("^[0-9]{14,19}$")]
    public string? CardNumber { get; set; }

    [Range(1, 12)]
    public int? ExpiryMonth { get; set; }

    [Range(1, 9999)]
    public int? ExpiryYear { get; set; }

    [AllowedValues(SupportedCurrencies.Usd, SupportedCurrencies.Gbp, SupportedCurrencies.Eur)]
    public string? Currency { get; set; }

    [Range(1, int.MaxValue)]
    public int? Amount { get; set; }

    [RegularExpression("^[0-9]{3,4}$")]
    public string? Cvv { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var clock = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var now = clock.GetUtcNow();
        if (ExpiryYear is null || ExpiryMonth is null)
        {
            yield break;
        }

        if (ExpiryYear < now.Year || (ExpiryYear == now.Year && ExpiryMonth < now.Month))
        {
            yield return new ValidationResult("Card has expired", new[] { nameof(ExpiryMonth), nameof(ExpiryYear) });
        }
    }
}