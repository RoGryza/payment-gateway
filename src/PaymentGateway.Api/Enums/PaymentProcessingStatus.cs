namespace PaymentGateway.Api.Models;

// Internal payment status.
public enum PaymentProcessingStatus
{
    Unresolved,
    Authorized,
    Declined,
    Rejected
}