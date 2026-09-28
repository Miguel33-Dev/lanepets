# 29/09: imagem do LanePets para hospedar (Railway, Render, qualquer Docker).
# Banco, backups, e-mails .txt e logs ficam em /app/data — monte um VOLUME nessa pasta
# para os dados sobreviverem a cada deploy. Segredos entram por variavel de ambiente (README).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LanePets.csproj ./
RUN dotnet restore LanePets.csproj
COPY . .
RUN dotnet publish LanePets.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=America/Sao_Paulo
RUN mkdir -p /app/data
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LanePets.dll"]
