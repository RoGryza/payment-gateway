namespace PaymentGateway.Api.Models;

public record Payment
{
    public required Guid Id { get; init; }
    public Guid? IdempotencyKey { get; init; }
    public required PaymentProcessingStatus Status { get; init; }
    public required string CardNumberLastFour { get; init; }
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public required string Currency { get; init; }
    public int Amount { get; init; }
}