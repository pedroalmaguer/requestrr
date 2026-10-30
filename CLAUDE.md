# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Requestrr is a Discord chatbot that lets users request movies, TV shows and music through slash commands, forwarding requests to Sonarr/Radarr/Lidarr/Overseerr/Ombi. A single ASP.NET Core app (`Requestrr.WebApi/`) hosts both the bot and a React admin portal (`Requestrr.WebApi/ClientApp/`) used to configure it. Default port is 4545.

## Commands

Run from `Requestrr.WebApi/` unless noted.

- Build backend: `dotnet build` (the Debug build runs `npm install` in `ClientApp/` automatically if `node_modules` is missing)
- Run (Debug): `dotnet run`, or the ".NET Core Launch (web)" VS Code config. In Development, ASP.NET starts the CRA dev server (`npm start`) via `UseReactDevelopmentServer`.
- Watch: `dotnet watch run`
- Publish: `dotnet publish -c release -o publish Requestrr.WebApi.csproj` (also runs `npm run build` in ClientApp)
- Frontend only (in `ClientApp/`): `npm start`, `npm run build`, `npm run install:clean`
- Docker: `docker build .` from `Requestrr.WebApi/` (uses `dockerfile`)
- CLI flags: `-c/--config-dir`, `-p/--port`, `-u/--base-url`; env vars `REQUESTRR_PORT`, `REQUESTRR_BASE_URL`

There is no test project in the repo; `npm test` exists only as the CRA default.

## Architecture

### Startup and configuration
- `Program.cs` resolves the config folder (from `appsettings*.json` `ConfigFolder`, default `./config`), creates `config/settings.json` from `SettingsTemplate.json` and `config/notifications.json` from `NotificationsTemplate.json` if missing, runs `SettingsFileUpgrader.Upgrade`, and loads the locale.
- `RequestrrBot/SettingsFile.cs` is the single source of truth: a static, lock-protected, cached `dynamic` JObject over `settings.json` with `Read()` / `Write(Action<dynamic>)`. `*SettingsProvider` classes (registered as singletons in `Startup.cs`) map that dynamic JSON into typed settings objects; `*SettingsRepository` classes under `Controllers/` write changes back.
- **Settings schema changes**: add a migration step to `SettingsFileUpgrader.cs` (chained `if Version == "x.y.z"` blocks that bump `settingsJson.Version`), and update `SettingsTemplate.json`'s `Version` and defaults to match.
- Auth is JWT (key in settings `Authentication.PrivateKey`), or `DisabledAuthenticationHandler` when `DisableAuthentication` is set.

### Bot (`RequestrrBot/`)
- `ChatBot.Start()` runs a background polling loop that re-reads `DiscordSettingsProvider` and rebuilds the DSharpPlus client, slash commands and notification engines whenever settings, language, or guild membership change. Saving config in the web UI takes effect through this loop, not a restart.
- **Slash commands are generated at runtime**: `SlashCommands.txt` is a C# source template with `[PLACEHOLDER]` tokens (localized names, role/channel IDs, per-category command blocks). `SlashCommands/SlashCommandBuilder.cs` substitutes them, compiles the result with Roslyn into a DLL in a `tmp` folder, and loads it. To change command shape, edit `SlashCommands.txt` and the builder's replacements together; compile errors only appear at runtime.
- Each media type (`Movies/`, `TvShows/`, `Music/`) follows the same pattern:
  - Interfaces `I*Searcher`, `I*Requester`, `I*IssueSearcher/Requester`, `I*UserInterface`, `I*NotificationWorkflow`
  - `*RequestingWorkflow` / `*IssueWorkflow` hold the logic and talk only to those interfaces
  - `*WorkflowFactory` picks the concrete download client from settings (`DownloadClients/DownloadClient.cs` enum) and wires in the Discord UI (`ChatClients/Discord/Discord*UserInterface.cs`)
  - TV season selection logic lives in `TvShows/SeasonsRequestWorkflows/`
- `DownloadClients/<Service>/` implement those interfaces. Versioned APIs use a facade that delegates to per-version clients (e.g. `SonarrClient` → `SonarrClientV2/V3/V4`, `RadarrClient` → `V2/V3`, `LidarrClient` → `V1`).
- `Notifications/` holds per-media notification engines and repositories persisted to `notifications.json`.

### Localization
- All user-facing bot strings come from `Language.Current` (`RequestrrBot/Locale/Language.cs`), deserialized from `locales/<language>.json`. Adding a string means adding a property to `Language` and a key to every file in `locales/`. Token placeholders like `[MovieTitle]` are defined in `LanguageTokens`.

### Web API and frontend
- `Controllers/` exposes `api/...` endpoints per area (Authentication, ChatClients, Configuration, DownloadClients/<Service>), each with request models plus provider/repository classes.
- `ClientApp/` is a Create React App (Argon Dashboard theme, reactstrap, Redux Toolkit). API calls are made from Redux actions in `src/store/actions/` using relative URLs (`../api/...`) so the configurable base URL keeps working. Per-service settings forms are in `src/components/DownloadClients/<Service>/`.

## Version bumps

A release version is updated in several places at once (see commit `3d64efa`): `ClientApp/package.json`, `ClientApp/src/components/Footers/AdminFooter.jsx` and `AuthFooter.jsx`, `RequestrrBot/Locale/Language.cs` (`BuildVersion`), and the version string in `RequestrrBot/DownloadClients/Overseerr/OverseerrClient.cs`. This is separate from the settings-file schema version handled by `SettingsFileUpgrader`.

## Contributing notes

- Branch from `develop` for upstream PRs (per `CONTRIBUTING.md`), with descriptive branch names like `fix-title-cards`.
- Target framework is net6.0 (Docker build uses the .NET 8 SDK to publish, then runs on the 6.0 ASP.NET runtime).
