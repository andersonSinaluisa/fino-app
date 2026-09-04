# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so a code-only change reuses the package layer.
COPY Directory.Build.props Directory.Packages.props Nexo.sln ./
COPY src/Nexo.Domain/Nexo.Domain.csproj src/Nexo.Domain/
COPY src/Nexo.Application/Nexo.Application.csproj src/Nexo.Application/
COPY src/Nexo.Infrastructure/Nexo.Infrastructure.csproj src/Nexo.Infrastructure/
COPY src/Nexo.Workers/Nexo.Workers.csproj src/Nexo.Workers/
COPY src/Nexo.Api/Nexo.Api.csproj src/Nexo.Api/
COPY tests/Nexo.Domain.Tests/Nexo.Domain.Tests.csproj tests/Nexo.Domain.Tests/
COPY tests/Nexo.Application.Tests/Nexo.Application.Tests.csproj tests/Nexo.Application.Tests/
COPY tests/Nexo.Api.IntegrationTests/Nexo.Api.IntegrationTests.csproj tests/Nexo.Api.IntegrationTests/
RUN dotnet restore Nexo.sln

COPY . .
RUN dotnet publish src/Nexo.Api/Nexo.Api.csproj -c Release -o /app --no-restore

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Never run the API as root.
RUN adduser --disabled-password --gecos "" --uid 10001 nexo
USER 10001

COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nexo.Api.dll"]
