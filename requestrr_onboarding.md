# Onboarding to Requestrr

This guide is for developers who are new to the Requestrr codebase. It covers how the code is organized, what happens at runtime from process start to a Discord user getting a "requested!" reply, how to get a local environment running, and a worked example of adding a new slash command.

For deployment and end-user configuration, see the [project wiki](https://github.com/thomst08/requestrr/wiki). For branch and PR conventions, see [CONTRIBUTING.md](CONTRIBUTING.md).

---

## 1. What Requestrr is

Requestrr is a Discord bot that lets people request media through slash commands, for example `/request movie Dune`. It forwards each request to the media server's management tools:

| Media  | Supported backends                     |
| ------ | -------------------------------------- |
| Movies | Radarr (v2–v5), Overseerr, Ombi        |
| TV     | Sonarr (v2–v4), Overseerr, Ombi        |
| Music  | Lidarr (v1–v2)                         |

Users can also report issues on existing media (Overseerr and Ombi only), and can ask to be notified when a request becomes available.

Everything ships as **one process**: an ASP.NET Core app that hosts both the Discord bot and a React admin portal used to configure it (default port `4545`).

---

## 2. Repository layout

```
requestrr/
├── Requestrr.WebApi/                 # The only project
│   ├── Program.cs                    # Entry point: CLI args, config folder, settings bootstrap
│   ├── Startup.cs                    # DI registrations, auth, SPA hosting, starts the bot
│   ├── SettingsTemplate.json         # Default settings.json for a fresh install
│   ├── NotificationsTemplate.json    # Default notifications.json
│   ├── SettingsFileUpgrader.cs       # Versioned migrations for settings.json
│   ├── SlashCommands.txt             # C# *template* for all Discord slash commands
│   ├── locales/*.json                # Every user-facing bot string, per language
│   ├── config/                       # Typed settings classes (not the runtime config files)
│   ├── Controllers/                  # REST API used by the admin portal
│   ├── RequestrrBot/                 # The bot itself
│   │   ├── ChatBot.cs                # Bot lifecycle + button/dropdown routing
│   │   ├── SettingsFile.cs           # Read/write access to settings.json
│   │   ├── SlashCommands/            # Runtime compiler for SlashCommands.txt
│   │   ├── ChatClients/Discord/      # Discord-specific UI (embeds, buttons, help, ping)
│   │   ├── Movies/ TvShows/ Music/   # Media-type workflows and interfaces
│   │   ├── DownloadClients/          # Radarr/Sonarr/Lidarr/Overseerr/Ombi API clients
│   │   ├── Notifications/            # "Notify me when available" engines
│   │   └── Locale/                   # Language class bound to locales/*.json
│   ├── ClientApp/                    # React admin portal (Create React App)
│   └── dockerfile
└── Logos/
```

---

## 3. Core concepts

### 3.1 Settings are one JSON file

All configuration lives in `<config folder>/settings.json`. Nothing is stored in a database.

- [`SettingsFile`](Requestrr.WebApi/RequestrrBot/SettingsFile.cs) is a static, lock-protected cache over that file. It exposes `Read()`, which returns a `dynamic` JObject, and `Write(Action<dynamic>)`.
- **`*SettingsProvider`** classes turn the dynamic JSON into typed objects, e.g. `DiscordSettingsProvider.Provide()` returns `DiscordSettings`. They are registered as singletons in `Startup.cs` and are called on demand, so they always reflect the latest file contents.
- **`*SettingsRepository`** classes under `Controllers/` write changes from the admin portal back through `SettingsFile.Write`.
- On every start, [`SettingsFileUpgrader`](Requestrr.WebApi/SettingsFileUpgrader.cs) migrates older files. It is a chain of `if (Version == "x.y.z") { ...; Version = "next"; }` blocks. **If you change the shape of the settings, add a step here and update `SettingsTemplate.json` to the same version.**

### 3.2 Strings come from locale files

The bot never hardcodes user-facing text. [`Language.Current`](Requestrr.WebApi/RequestrrBot/Locale/Language.cs) is deserialized from `locales/<language>.json`. Each property maps to a flat dotted key:

```csharp
[JsonProperty("Discord.Command.Ping.Response")]
public string DiscordCommandPingResponse { get; set; }
```

```json
"Discord.Command.Ping.Response": "Pong",
```

Slash command **names** are localized too: in French, `/request` is a different word. Placeholders such as `[MovieTitle]` and `[AuthorUsername]` are listed in `LanguageTokens`.

### 3.3 Workflows, interfaces and factories

Movies, TV and Music use the same pattern, so once you understand one you understand all three. Using Movies as the example:

```
                 MovieWorkflowFactory  (picks implementations from settings)
                          │
                          ▼
              MovieRequestingWorkflow  (logic: search → select → request → notify)
        ┌─────────────┬───────────────┬────────────────────┬─────────────────────────┐
        ▼             ▼               ▼                    ▼
  IMovieSearcher  IMovieRequester  IMovieUserInterface  IMovieNotificationWorkflow
        │             │               │
        └──────┬──────┘               └─► DiscordMovieUserInterface (embeds, buttons)
               ▼
   RadarrClient | OverseerrClient | OmbiClient
```

- The workflow knows nothing about Discord or Radarr. It only talks to the interfaces.
- The factory reads `DiscordSettings.MovieDownloadClient` (a [`DownloadClient`](Requestrr.WebApi/RequestrrBot/DownloadClients/DownloadClient.cs) enum) to decide which client to pass in.
- Clients for APIs with several versions use a facade that hands off to a per-version class. For example, `SonarrClient` delegates to `SonarrClientV2`, `V3` or `V4` based on the configured version.

### 3.4 Slash commands are compiled at runtime

This is the least obvious part of the codebase, and the part you are most likely to touch.

Discord slash commands are declared with attributes (`[SlashCommand("movie", "...")]`). Names, descriptions, role restrictions, channel restrictions and per-category commands all depend on the user's settings and language, so they can't be fixed at compile time. Instead:

1. [`SlashCommands.txt`](Requestrr.WebApi/SlashCommands.txt) contains C# source with `[PLACEHOLDER]` tokens and `[X_COMMAND_START]`/`[X_COMMAND_END]` section markers.
2. [`SlashCommandBuilder.GetCode`](Requestrr.WebApi/RequestrrBot/SlashCommands/SlashCommandBuilder.cs) fills in the placeholders. It removes sections for disabled features, and duplicates sections once per category, e.g. one `/request` subcommand per Radarr category.
3. `SlashCommandBuilder.Build` compiles the result with Roslyn into `<config>/tmp/slashcommandsbuilder-<guid>.dll`, loads it, and returns the `SlashCommands` type.
4. `ChatBot` registers that type with DSharpPlus.

Consequences you need to know:

- **Errors in `SlashCommands.txt` don't break `dotnet build`.** They show up at runtime as `Failed to build SlashCommands assembly: ...` in the log.
- A placeholder you forget to replace either breaks compilation (if it sits in code) or appears literally in Discord (if it sits inside a string).
- `SlashCommandBuilder.CommandList` records the generated command names, which `/help` reads. It also throws on duplicate names, e.g. two categories with the same name.

---

## 4. Thread of execution

### 4.1 Process startup

```
Program.Main
 ├─ Parse CLI args (-c config dir, -p port, -u base url) and env vars
 │    (REQUESTRR_PORT, REQUESTRR_BASE_URL)
 ├─ Resolve config folder: from appsettings[.Development].json "ConfigFolder"
 ├─ UpdateSettingsFile()
 │    ├─ No settings.json? → copy SettingsTemplate.json (with a new JWT key)
 │    ├─ Otherwise        → SettingsFileUpgrader.Upgrade()
 │    └─ No notifications.json? → copy NotificationsTemplate.json
 ├─ SetLanguage()  → Language.Current = locales/<lang>.json
 ├─ Write CLI port/base-url overrides into settings.json
 └─ WebHost.UseStartup<Startup>().Run()
       Startup.ConfigureServices → DI singletons, JWT (or Disabled) auth
       Startup.Configure         → static files, base path, API routes, SPA
                                    (Development: proxies to `npm start`)
                                 → ChatBot.Start()
```

### 4.2 The bot's control loop

[`ChatBot.Start()`](Requestrr.WebApi/RequestrrBot/ChatBot.cs) starts a background loop that runs **every 5 seconds** for the life of the process:

1. Read the latest `DiscordSettings`.
2. If the settings, the language or the set of joined guilds changed, call `RestartBot`:
   - If the token changed, or there is no client yet, dispose the old `DiscordClient` and create a new one. This wires up handlers for buttons/dropdowns (`ComponentInteractionCreated`), modals, the socket, and heartbeats.
   - `SlashCommandBuilder.Build(...)` creates a fresh commands assembly.
   - Register it, either globally (if DMs are enabled) or per guild, clearing the other scope with `EmptySlashCommands`.
   - Rebuild the notification engines.
3. Exit the process if the socket has been closed for 10+ minutes or heartbeats have stopped. This lets Docker restart the container.

**This is why saving in the admin portal takes effect without a restart.** The portal writes `settings.json`, and the loop notices the change within a few seconds.

### 4.3 A request, end to end

Example: a user runs `/request movie Dune` with Radarr configured and no categories.

```
Discord
  │  slash command interaction
  ▼
SlashCommands.RequestGroup.MovieByTitle99999          (generated from SlashCommands.txt)
  ├─ [RequireRoles]/[RequireChannels] checks → failure goes to ChatBot.SlashCommandErrorHandler
  ├─ Reply "Sending..." (ephemeral if AutomaticallyPurgeCommandMessages)
  └─ MovieWorkflowFactory.CreateRequestingWorkflow(interaction, categoryId)
        └─ MovieRequestingWorkflow.SearchMovieAsync("Dune")
              ├─ IMovieSearcher (RadarrClient → RadarrClientV3).SearchMovieAsync
              └─ IMovieUserInterface.ShowMovieSelection
                    → dropdown with customId "MRS/<userId>/<categoryId>"

User picks a movie from the dropdown
  ▼
ChatBot.DiscordComponentInteractionCreatedHandler
  ├─ Acknowledge interaction; parse <userId> from customId; ignore other users' clicks
  ├─ customId starts with "mr" → HandleMovieRequestAsync
  │     "mrs" → workflow.HandleMovieSelectionAsync(tmdbId)
  │              → DisplayMovieDetailsAsync: embed + button "MRC/<userId>/<cat>/<tmdbId>"
  │                 (or "already available/requested" + optional notify button)
  ▼
User clicks "Request"
  ▼
ChatBot → "mrc" → workflow.RequestMovieAsync(tmdbId)
  ├─ IMovieRequester (RadarrClient).RequestMovieAsync → POST to Radarr
  ├─ IMovieUserInterface.DisplayRequestSuccessAsync / DisplayRequestDeniedAsync
  └─ IMovieNotificationWorkflow.NotifyForNewRequestAsync → persisted to notifications.json

Later: MovieNotificationEngine polls the backend once a minute
(SearchAvailableMoviesAsync). When the movie becomes available it notifies the
users who asked, by DM or in channels depending on NotificationMode.
```

**Custom ID convention:** every button, dropdown and modal ID is `PREFIX/<discordUserId>/...`. `ChatBot` routes on the prefix with `StartsWith`. `MR*` is a movie request, `MIR*` a movie issue, `MNR` a movie notification, `TR*`/`TS*` TV, `MUR*` music, and so on. The second segment is checked so that only the person who ran the command can click their own buttons. Because routing uses `StartsWith`, **choose new prefixes that don't collide with existing ones, and check the order of the `if` chain**.

### 4.4 The admin portal

- The React app ([`ClientApp/src`](Requestrr.WebApi/ClientApp/src)) uses Redux Toolkit, with one actions/reducer pair per area under `src/store/`. It calls the API with **relative** URLs (`fetch("../api/movies", ...)`) so it keeps working when the app is served under a base URL.
- Each area has a controller under [`Controllers/`](Requestrr.WebApi/Controllers), e.g. `POST /api/chatclients` saves Discord settings and `POST /api/movies/radarr` saves Radarr settings. The controller validates the input, writes it through a repository, and the bot's loop picks up the change.
- Authentication uses a JWT signed with `Authentication.PrivateKey` from `settings.json`. It can be turned off (`DisableAuthentication`), in which case `DisabledAuthenticationHandler` is used.

---

## 5. Local development setup

### 5.1 Prerequisites

- **.NET SDK 6.0 or newer.** The project targets `net6.0`, so you need the **.NET 6 runtime** installed to run it. The Docker build compiles with the .NET 8 SDK.
- **Node.js + npm**, for the React portal.
- A **Discord application and bot token** for testing. Create one in the [Discord Developer Portal](https://discord.com/developers/applications), enable the privileged gateway intents (the bot uses `DiscordIntents.All`), and invite it to a test server with the `bot` and `applications.commands` scopes.
- Optionally, a Radarr, Sonarr, Lidarr, Overseerr or Ombi instance to request against. Running one in Docker is easiest.

### 5.2 First run

```bash
git clone https://github.com/<you>/requestrr.git
cd requestrr/Requestrr.WebApi

dotnet build     # Debug build runs `npm install` in ClientApp/ if node_modules is missing
dotnet run       # or: dotnet watch run
```

- In Debug, the config folder comes from `appsettings.Development.json` (`"ConfigFolder": "./config"`). Relative paths resolve against the **built executable's folder** (e.g. `bin/Debug/net6.0/config`), not the project folder. Use `dotnet run -- -c /some/absolute/path` to put it somewhere else.
- With `ASPNETCORE_ENVIRONMENT=Development` (the VS Code launch config sets this), ASP.NET runs `npm start` and proxies the React dev server, so frontend changes hot-reload.
- Open `http://localhost:4545`, create the admin account, then:
  1. **Chat Clients**: paste the bot token, pick the language, and optionally restrict to channels or roles.
  2. **Movies / TV Shows / Music**: pick a backend and enter its URL and API key.
- Within a few seconds the log should show `Bot configuration changed: restarting bot`, and the commands should appear in your test server.

VS Code users: [`.vscode/launch.json`](.vscode/launch.json) has a ".NET Core Launch (web)" configuration that builds and attaches the debugger.

### 5.3 Other useful commands

| Task                         | Command (from `Requestrr.WebApi/`)                          |
| ---------------------------- | ----------------------------------------------------------- |
| Frontend only                | `cd ClientApp && npm start`                                 |
| Clean frontend reinstall     | `cd ClientApp && npm run install:clean`                     |
| Production publish           | `dotnet publish -c release -o publish Requestrr.WebApi.csproj` |
| Docker image                 | `docker build -t requestrr .`                               |
| Run on another port/base URL | `dotnet run -- -p 4546 -u /requestrr`                       |

There is no automated test suite. Verify changes by running the bot against a test Discord server.

### 5.4 Debugging tips

- **Commands don't appear:** check the log for `Failed to build SlashCommands assembly` or `Duplicate Slash Commands detected`. Guild commands update almost immediately. Global commands (DM mode) can take a while to propagate on Discord's side.
- **Settings look stale:** `SettingsFile` caches the parsed JSON in memory. Edit settings through the portal, or restart the process after hand-editing `settings.json`.
- **Leftover DLLs:** generated assemblies collect in `<config>/tmp/`. `SlashCommandBuilder.CleanUp()` deletes old ones after each restart, and they are safe to delete by hand while the app is stopped.

---

## 6. Contributing: adding a new slash command

Worked example: add a top-level `/version` command that replies with the running Requestrr version. The pattern is the same as the existing `/ping` and `/help` commands. Commands under `/request` follow the same steps, with more section-marker handling in `SlashCommandBuilder`.

### Step 1: Add the strings to every locale

Add the keys to **every** file in `locales/`. A missing key deserializes as `null`, which breaks the command name. Translate where you can, and otherwise fall back to English:

```json
"Discord.Command.Version.Request.Name": "version",
"Discord.Command.Version.Request.Description": "Show the Requestrr version",
"Discord.Command.Version.Response": "Requestrr [Version]",
```

Discord requires command names to be lowercase, 1–32 characters, with no spaces.

### Step 2: Expose them on `Language`

In [`RequestrrBot/Locale/Language.cs`](Requestrr.WebApi/RequestrrBot/Locale/Language.cs), next to the Ping properties:

```csharp
[JsonProperty("Discord.Command.Version.Request.Name")]
public string DiscordCommandVersionRequestName { get; set; }

[JsonProperty("Discord.Command.Version.Request.Description")]
public string DiscordCommandVersionRequestDescription { get; set; }

[JsonProperty("Discord.Command.Version.Response")]
public string DiscordCommandVersionResponse { get; set; }
```

### Step 3: Write the handler as a workflow class

Keep logic out of the template, which has no compile-time checking. Put it in a normal class that is compiled with the project. Model it on [`DiscordPingWorkFlow`](Requestrr.WebApi/RequestrrBot/ChatClients/Discord/DiscordPingWorkFlow.cs):

```csharp
// RequestrrBot/ChatClients/Discord/DiscordVersionWorkFlow.cs
public class DiscordVersionWorkFlow
{
    private readonly InteractionContext _context;

    public DiscordVersionWorkFlow(InteractionContext context)
    {
        _context = context;
    }

    public async Task HandleVersionAsync()
    {
        var message = Language.Current.DiscordCommandVersionResponse.Replace("[Version]", Language.BuildVersion);
        await _context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral(true).WithContent(message));
    }
}
```

### Step 4: Declare the command in `SlashCommands.txt`

Add a method to the outer `SlashCommands` class, next to `Ping`, using placeholders for anything localized or configurable:

```csharp
[SlashCommand("[REQUEST_VERSION_NAME]", "[REQUEST_VERSION_DESCRIPTION]")]
[RequireChannels(new ulong[] { [REQUIRED_CHANNEL_IDS] })]
public async Task Version(InteractionContext ctx)
{
    try
    {
        await new DiscordVersionWorkFlow(ctx).HandleVersionAsync();
    }
    catch (System.Exception ex)
    {
        _logger.LogError(ex, "Error while sending version command: " + ex.Message);
    }
}
```

The template's `using` list already covers `Requestrr.WebApi.RequestrrBot.ChatClients.Discord`. If your workflow is in another namespace, add a `using` at the top of the template.

To make the command depend on a setting, wrap it in `[VERSION_COMMAND_START]` / `[VERSION_COMMAND_END]` markers and, in the builder, either strip the markers or remove the whole section. Copy the pattern used for `[MUSIC_COMMAND_START]`.

### Step 5: Fill in the placeholders in `SlashCommandBuilder`

In `GetCode` in [`SlashCommandBuilder.cs`](Requestrr.WebApi/RequestrrBot/SlashCommands/SlashCommandBuilder.cs), next to the Ping/Help replacements:

```csharp
code = code.Replace("[REQUEST_VERSION_NAME]", Language.Current.DiscordCommandVersionRequestName);
code = code.Replace("[REQUEST_VERSION_DESCRIPTION]", Language.Current.DiscordCommandVersionRequestDescription);
```

…and register it so `/help` lists it and the duplicate check covers it:

```csharp
_commandList[CommandType.Misc].Add(Language.Current.DiscordCommandVersionRequestName);
```

### Step 6: (If needed) give the command services

Generated commands resolve dependencies from a small `ServiceCollection` built in `ChatBot.RestartBot`. Today it holds `DiscordClient`, `ILogger`, `DiscordSettingsProvider` and the three workflow factories. If your command needs anything else, such as a settings provider or an API client, add it there and resolve it with `_serviceProvider.Get<T>()` in the template.

### Step 7: (If needed) handle buttons, dropdowns and modals

If the command replies with interactive components:

1. Give each component a custom ID of the form `<PREFIX>/<ctx.User.Id>/<data...>`.
2. Add a branch in `ChatBot.DiscordComponentInteractionCreatedHandler`, or in `DiscordModalSubmittedHandler` for modals, that routes your prefix to a handler.
3. Make sure no existing branch earlier in the chain already matches your prefix with `StartsWith`.
4. Include `modal` in the ID of components that open a modal. The handler skips its automatic `UpdateMessage` acknowledgement for those.

### Step 8: Verify

1. `dotnet build`. This only catches errors in Steps 2, 3, 5 and 6.
2. `dotnet run`, then save any setting in the portal, or restart, so the bot rebuilds its commands.
3. Check the log for compile errors in the generated commands.
4. In Discord: confirm `/version` appears, works in a monitored channel, is rejected in an unmonitored one, and is listed by `/help`.
5. Switch the language in the portal to confirm the localized name loads, and that no `null` or literal `[PLACEHOLDER]` text appears.

### Checklist

- [ ] Keys added to **all** `locales/*.json`
- [ ] `Language.cs` properties added
- [ ] Logic in a compiled workflow class, not in `SlashCommands.txt`
- [ ] Template method added; every placeholder replaced in `SlashCommandBuilder.GetCode`
- [ ] Command added to `_commandList` so `/help` shows it
- [ ] New services added to the `ChatBot` service collection, if used
- [ ] New component prefixes routed in `ChatBot` without collisions
- [ ] If settings were added: `SettingsTemplate.json` + `SettingsFileUpgrader` step + provider/repository + portal UI
- [ ] Tested on a real Discord server

### Submitting

Follow [CONTRIBUTING.md](CONTRIBUTING.md): branch from `develop` with a descriptive name (e.g. `feature-version-command`), rebase on `upstream/develop`, and open a PR against `develop`.
