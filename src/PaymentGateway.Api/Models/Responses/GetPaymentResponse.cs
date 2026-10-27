namespace PaymentGateway.Api.Models.Responses;

public class GetPaymentResponse
{
    public Guid Id { get; set; }
    public PaymentStatus Status { get; set; }
    public required string CardNumberLastFour { get; set; }
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public required string Currency { get; set; }
    public int Amount { get; set; }
}