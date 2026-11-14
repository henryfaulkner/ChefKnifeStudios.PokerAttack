FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["./src", "/src"]

WORKDIR /src/Server/ChefKnifeStudios.PokerAttack.Server.Data

RUN dotnet tool install dotnet-ef -g --version 9.0.8
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --self-contained -r linux-x64 --output /app/publish/chefknifestudios.pokerattack.efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
ARG SOURCE_REVISION=uncommitted
LABEL org.opencontainers.image.revision=$SOURCE_REVISION
WORKDIR /app

COPY --from=build /app/publish .

RUN chmod +x ./chefknifestudios.pokerattack.efbundle
USER $APP_UID
ENTRYPOINT ["./chefknifestudios.pokerattack.efbundle"]
