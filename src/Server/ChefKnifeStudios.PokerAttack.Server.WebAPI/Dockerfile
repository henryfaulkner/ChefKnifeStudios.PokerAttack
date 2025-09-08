FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["./src", "/src"]

WORKDIR /src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI

RUN dotnet build -c Release

FROM build AS publish

RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "ChefKnifeStudios.PokerAttack.Server.WebAPI.dll"]

# Build image with: docker build -f src/Server/ChefKnifeStudios.PokerAttack.Server.WebAPI/Dockerfile -t poker-attack-api-image .
# Run image with: docker run -d -p 5268:8080 --name poker-attack-api-container poker-attack-api-image