using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests.Helpers;

// A payment's defining attributes, used to drive test setup and assertions.
// Randomized valid fields by default, override just what a given test cares about.
public record PaymentFixture
{
    public Guid? Id { get; init; }
    public Guid? IdempotencyKey { get; init; } = Guid.NewGuid();
    public string CardNumber { get; init; } = RandomCardNumber();
    public int ExpiryMonth { get; init; } = Random.Shared.Next(1, 13);
    public int ExpiryYear { get; init; } = DateTime.UtcNow.Year + Random.Shared.Next(1, 6);
    public string Currency { get; init; } = SupportedCurrencies.All[Random.Shared.Next(SupportedCurrencies.All.Length)];
    public int Amount { get; init; } = Random.Shared.Next(1, 100_000);
    public string Cvv { get; init; } = Random.Shared.Next(100, 1000).ToString();

    // The bank simulator decides authorized/declined/unavailable purely from the card number's last digit.
    public PaymentFixture WithCardNumberEndingIn(char digit) => this with { CardNumber = $"{CardNumber[..^1]}{digit}" };

    private static string RandomCardNumber() =>
        string.Concat(Enumerable.Range(0, 16).Select(_ => Random.Shared.Next(0, 10)));

    public PostPaymentRequest ToRequest() => new()
    {
        IdempotencyKey = IdempotencyKey,
        CardNumber = CardNumber,
        ExpiryMonth = ExpiryMonth,
        ExpiryYear = ExpiryYear,
        Currency = Currency,
        Amount = Amount,
        Cvv = Cvv
    };
}