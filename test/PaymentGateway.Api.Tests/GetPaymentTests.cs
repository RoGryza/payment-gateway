using System.Net;
using System.Net.Http.Json;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Tests.Helpers;

namespace PaymentGateway.Api.Tests;

public class GetPaymentTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public GetPaymentTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Rejected)]
    public async Task RetrievesAPaymentSuccessfully(PaymentStatus status)
    {
        var fixture = await Given.APayment(_factory, _client, status);

        var response = await _client.GetAsync($"/api/Payments/{fixture.Id!.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GetPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(body);
        Then.PaymentMatches(fixture, body, status);
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        var response = await _client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUnresolvedPayment_ReturnsInternalServerError()
    {
        var fixture = await Given.AnUnknownPayment(_factory, _client);

        var response = await _client.GetAsync($"/api/Payments/{fixture.Id!.Value}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}