FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
COPY database/ database/
RUN dotnet publish src/JuNotes.Frontend/JuNotes.Frontend.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0
# pg_dump has to be at least as new as the database, so the client comes from the PostgreSQL project's own repository.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && install -d /usr/share/postgresql-common/pgdg \
 && curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
 && . /etc/os-release \
 && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends postgresql-client-18 \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data/backups && chown -R app:app /data

ENV ASPNETCORE_URLS=http://+:8080 \
    Backup__Directory=/data/backups \
    Backup__PgDump=/usr/lib/postgresql/18/bin/pg_dump
WORKDIR /app
COPY --from=build /app .
USER app
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "JuNotes.Frontend.dll"]
