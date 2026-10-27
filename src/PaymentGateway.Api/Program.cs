using System.Text.Json.Serialization;

using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using PaymentGateway.Api.Middleware;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// A validation failure becomes a persisted Rejected payment (200), not [ApiController]'s automatic 400 -
// so the action must run even when ModelState is invalid, and check it itself.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentsRepository>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddHttpClient<BankClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AcquiringBank:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

builder.Services.AddHealthChecks();

builder.Services.AddMetrics();
builder.Services.AddSingleton<PaymentMetrics>();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();
builder.Services.AddProblemDetails();

var otlpEndpoint = builder.Configuration["Telemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("PaymentGateway.Api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(PaymentMetrics.MeterName)
        .AddPrometheusExporter());

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseWhen(
    context => context.Request.Path.Value is not ("/health" or "/metrics"),
    branch => branch.UseHttpLogging());

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();