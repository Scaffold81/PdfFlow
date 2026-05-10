FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ApiGateway.csproj", "./"]
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app

# Create storage directory
RUN mkdir -p /app/storage

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
