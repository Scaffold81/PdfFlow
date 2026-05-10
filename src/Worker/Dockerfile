FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Worker.csproj", "./"]
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app

# Mount point for shared storage volume
RUN mkdir -p /app/storage

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Worker.dll"]
