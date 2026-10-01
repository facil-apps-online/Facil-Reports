FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Licencia de DevExpress (variable oficial DevExpress_License) — se valida al compilar, en esta
# etapa de build.
ARG DEVEXPRESS_LICENSE_KEY=""
ENV DevExpress_License=${DEVEXPRESS_LICENSE_KEY}

# Feed privado de NuGet de DevExpress (URL con token de nuestra cuenta) — solo aquí, nunca en un
# nuget.config versionado en el repo. Necesario porque estas versiones no están en nuget.org.
ARG DEVEXPRESS_NUGET_FEED_URL=""
RUN if [ -n "$DEVEXPRESS_NUGET_FEED_URL" ]; then dotnet nuget add source "$DEVEXPRESS_NUGET_FEED_URL" -n DevExpress; fi

# Copy csproj and restore
COPY ["FacilReports/FacilReports.csproj", "FacilReports/"]
RUN dotnet restore "FacilReports/FacilReports.csproj"

# Copy everything and build
COPY FacilReports/ FacilReports/
RUN dotnet publish "FacilReports/FacilReports.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app

# curl para el healthcheck. libgdiplus: DevExpress 22.1 (System.Drawing.Common por debajo, no el
# renderizador propio que trajeron versiones posteriores) lo necesita para dibujar en Linux — sin
# esto falla con "Unable to load shared library 'libgdiplus'" al exportar el primer PDF.
# ghostscript: recorta las fuentes embebidas del PDF vectorial (de ~1 MB a ~150 KB por factura).
# fonts-liberation (equivale a Arial) y fonts-dejavu-core (DejaVu Sans / Sans Mono): las plantillas
# estándar usan esas tipografías y el contenedor no trae ninguna.
RUN apt-get update && apt-get install -y --no-install-recommends curl libgdiplus ghostscript fontconfig fonts-liberation fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Copy Reports directory
COPY FacilReports/Reports/ /app/Reports/

# Environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FacilReports.dll"]
