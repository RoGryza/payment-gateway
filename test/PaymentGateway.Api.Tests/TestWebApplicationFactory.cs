using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<PaymentsController>
{
    public PaymentsRepository Repository { get; } = new();

    public System.Text.Json.JsonSerializerOptions JsonOptions =>
        Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services => services.AddSingleton(Repository));
    }
}