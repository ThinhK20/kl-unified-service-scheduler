# Build context: backend/   (docker build -f src/Scheduler.Api/Dockerfile .)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Optional: trust an extra CA during the build only (corporate/TLS-inspecting proxies):
#   docker build --secret id=extra_ca,src=/path/ca.crt ...
# The certificate never reaches the final image.
RUN --mount=type=secret,id=extra_ca,required=false \
    if [ -f /run/secrets/extra_ca ]; then \
      cp /run/secrets/extra_ca /usr/local/share/ca-certificates/extra_ca.crt && update-ca-certificates; \
    fi

# Restore first (cached while only code changes).
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Scheduler.Domain/Scheduler.Domain.csproj src/Scheduler.Domain/
COPY src/Scheduler.Application/Scheduler.Application.csproj src/Scheduler.Application/
COPY src/Scheduler.Infrastructure/Scheduler.Infrastructure.csproj src/Scheduler.Infrastructure/
COPY src/Scheduler.Api/Scheduler.Api.csproj src/Scheduler.Api/
RUN dotnet restore src/Scheduler.Api/Scheduler.Api.csproj

COPY src/ src/
RUN dotnet publish src/Scheduler.Api/Scheduler.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
# Built-in non-root user of the .NET images (UID 1654).
USER $APP_UID
ENTRYPOINT ["dotnet", "Scheduler.Api.dll"]
