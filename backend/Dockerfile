FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY Skinora.sln .
COPY src/Skinora.API/Skinora.API.csproj src/Skinora.API/
COPY src/Skinora.Shared/Skinora.Shared.csproj src/Skinora.Shared/
COPY src/Modules/Skinora.Transactions/Skinora.Transactions.csproj src/Modules/Skinora.Transactions/
COPY src/Modules/Skinora.Payments/Skinora.Payments.csproj src/Modules/Skinora.Payments/
COPY src/Modules/Skinora.Steam/Skinora.Steam.csproj src/Modules/Skinora.Steam/
COPY src/Modules/Skinora.Users/Skinora.Users.csproj src/Modules/Skinora.Users/
COPY src/Modules/Skinora.Auth/Skinora.Auth.csproj src/Modules/Skinora.Auth/
COPY src/Modules/Skinora.Notifications/Skinora.Notifications.csproj src/Modules/Skinora.Notifications/
COPY src/Modules/Skinora.Admin/Skinora.Admin.csproj src/Modules/Skinora.Admin/
COPY src/Modules/Skinora.Disputes/Skinora.Disputes.csproj src/Modules/Skinora.Disputes/
COPY src/Modules/Skinora.Fraud/Skinora.Fraud.csproj src/Modules/Skinora.Fraud/
COPY src/Modules/Skinora.Platform/Skinora.Platform.csproj src/Modules/Skinora.Platform/
COPY tests/Skinora.API.Tests/Skinora.API.Tests.csproj tests/Skinora.API.Tests/
COPY tests/Skinora.Transactions.Tests/Skinora.Transactions.Tests.csproj tests/Skinora.Transactions.Tests/
COPY tests/Skinora.Payments.Tests/Skinora.Payments.Tests.csproj tests/Skinora.Payments.Tests/
COPY tests/Skinora.Steam.Tests/Skinora.Steam.Tests.csproj tests/Skinora.Steam.Tests/
COPY tests/Skinora.Users.Tests/Skinora.Users.Tests.csproj tests/Skinora.Users.Tests/
COPY tests/Skinora.Auth.Tests/Skinora.Auth.Tests.csproj tests/Skinora.Auth.Tests/
COPY tests/Skinora.Notifications.Tests/Skinora.Notifications.Tests.csproj tests/Skinora.Notifications.Tests/
COPY tests/Skinora.Admin.Tests/Skinora.Admin.Tests.csproj tests/Skinora.Admin.Tests/
COPY tests/Skinora.Disputes.Tests/Skinora.Disputes.Tests.csproj tests/Skinora.Disputes.Tests/
COPY tests/Skinora.Fraud.Tests/Skinora.Fraud.Tests.csproj tests/Skinora.Fraud.Tests/
COPY tests/Skinora.Platform.Tests/Skinora.Platform.Tests.csproj tests/Skinora.Platform.Tests/
COPY tests/Skinora.Shared.Tests/Skinora.Shared.Tests.csproj tests/Skinora.Shared.Tests/

RUN dotnet restore

# Copy everything and build
COPY . .
RUN dotnet publish src/Skinora.API/Skinora.API.csproj -c Release -o /app/publish --no-restore

FROM base AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Skinora.API.dll"]
