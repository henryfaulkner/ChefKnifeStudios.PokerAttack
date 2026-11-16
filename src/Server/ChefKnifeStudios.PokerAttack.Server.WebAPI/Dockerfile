FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["./src", "/src"]
WORKDIR /src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG SOURCE_REVISION=uncommitted
LABEL org.opencontainers.image.revision=$SOURCE_REVISION
LABEL com.chefknifestudios.pokerattack.release-capability=1
ENV POKERATTACK_SOURCE_REVISION=$SOURCE_REVISION
WORKDIR /app
ENV ASPNETCORE_URLS=http://127.0.0.1:8081 \
    DOTNET_EnableDiagnostics=0 \
    HOME=/tmp
EXPOSE 8081

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "ChefKnifeStudios.PokerAttack.Server.WebAPI.dll"]

# Host networking is intentional on Linode; see deploy/linode/compose.yaml.
