# Build from the repository root:
#   docker build -f VoteCheck.Api/Dockerfile -t votecheck-api .
#   docker run -p 8080:8080 votecheck-api
#
# Then browse http://localhost:8080/swagger.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against the project files alone first, so the layer cache survives source edits.
COPY VoteCheck.Core/VoteCheck.Core.csproj VoteCheck.Core/
COPY VoteCheck.Api/VoteCheck.Api.csproj VoteCheck.Api/
RUN dotnet restore VoteCheck.Api/VoteCheck.Api.csproj

COPY VoteCheck.Core/ VoteCheck.Core/
COPY VoteCheck.Api/ VoteCheck.Api/
RUN dotnet publish VoteCheck.Api/VoteCheck.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run unprivileged; the image serves public open data and needs no write access.
USER $APP_UID

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VoteCheck.Api.dll"]
