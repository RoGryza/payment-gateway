namespace PaymentGateway.Api.Exceptions;

public enum ApplicationExceptionOrigin
{
    // This service.
    Service,

    // External dependency.
    Dependency
}

// Exception classification for observability.
// Exceptions that don't implement this default to Service errors.
public interface IApplicationException
{
    ApplicationExceptionOrigin Origin { get; }
    string? DependencyName { get; }
}