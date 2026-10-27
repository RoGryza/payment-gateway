using System.Text.Json.Serialization;

using PaymentGateway.Api.Exceptions;

namespace PaymentGateway.Api.Services;

public enum BankAuthorizationOutcome
{
    Authorized,
    Declined
}

// The bank itself is down/slow.
public class BankUnavailableException : Exception, IApplicationException
{
    public BankUnavailableException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }

    public ApplicationExceptionOrigin Origin => ApplicationExceptionOrigin.Dependency;
    public string? DependencyName => BankClient.DependencyName;
}

// There is an internal issue with our integration.
public class BankIntegrationException : Exception, IApplicationException
{
    public BankIntegrationException(string message) : base(message)
    {
    }

    public ApplicationExceptionOrigin Origin => ApplicationExceptionOrigin.Service;
    public string? DependencyName => BankClient.DependencyName;
}

public class BankClient
{
    public const string DependencyName = "acquiring_bank";

    private readonly HttpClient _httpClient;

    public BankClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BankAuthorizationOutcome> AuthorizeAsync(PaymentSubmission request, CancellationToken cancellationToken)
    {
        var bankRequest = new
        {
            card_number = request.CardNumber,
            expiry_date = $"{request.ExpiryMonth:D2}/{request.ExpiryYear}",
            currency = request.Currency,
            amount = request.Amount,
            cvv = request.Cvv
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/payments", bankRequest, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BankUnavailableException("Acquiring bank was unreachable", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BankUnavailableException("Acquiring bank timed out", exception);
        }

        using (response)
        {
            return await ReadOutcomeAsync(response, cancellationToken);
        }
    }

    private static async Task<BankAuthorizationOutcome> ReadOutcomeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // We validate before ever calling the bank, so a 400 here means our own request was
            // malformed - anything else is the bank's own problem, not ours.
            if ((int)response.StatusCode == 400)
            {
                throw new BankIntegrationException($"Acquiring bank rejected our request as malformed: {response.StatusCode}");
            }

            throw new BankUnavailableException($"Acquiring bank returned {response.StatusCode}");
        }

        var body = await response.Content.ReadFromJsonAsync<BankResponse>(cancellationToken: cancellationToken);
        return body is { Authorized: true } ? BankAuthorizationOutcome.Authorized : BankAuthorizationOutcome.Declined;
    }

    private record BankResponse([property: JsonPropertyName("authorized")] bool Authorized);
}