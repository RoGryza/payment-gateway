namespace PaymentGateway.Api.Tests.Helpers;

// Stands in for the bank at the HTTP layer, for failures the real simulator cannot be made to produce.
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public static StubHttpMessageHandler Responding(System.Net.HttpStatusCode statusCode) =>
        new(() => new HttpResponseMessage(statusCode));

    // What HttpClient throws when its Timeout elapses.
    public static StubHttpMessageHandler TimingOut() =>
        new(() => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException()));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_respond());
}