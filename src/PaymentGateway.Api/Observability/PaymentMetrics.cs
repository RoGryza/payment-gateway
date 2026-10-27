using System.Diagnostics.Metrics;

using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Observability;

public class PaymentMetrics
{
    public const string MeterName = "PaymentGateway.Api";

    private readonly Counter<long> _paymentsProcessed;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _paymentsProcessed = meter.CreateCounter<long>(
            "payments.processed",
            unit: "{payment}",
            description: "Number of payments processed, by resulting status and currency.");
    }

    public void RecordPayment(PaymentProcessingStatus status, string currency) =>
        _paymentsProcessed.Add(1,
            new KeyValuePair<string, object?>("status", status.ToString()),
            new KeyValuePair<string, object?>("currency", SupportedCurrencies.ToMetricLabel(currency)));
}