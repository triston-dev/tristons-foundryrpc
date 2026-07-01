# Triston's FoundryRPC

**Author: [triston-dev](https://github.com/triston-dev)**

A lightweight, standalone Windows tray application that shows which **Foundry VTT**
world/campaign you are currently running as **Discord Rich Presence** — e.g.
*"Running The Third Expeditionary Fleet — 3 online"* with a live session timer.

- Background tray app only — **no Foundry module to install, no browser extension**.
- Original, independent implementation. MIT licensed.
- Built for **Foundry VTT v13** (developed & tested against v13.351).

---

## How detection works

The app polls a local endpoint every ~15 seconds, asks *"what world is running?"*,
and mirrors the answer to Discord:

- **Primary (default): the Foundry MCP bridge control channel.**
  If you run [Triston's Bridge Fork](https://github.com/triston-dev/tristons-bridge-fork)
  of the Foundry MCP bridge (**required dependency** for world detection), its
  local backend exposes a TCP JSON-lines control channel on `127.0.0.1:31414`.
  The app opens a plain socket and calls the `get-world-info` tool:

  ```
  → {"id":"1","method":"call_tool","params":{"name":"get-world-info","args":{}}}
  ← {"id":"1","result":{"content":[{"type":"text","text":"{\"id\":\"campaign_2\",\"title\":\"My Campaign\",...}"}]}}
  ```

  This works **no matter where your Foundry server is hosted** (localhost, VPS,
  partner-hosted) because the bridge module inside Foundry connects out to your
  machine. It also returns the world's **human-readable title** directly, so no
  slug-to-title mapping is normally needed.

- **Idle detection.** Any of the following clears your Discord presence:
  - the control channel is unreachable (connection refused / timeout),
  - the bridge answers but reports no connected world ("module not connected"),
  - the payload has no world id (Foundry sitting at the setup screen).

  A small hysteresis (`idleAfterMissedPolls`, default 2) keeps one transient blip
  from flickering your presence off and back on.

- **Session timer.** Foundry's own `uptime` is *server process* uptime, not
  world-session time, so the app never uses it. Instead it records the local
  timestamp when it sees idle→active or a world-id change, and hands that to
  Discord as the elapsed-timer start. The timer resets when the world changes or
  goes idle.

- **Minimal updates.** A Discord update is pushed only when something meaningful
  changes: world id, active state, or user count.

> **Note on Foundry's `/api/status`:** stock Foundry v13 also exposes an
> unauthenticated `GET http://<host>:30000/api/status` returning
> `{"active":true,"world":"campaign_2","system":"dnd5e","users":1,"uptime":…}`.
> This app intentionally uses the bridge instead (per-world URLs made the HTTP
> endpoint awkward for this setup, and the bridge returns the pretty title), but
> the polling architecture would accept such a source with a small adapter in
> `BridgeClient`/`WorldStatus` if you ever want it.

## Prerequisites

- **Windows 10/11**
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (to build;
  the published exe below is self-contained and needs no runtime)
- **Discord desktop app** running on the same machine
- **[Triston's Bridge Fork](https://github.com/triston-dev/tristons-bridge-fork)**
  — the Foundry MCP bridge this app depends on for world detection; its backend
  must be reachable on `127.0.0.1:31414` (host/port configurable)

## Building & running

```powershell
git clone <this repo>
cd "Tristons FoundryRPC"
dotnet build "Tristons FoundryRPC.sln" -c Release
dotnet run --project "Tristons FoundryRPC"
```

The app appears in the system tray (no window, no console). Right-click the icon
for the menu:

| Menu item | What it does |
|---|---|
| *(status line)* | Shows the current world / idle state |
| **Enable Rich Presence** | Toggle Discord presence on/off (persisted) |
| **Configure Foundry Host/Port…** | Change where the bridge control channel lives |
| **World Display Names** | Open `config.json` to edit display-name overrides |
| **Run at Windows startup** | Adds/removes a per-user registry Run entry |
| **About…** | Author credit + GitHub link |
| **View log…** | Opens the diagnostic log |
| **Quit** | Stops polling, clears presence, exits cleanly |

### Self-contained single-file exe

```powershell
dotnet publish "Tristons FoundryRPC" -c Release -r win-x64 --self-contained
```

Output: `Tristons FoundryRPC\bin\Release\net8.0-windows\win-x64\publish\Tristons FoundryRPC.exe`
— a single exe with the tray icon embedded (the icon is both the exe's file icon
and an embedded resource, so it renders even from single-file publish). Copy it
anywhere and run it.

## Configuration

Settings persist at `%APPDATA%\Tristons FoundryRPC\config.json`
(created with defaults on first run):

```jsonc
{
  "bridgeHost": "127.0.0.1",          // where the bridge control channel lives
  "bridgePort": 31414,
  "pollIntervalSeconds": 15,
  "idleAfterMissedPolls": 2,           // grace polls before clearing presence
  "rpcEnabled": true,
  "discordApplicationId": "1521954755885273259",
  "detailsTemplate": "Running {world}",
  "stateTemplate": "{users} online",
  "showState": true,                   // turn the State line off entirely
  "largeImageKey": "https://foundryvtt.com/static/assets/icons/fvtt.png", // image URL or portal asset key
  "largeImageText": "Foundry VTT — {world}",
  "smallImageKey": "",                 // optional, e.g. a game-system icon
  "smallImageText": "",
  "worldDisplayNames": {},             // world id -> display title overrides
  "showBalloonOnWorldChange": true,
  "logVerbosity": "Info"               // "Error" | "Info" | "Debug"
}
```

### Presence text templates

`detailsTemplate`, `stateTemplate`, `largeImageText`, and `smallImageText` accept
these tokens:

| Token | Meaning | Example |
|---|---|---|
| `{world}` | Resolved world display title | `The Third Expeditionary Fleet` |
| `{system}` | Game system id | `dnd5e` |
| `{users}` | Active (connected) user count | `3` |
| `{version}` | Foundry version | `13.351` |

### World display-name overrides

The bridge already returns the pretty world title, so overrides are rarely
needed — but if you want to display something different, map the **raw world id**
to your preferred title:

```json
"worldDisplayNames": {
  "campaign_2": "The Third Expeditionary Fleet"
}
```

The raw world id is written to the log the **first time each world is seen**, so
you know exactly what key to use. Resolution order:
**override → bridge title → raw id**.

### Changing the Discord Application ID & art assets

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
   and create (or open) an application. Its **name** is what Discord shows as
   *"Playing …"*.
2. Copy the **Application ID** into `discordApplicationId` in `config.json`
   (the built-in default lives in `DiscordPresenceManager.DefaultApplicationId`).
3. Presence art: `largeImageKey` / `smallImageKey` accept **either** a full
   `https://` image URL (the default is the official Foundry logo URL — works
   with zero portal setup; verified live 2026-07) **or** the key of an art
   asset uploaded under **Rich Presence → Art Assets** in the portal.
4. Restart the tray app (config is read at startup).

## Diagnostics

- Log file: `%APPDATA%\Tristons FoundryRPC\foundryrpc.log`
  (rolls to `.old` at ~1 MB). Set `"logVerbosity": "Debug"` to watch every poll.
- The app never opens a console window; all diagnostics go to that file.
- Single-instance: a second launch shows a message box and exits.
- Clean shutdown (tray → Quit) stops polling and clears your Discord presence.
- Handles gracefully: Foundry/bridge not running, connection refused, malformed
  or partial JSON, world not loaded, and Discord not (yet) running — the RPC
  client reconnects automatically when Discord appears and the presence is
  re-applied.

## Project layout

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point, single-instance mutex, bootstrap |
| `TrayApp.cs` | NotifyIcon, context menu, UI-thread wiring |
| `FoundryStatusWatcher.cs` | Poll loop, change detection, session timestamp, hysteresis |
| `BridgeClient.cs` | TCP JSON-lines protocol to the bridge control channel |
| `WorldStatus.cs` | Parsed world snapshot + "meaningful change" rules |
| `WorldNameResolver.cs` | Override map → bridge title → raw id |
| `DiscordPresenceManager.cs` | Rich Presence via [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp) |
| `StartupManager.cs` | Run-at-startup registry toggle |
| `Config.cs` / `Logger.cs` | JSON settings & rolling file log |

## Author / Credits

**Triston's FoundryRPC** is written and maintained by
**[triston-dev](https://github.com/triston-dev)**.

- World detection depends on
  [Triston's Bridge Fork](https://github.com/triston-dev/tristons-bridge-fork)
  of the Foundry MCP bridge (control channel on `127.0.0.1:31414`).
- Discord RPC via the excellent [discord-rpc-csharp (DiscordRichPresence)](https://github.com/Lachee/discord-rpc-csharp) library by Lachee.
- Not affiliated with Foundry Gaming LLC or Discord Inc. "Foundry VTT" and
  "Discord" are trademarks of their respective owners.

## License

[MIT](LICENSE) — © triston-dev
