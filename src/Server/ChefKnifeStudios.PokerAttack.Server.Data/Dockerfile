FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["./src", "/src"]

WORKDIR /src/ChefKnifeStudios.PokerAttack.Server.Data

RUN dotnet build -c Release

FROM build AS publish

WORKDIR /src/ChefKnifeStudios.PokerAttack.Server.Data

RUN dotnet tool install dotnet-ef -g --version 9.0.5 
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --self-contained -r linux-x64 --output /app/publish/chefknifestudios.pokerattack.efbundle

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

COPY --from=publish /app/publish .

RUN chmod +x ./chefknifestudios.pokerattack.efbundle
ENTRYPOINT ./chefknifestudios.pokerattack.efbundle --connection "$CONNECTIONSTRINGS__POKERATTACKDB"