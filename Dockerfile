# --- Fase 1: compilazione ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish "Campanile.Server.csproj" -c Release -o /app/pubblicato

# --- Fase 2: esecuzione (immagine finale, più leggera) ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS finale
WORKDIR /app
COPY --from=build /app/pubblicato .

# Render assegna la porta a cui ascoltare tramite la variabile d'ambiente PORT,
# decisa quando il contenitore parte (non quando viene costruito) — per questo
# la lettura avviene dentro una shell all'avvio, non con una semplice ENV fissa.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet Campanile.Server.dll"]
