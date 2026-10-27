namespace PaymentGateway.Api.Models;

public static class SupportedCurrencies
{
    public const string Usd = "USD";
    public const string Gbp = "GBP";
    public const string Eur = "EUR";

    public static readonly string[] All = { Usd, Gbp, Eur };

    public static string ToMetricLabel(string? currency) =>
        currency is not null && All.Contains(currency) ? currency : "other";
}