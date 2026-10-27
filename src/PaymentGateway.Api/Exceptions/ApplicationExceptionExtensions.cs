namespace PaymentGateway.Api.Exceptions;

public static class ApplicationExceptionExtensions
{
    public static ApplicationExceptionOrigin GetApplicationExceptionOrigin(this Exception exception) =>
        (exception as IApplicationException)?.Origin ?? ApplicationExceptionOrigin.Service;

    public static string? GetDependencyName(this Exception exception) =>
        (exception as IApplicationException)?.DependencyName;
}