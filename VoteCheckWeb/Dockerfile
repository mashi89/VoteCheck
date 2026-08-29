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

# Run unprivileged; the image serves public open data and needs no write access.
USER $APP_UID

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VoteCheckWeb.dll"]
