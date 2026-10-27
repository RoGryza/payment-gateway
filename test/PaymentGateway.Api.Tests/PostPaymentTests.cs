using System.Net;
using System.Net.Http.Json;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Helpers;

namespace PaymentGateway.Api.Tests;

public class PostPaymentTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public PostPaymentTests()
    {
        _client = _factory.CreateClient();
    }

    // Derived factories are disposed along with _factory.
    private HttpClient ClientWith(Action<IServiceCollection> configure) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(configure)).CreateClient();

    private Task<HttpResponseMessage> PostRaw(string json) =>
        _client.PostAsync("/api/Payments", new StringContent(json, Encoding.UTF8, "application/json"));

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PostPaymentWithUnevenCardNumber_AuthorizesAndPersists()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1');

        var response = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(payment);
        fixture = fixture with { Id = payment.Id };
        Then.PaymentMatches(fixture, payment, PaymentStatus.Authorized);
        Then.PaymentIsStored(_factory.Repository, fixture, PaymentStatus.Authorized);
    }

    [Fact]
    public async Task PostPaymentWithEvenCardNumber_DeclinesAndPersists()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('2');

        var response = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(payment);
        fixture = fixture with { Id = payment.Id };
        Then.PaymentMatches(fixture, payment, PaymentStatus.Declined);
        Then.PaymentIsStored(_factory.Repository, fixture, PaymentStatus.Declined);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public async Task PostPaymentWithExpiryYear_IsRejected(int yearOffset, int monthOffset)
    {
        // Pin now away from any month/year boundary.
        var now = new DateTimeOffset(2030, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var client = ClientWith(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(now)));
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1') with { ExpiryYear = now.Year + yearOffset, ExpiryMonth = now.Month + monthOffset };

        var response = await client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(payment);
        fixture = fixture with { Id = payment.Id };
        Then.PaymentMatches(fixture, payment, PaymentStatus.Rejected);
        Then.PaymentIsStored(_factory.Repository, fixture, PaymentStatus.Rejected);
    }

    [Fact]
    public async Task PostPaymentWhenBankUnavailable_Returns503AndLeavesPaymentUnresolved()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('0');

        var response = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Then.PaymentIsUnresolved(_factory.Repository, fixture);
    }

    [Fact]
    public async Task PostPaymentWhenBankUnreachable_Returns503AndLeavesPaymentUnresolved()
    {
        var client = ClientWith(services => services.AddHttpClient<BankClient>(c => c.BaseAddress = new Uri("http://localhost:1")));
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1'); // would be Authorized if the bank were reachable

        var response = await client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Then.PaymentIsUnresolved(_factory.Repository, fixture);
    }

    [Fact]
    public async Task PostPaymentWhenBankTimesOut_Returns503AndLeavesPaymentUnresolved()
    {
        var client = ClientWith(services => services.AddHttpClient<BankClient>()
            .ConfigurePrimaryHttpMessageHandler(StubHttpMessageHandler.TimingOut));
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1');

        var response = await client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Then.PaymentIsUnresolved(_factory.Repository, fixture);
    }

    [Fact]
    public async Task PostPaymentWhenBankRejectsOurRequest_Returns500AndLeavesPaymentUnresolved()
    {
        var client = ClientWith(services => services.AddHttpClient<BankClient>()
            .ConfigurePrimaryHttpMessageHandler(() => StubHttpMessageHandler.Responding(HttpStatusCode.BadRequest)));
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1');

        var response = await client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Then.PaymentIsUnresolved(_factory.Repository, fixture);
    }

    [Fact]
    public async Task PostPaymentTwiceWithSameFixture_ReturnsSameResolvedPayment()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1');

        var first = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());
        var second = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstPayment = await first.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        var secondPayment = await second.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(firstPayment);
        Assert.NotNull(secondPayment);
        Assert.Equal(firstPayment.Id, secondPayment.Id);
        Then.PaymentMatches(fixture with { Id = firstPayment.Id }, secondPayment, PaymentStatus.Authorized);
    }

    [Fact]
    public async Task PostPaymentTwiceWithSameKeyDifferentData_ReturnsConflict()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1');

        await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());
        var retry = await _client.PostAsJsonAsync("/api/Payments", (fixture with { Amount = fixture.Amount + 1 }).ToRequest());

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    [Fact]
    public async Task PostPaymentWithSameKeyWhileFirstAttemptUnresolved_ReturnsConflict()
    {
        var fixture = await Given.AnUnknownPayment(_factory, _client);

        var retry = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    // Known limitation: a Rejected payment is a real, persisted payment, so it claims its key like any other.
    [Fact]
    public async Task PostCorrectedPaymentWithKeyOfRejectedPayment_ReturnsConflict()
    {
        var invalid = new PaymentFixture().WithCardNumberEndingIn('1') with { ExpiryYear = 2020 };
        await _client.PostAsJsonAsync("/api/Payments", invalid.ToRequest());

        var corrected = await _client.PostAsJsonAsync("/api/Payments", (invalid with { ExpiryYear = DateTime.UtcNow.Year + 5 }).ToRequest());

        Assert.Equal(HttpStatusCode.Conflict, corrected.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidPayments))]
    public async Task PostPaymentWithInvalidRequest_RejectsAndPersists(string because, PaymentFixture fixture)
    {
        var response = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(payment);
        Assert.True(payment.Status == PaymentStatus.Rejected, $"expected Rejected because: {because}");
        fixture = fixture with { Id = payment.Id };
        Then.PaymentMatches(fixture, payment, PaymentStatus.Rejected);
        Then.PaymentIsStored(_factory.Repository, fixture, PaymentStatus.Rejected);
    }

    [Theory]
    [InlineData("cardNumber", true)]
    [InlineData("expiryMonth", true)]
    [InlineData("expiryYear", true)]
    [InlineData("currency", true)]
    [InlineData("amount", true)]
    [InlineData("cvv", true)]
    [InlineData("cardNumber", false)]
    [InlineData("expiryMonth", false)]
    [InlineData("expiryYear", false)]
    [InlineData("currency", false)]
    [InlineData("amount", false)]
    [InlineData("cvv", false)]
    public async Task PostPaymentWithMissingField_ReturnsBadRequestAndPersistsNothing(string field, bool explicitNull)
    {
        var key = Guid.NewGuid();
        var fields = new Dictionary<string, string>
        {
            ["idempotencyKey"] = $"\"{key}\"",
            ["cardNumber"] = "\"1234567890123451\"",
            ["expiryMonth"] = "4",
            ["expiryYear"] = "2030",
            ["currency"] = "\"GBP\"",
            ["amount"] = "100",
            ["cvv"] = "\"123\""
        };
        if (explicitNull)
        {
            fields[field] = "null";
        }
        else
        {
            fields.Remove(field);
        }
        var json = "{" + string.Join(",", fields.Select(f => $"\"{f.Key}\":{f.Value}")) + "}";

        var response = await PostRaw(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_factory.Repository.GetByIdempotencyKey(key));
    }

    [Fact]
    public async Task PostPaymentWithUnparseableField_ReturnsBadRequest()
    {
        var json = $"{{\"idempotencyKey\":\"{Guid.NewGuid()}\",\"cardNumber\":\"1234567890123456\"," +
            "\"expiryMonth\":4,\"expiryYear\":2030,\"currency\":\"GBP\",\"amount\":\"not-a-number\",\"cvv\":\"123\"}";

        var response = await PostRaw(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostPaymentWithMalformedJson_ReturnsBadRequest()
    {
        var response = await PostRaw("{\"idempotencyKey\":");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostPaymentWithEmptyIdempotencyKey_ReturnsBadRequestAndPersistsNothing()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1') with { IdempotencyKey = Guid.Empty };

        var response = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_factory.Repository.GetByIdempotencyKey(Guid.Empty));
    }

    [Fact]
    public async Task PostPaymentWithoutIdempotencyKey_ProcessesEveryRequestIndependently()
    {
        var fixture = new PaymentFixture().WithCardNumberEndingIn('1') with { IdempotencyKey = null };

        var first = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());
        var second = await _client.PostAsJsonAsync("/api/Payments", fixture.ToRequest());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstPayment = await first.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        var secondPayment = await second.Content.ReadFromJsonAsync<PostPaymentResponse>(_factory.JsonOptions);
        Assert.NotNull(firstPayment);
        Assert.NotNull(secondPayment);
        Assert.NotEqual(firstPayment.Id, secondPayment.Id);
        Then.PaymentMatches(fixture with { Id = firstPayment.Id }, firstPayment, PaymentStatus.Authorized);
        Then.PaymentIsStored(_factory.Repository, fixture with { Id = secondPayment.Id }, PaymentStatus.Authorized);
    }

    public static IEnumerable<object[]> InvalidPayments()
    {
        var valid = new PaymentFixture().WithCardNumberEndingIn('1');

        yield return new object[] { "card number shorter than 14 digits", valid with { CardNumber = "1234567890123" } };
        yield return new object[] { "card number longer than 19 digits", valid with { CardNumber = "123456789012345678901" } };
        yield return new object[] { "non-numeric card number", valid with { CardNumber = "1234abcd12345678" } };
        yield return new object[] { "expiry date in the past", valid with { ExpiryYear = 2020, ExpiryMonth = 6 } };
        yield return new object[] { "expiry year above 9999", valid with { ExpiryYear = 10000 } };
        yield return new object[] { "expiry month above 12", valid with { ExpiryMonth = 13 } };
        yield return new object[] { "expiry month below 1", valid with { ExpiryMonth = 0 } };
        yield return new object[] { "unsupported currency", valid with { Currency = "JPY" } };
        yield return new object[] { "cvv shorter than 3 digits", valid with { Cvv = "12" } };
        yield return new object[] { "cvv longer than 4 digits", valid with { Cvv = "12345" } };
        yield return new object[] { "non-numeric cvv", valid with { Cvv = "12a" } };
        yield return new object[] { "non-positive amount", valid with { Amount = 0 } };
    }
}