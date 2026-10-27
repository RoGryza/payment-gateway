using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Exceptions;

namespace PaymentGateway.Api.Middleware;

public class ApplicationExceptionHandler : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    private readonly ILogger<ApplicationExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetails;

    public ApplicationExceptionHandler(ILogger<ApplicationExceptionHandler> logger, IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Request was aborted by the client");
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var origin = exception.GetApplicationExceptionOrigin();
        var dependencyName = exception.GetDependencyName();

        var activity = Activity.Current;
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);
        activity?.SetTag("error.origin", origin.ToString());
        activity?.SetTag("error.dependency_name", dependencyName);

        _logger.LogError(exception, "Unhandled exception (origin: {Origin}, dependency: {DependencyName})", origin, dependencyName);

        var tags = httpContext.Features.Get<IHttpMetricsTagsFeature>()?.Tags;
        tags?.Add(new KeyValuePair<string, object?>("error_origin", origin.ToString()));
        if (dependencyName is not null)
        {
            tags?.Add(new KeyValuePair<string, object?>("error_dependency_name", dependencyName));
        }

        var (statusCode, title) = origin == ApplicationExceptionOrigin.Dependency
            ? (StatusCodes.Status503ServiceUnavailable, "Service unavailable")
            : (StatusCodes.Status500InternalServerError, "Internal server error");

        httpContext.Response.StatusCode = statusCode;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = statusCode, Title = title }
        });
    }
}