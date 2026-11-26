# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between source changes.
COPY Directory.Build.props RepairHub.slnx ./
COPY src/RepairHub.Api/RepairHub.Api.csproj src/RepairHub.Api/
COPY src/RepairHub.Application/RepairHub.Application.csproj src/RepairHub.Application/
COPY src/RepairHub.Domain/RepairHub.Domain.csproj src/RepairHub.Domain/
COPY src/RepairHub.Infrastructure/RepairHub.Infrastructure.csproj src/RepairHub.Infrastructure/
RUN dotnet restore src/RepairHub.Api/RepairHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/RepairHub.Api/RepairHub.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Built-in non-root user of the official .NET images.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RepairHub.Api.dll"]
