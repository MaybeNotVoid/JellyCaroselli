# SeerrHomeCarousel (JellyCaroselli)

Plugin per Jellyfin (`10.11.11+` e `12.0.0+`) che aggiunge i caroselli di **Nuovi Film** e **Nuove Serie TV** direttamente nella Home screen di Jellyfin e apre il **modale integrato di SeerrFin** quando premi su un titolo.

## Prerequisiti
Sul server Jellyfin devono essere installati e attivi:
1. **File Transformation** (`IAmParadox27`)
2. **SeerrFin** (`varunaditya-plus/SeerrFin`) configurato con la tua istanza Seerr/Jellyseerr

## Installazione tramite Repository GitHub
1. Apri Jellyfin → **Pannello Controllo** → **Plugin** → **Repository** → **+ Nuovo repository**.
2. Inserisci:
   - **Nome repository:** `SeerrHomeCarousel`
   - **URL:**
     ```text
     https://raw.githubusercontent.com/MaybeNotVoid/JellyCaroselli/main/manifest.json
     ```
3. Vai in **Catalogo**, cerca **SeerrHomeCarousel** e clicca **Installa**.
4. Riavvia Jellyfin.

## Configurazione
In **Pannello Controllo** → **Plugin** → **SeerrHomeCarousel** puoi personalizzare:
- Abilitazione e titoli dei caroselli Film / Serie TV
- Sorgente Seerr (`upcoming`, `trending`, `popular`, `top-rated`)
- Numero massimo di elementi per carosello (`5 - 50`)
- Posizione nella Home (`Subito dopo "I miei media"`, `In cima`, `In fondo`)
- Opzione per nascondere i titoli già disponibili su Jellyfin
