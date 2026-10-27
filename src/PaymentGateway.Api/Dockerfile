FROM mcr.microsoft.com/dotnet/sdk:8.0.427-bookworm-slim AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/PaymentGateway.Api/PaymentGateway.Api.csproj src/PaymentGateway.Api/
RUN dotnet restore src/PaymentGateway.Api/PaymentGateway.Api.csproj

COPY src/PaymentGateway.Api/ src/PaymentGateway.Api/
RUN dotnet publish src/PaymentGateway.Api/PaymentGateway.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0.33-bookworm-slim AS final
WORKDIR /app

# Pick up Debian security fixes released after the base image was built.
RUN apt-get update \
    && apt-get install -y --only-upgrade openssl libssl3 \
    && rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# The runtime image has no curl/wget, so probe /health with bash's built-in TCP support.
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "PaymentGateway.Api.dll"]
