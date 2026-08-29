# Build from the repository root:
#   docker build -f VoteCheckWeb/Dockerfile -t votecheck .
#   docker run -p 8080:8080 votecheck
#
# Then browse http://localhost:8080 for the site, /swagger for the JSON API.
#
# The mirror lives at VoteCheck:DbPath and should be on a persistent volume:
# it is rebuildable from the API, but re-backfilling on every restart is rude
# to upstream and slow to start.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against the project files alone first, so the layer cache survives source edits.
COPY VoteCheck.Core/VoteCheck.Core.csproj VoteCheck.Core/
COPY VoteCheckWeb/VoteCheckWeb.csproj VoteCheckWeb/
RUN dotnet restore VoteCheckWeb/VoteCheckWeb.csproj

COPY VoteCheck.Core/ VoteCheck.Core/
COPY VoteCheckWeb/ VoteCheckWeb/
RUN dotnet publish VoteCheckWeb/VoteCheckWeb.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app .

# The sync writes continuously, and SQLite in WAL mode needs to create -wal/-shm files
# beside the database, so it needs a writable *directory*, not just a writable file.
# /data is that directory and is where a volume should be mounted; without one the
# mirror is lost on every restart and re-backfills from scratch.
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
VOLUME ["/data"]
ENV VoteCheck__DbPath=/data/votecheck.db

# Run unprivileged. Everything the app writes lives under /data, which it owns.
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# No HEALTHCHECK here on purpose: the runtime image ships no curl or wget, and a check
# that only proves the runtime exists (`dotnet --info`) would report healthy while the
# app is down. Point your orchestrator's HTTP probe at /health instead.

ENTRYPOINT ["dotnet", "VoteCheckWeb.dll"]
