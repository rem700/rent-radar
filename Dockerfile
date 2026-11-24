# One image definition for both .NET services: --build-arg PROJECT=RentRadar.Api | RentRadar.Worker
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/RentRadar.Domain/RentRadar.Domain.csproj src/RentRadar.Domain/
COPY src/RentRadar.Contracts/RentRadar.Contracts.csproj src/RentRadar.Contracts/
COPY src/RentRadar.Scraping/RentRadar.Scraping.csproj src/RentRadar.Scraping/
COPY src/RentRadar.ServiceDefaults/RentRadar.ServiceDefaults.csproj src/RentRadar.ServiceDefaults/
COPY src/RentRadar.Api/RentRadar.Api.csproj src/RentRadar.Api/
COPY src/RentRadar.Worker/RentRadar.Worker.csproj src/RentRadar.Worker/
ARG PROJECT=RentRadar.Api
RUN dotnet restore src/${PROJECT}/${PROJECT}.csproj
COPY src/ src/
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT=RentRadar.Api
ENV APP_DLL=${PROJECT}.dll \
    Urls=http://0.0.0.0:8080
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
