# Rogue Stars server. Build from the repository root:
#   docker build -f server/Dockerfile -t rogue-stars-server .
# Multi-stage: the SDK image restores/publishes the shared rules + server; the runtime image runs as non-root on 8080.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Restore first (layer cache): only project files and build props.
COPY Directory.Build.props ./
COPY shared/RogueStars.Rules/RogueStars.Rules.csproj shared/RogueStars.Rules/
COPY server/RogueStars.Server/RogueStars.Server.csproj server/RogueStars.Server/
RUN dotnet restore server/RogueStars.Server/RogueStars.Server.csproj

COPY shared/RogueStars.Rules/ shared/RogueStars.Rules/
COPY server/RogueStars.Server/ server/RogueStars.Server/
RUN dotnet publish server/RogueStars.Server/RogueStars.Server.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
# Built-in non-root user of the official .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "RogueStars.Server.dll"]
