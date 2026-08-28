FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MzansiMarket.slnx ./
COPY src/MzansiMarket.Api/MzansiMarket.Api.csproj src/MzansiMarket.Api/
RUN dotnet restore src/MzansiMarket.Api/MzansiMarket.Api.csproj

COPY src/MzansiMarket.Api/ src/MzansiMarket.Api/
RUN dotnet publish src/MzansiMarket.Api/MzansiMarket.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
EXPOSE 10000
USER app

ENTRYPOINT ["/bin/sh", "-c", "exec dotnet MzansiMarket.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
