
# Teamleader Focus Agent (KISS)

Minimal C# console app that reads CSV lines and creates/updates Company → Project → Phase → Task → Time entry in Teamleader Focus using simple HttpClient calls.

Usage

1. Build:

```powershell
dotnet build .\src\TeamleaderFocusAgent.csproj
```

2. Run:

```powershell
# Set your token (recommended)
$env:TEAMLEADER_TOKEN = "<your_access_token>"

# Run (config path optional, input path optional)
dotnet run --project .\src\TeamleaderFocusAgent.csproj -- src\appsettings.json input.csv
```

CSV format (KISS): each line should be `tags;start;end;notes` where `tags` is a comma-separated hierarchy: `Company,Project,Group,Task[,extra...]`.
Example:

```
ACME,CRM,Backend,Fix Email Bug,UI,Critical;2025-11-21 09:00;2025-11-21 12:00;Something broken
```

Notes
- OAuth token is expected as an environment variable `TEAMLEADER_TOKEN` or placed in `src\appsettings.json` under `ApiToken`.
- Fuzzy matching (company/project/phase) uses simple `Contains` or first-3-letters match as defined in the spec.
- Task matching is exact by name; if missing the project phase will be used when creating the task.

Limitations / To do
- Proper OAuth2 flow is out of scope; you must provide a valid token.
- Endpoints and response shapes may differ depending on Teamleader account; adjust paths and JSON parsing if needed.
- Error handling is minimal — extend for production use.