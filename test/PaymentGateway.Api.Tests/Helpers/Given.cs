using System.Net.Http.Json;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests.Helpers;

// Sets up pre-existing state via the real API.
public static class Given
{
    public static async Task<PaymentFixture> APayment(TestWebApplicationFactory factory, HttpClient client, PaymentStatus status = PaymentStatus.Authorized)
    {
        var payment = status switch
        {
            PaymentStatus.Authorized => new PaymentFixture().WithCardNumberEndingIn('1'),
            PaymentStatus.Declined => new PaymentFixture().WithCardNumberEndingIn('2'),
            PaymentStatus.Rejected => new PaymentFixture().WithCardNumberEndingIn('1') with { Cvv = "12" },
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "no fixture for this status")
        };

        var response = await client.PostAsJsonAsync("/api/Payments", payment.ToRequest());
        var created = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(factory.JsonOptions);
        return payment with { Id = created!.Id };
    }

    public static async Task<PaymentFixture> AnUnknownPayment(TestWebApplicationFactory factory, HttpClient client)
    {
        var payment = new PaymentFixture().WithCardNumberEndingIn('0');
        await client.PostAsJsonAsync("/api/Payments", payment.ToRequest());
        var stored = factory.Repository.GetByIdempotencyKey(payment.IdempotencyKey!.Value);
        return payment with { Id = stored!.Id };
    }
}