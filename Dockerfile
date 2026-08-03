FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["FacilReports/FacilReports.csproj", "FacilReports/"]
RUN dotnet restore "FacilReports/FacilReports.csproj"

# Copy everything and build
COPY FacilReports/ FacilReports/
RUN dotnet publish "FacilReports/FacilReports.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app

# Install curl for the Docker healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Copy Reports directory
COPY FacilReports/Reports/ /app/Reports/

# Environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FacilReports.dll"]
