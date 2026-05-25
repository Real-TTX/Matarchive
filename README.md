# Matarchive

Matarchive ist ein lokales ASP.NET-Core-Projekt für Backup- und Sync-Jobs. Der Name kombiniert "Matthix" und "Archive".

## Stack

- ASP.NET Core
- C#
- Docker + Docker Compose
- GitHub Actions für den Image-Build

## Was das Projekt abdeckt

- Tasks mit fester Source und Destination
- Verbindungen vom Typ `POP3`, `IMAP`, `SMB` und vorbereiteter Erweiterbarkeit für weitere Typen
- Archive- und Sync-Tasks
- Lokale Benutzerverwaltung, alle Benutzer sind Admins
- API-Keys zum Abrufen des Task-Status
- E-Mail-Benachrichtigungen über SMTP
- Mobile-freundliches UI
- Separate Seiten für Liste, Anlage und Bearbeitung

## Starten mit Docker

```bash
docker compose up --build
```

Die App läuft standardmäßig auf `http://localhost:8077`.

### Wichtige Umgebungsvariablen

- `APP_PORT`: Container- und Host-Port, standardmäßig `8077`
- `MATARCHIVE_ADMIN_USERNAME`: Initialer Admin-Login, standardmäßig `admin`
- `MATARCHIVE_ADMIN_PASSWORD`: Initiales Admin-Passwort, standardmäßig `ChangeMe!123`

### Volume

- `data`: enthält die JSON-Persistenz für Benutzer, Tasks, Verbindungen, Läufe und API-Keys

## Live-Reload im Dev-Modus

Wenn du den Stack während der Entwicklung automatisch aktuell halten willst, nutze die Dev-Compose-Datei:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

Diese Variante läuft mit `dotnet watch` im Container und übernimmt Codeänderungen automatisch.

## Lokal entwickeln

```bash
dotnet run --project src/Matarchive.Web
```

Für die lokale Entwicklung ist in `Properties/launchSettings.json` ebenfalls Port `8077` hinterlegt.

## Login

- Benutzername: `admin`
- Passwort: `ChangeMe!123`

Diese Werte kannst du über `appsettings.json` oder die Docker-Umgebungsvariablen ändern.

## API-Status

Der Status kann mit einem API-Key abgefragt werden.

Beispiel:

```http
GET /api/status
X-Matarchive-Api-Key: mk_...
```

Alternativ wird auch `Authorization: Bearer <key>` akzeptiert.

## Projektstruktur

- `src/Matarchive.Web/Program.cs`: Startup, Auth, API und Hosting
- `src/Matarchive.Web/Domain`: Kernmodelle
- `src/Matarchive.Web/Infrastructure`: JSON-Store, Auth, API-Key-Logik, Benachrichtigungen
- `src/Matarchive.Web/Services`: Hintergrundläufe und Scheduler
- `src/Matarchive.Web/Pages`: UI-Seiten
- `src/Matarchive.Web/wwwroot`: Logo, Icons und App-Design
