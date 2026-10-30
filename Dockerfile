FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/AssignHub.Domain/AssignHub.Domain.csproj src/AssignHub.Domain/
COPY src/AssignHub.Application/AssignHub.Application.csproj src/AssignHub.Application/
COPY src/AssignHub.Infrastructure/AssignHub.Infrastructure.csproj src/AssignHub.Infrastructure/
COPY src/AssignHub.Api/AssignHub.Api.csproj src/AssignHub.Api/
RUN dotnet restore src/AssignHub.Api/AssignHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/AssignHub.Api/AssignHub.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "AssignHub.Api.dll"]
