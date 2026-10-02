FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["customer-api.csproj", "."]
RUN dotnet restore "customer-api.csproj"
COPY . .
RUN dotnet publish "customer-api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# curl is what the compose healthcheck and the HEALTHCHECK below call; the aspnet image omits it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup --no-create-home appuser

# DSR-2 access-request exports (Customer__DsrExportPath); compose mounts a volume here.
RUN mkdir -p /var/crm/dsr-exports && chown -R appuser:appgroup /var/crm/dsr-exports

COPY --from=build --chown=appuser:appgroup /app/publish .
USER appuser

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "customer-api.dll"]
