# Triston's FoundryRPC

**by [triston-dev](https://github.com/triston-dev)**

Show off which **Foundry VTT** campaign you're running — right on your Discord
profile. Triston's FoundryRPC is a tiny Windows tray app that sets your Discord
Rich Presence to the world you have loaded, with a live session timer:

> **Playing Triston's FoundryRPC**
> Running The Third Expeditionary Fleet
> 3 online · ⏱ 1:18

- Sits quietly in the system tray — no window, no console.
- Nothing to install inside Foundry beyond the bridge you may already use.
- Works no matter **where your Foundry server is hosted** — localhost, VPS, or
  partner-hosted at any URL.
- Presence clears automatically when you close your world.

---

## Requirements

| What | Why |
|---|---|
| Windows 10/11 | It's a Windows tray app |
| [Discord desktop app](https://discord.com/download) | Rich Presence only works with the desktop client, running on the same PC |
| [**Triston's Bridge Fork**](https://github.com/triston-dev/tristons-bridge-fork) | **Required dependency** — this is how the app knows which world is running |

## Installation

### Step 1 — Install Triston's Bridge Fork

This app detects your world through
[Triston's Bridge Fork](https://github.com/triston-dev/tristons-bridge-fork)
of the Foundry ↔ Claude MCP bridge. If you already run it, skip ahead.
Otherwise, follow its
[installation guide](https://github.com/triston-dev/tristons-bridge-fork#installation)
— in short:

1. Download `foundry-mcp-bridge.zip` (Foundry module) and
   `foundry-mcp-server.zip` (local server) from its
   [latest release](https://github.com/triston-dev/tristons-bridge-fork/releases/latest)
   and install both halves per the guide.
2. In Foundry: **Manage Modules → enable "Triston's Bridge Fork"**, then in
   **Game Settings → Triston's Bridge Fork**, make sure **Enable MCP Bridge**
   is on and shows **Connected**.

### Step 2 — Get Triston's FoundryRPC

**Option A — download:** grab `Tristons FoundryRPC.exe` from this repo's
[Releases](https://github.com/triston-dev/tristons-foundryrpc/releases) page
and put it anywhere you like (it's fully self-contained).

**Option B — build it yourself** (needs the free
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)):

```powershell
git clone https://github.com/triston-dev/tristons-foundryrpc.git
cd tristons-foundryrpc
dotnet publish "Tristons FoundryRPC" -c Release -r win-x64 --self-contained
```

Your exe lands in
`Tristons FoundryRPC\bin\Release\net8.0-windows\win-x64\publish\`.

### Step 3 — Run it

Double-click the exe. A tray icon appears; within ~15 seconds of loading a
world in Foundry, your Discord presence shows it. That's the whole install.

To have it start with Windows: right-click the tray icon →
**Run at Windows startup**.

## Using the tray menu

Right-click the tray icon:

| Menu item | What it does |
|---|---|
| *(status line)* | Shows the current world / idle state |
| **Enable Rich Presence** | Turn the Discord presence on or off |
| **Configure Foundry Host/Port…** | Where the bridge lives (default `127.0.0.1:31414`) |
| **World Display Names** | Override what a world is called on Discord |
| **Run at Windows startup** | Start automatically with Windows |
| **About…** | Version, author, GitHub link |
| **View log…** | Open the diagnostic log |
| **Quit** | Clears your presence and exits |

## Customizing your presence

Settings live in `%APPDATA%\Tristons FoundryRPC\config.json` (created on first
run; edit with any text editor, then restart the app). The lines Discord shows
are simple templates:

```json
"detailsTemplate": "Running {world}",
"stateTemplate": "{users} online",
"showState": true
```

| Token | Becomes | Example |
|---|---|---|
| `{world}` | World title | `Curse of Strahd` |
| `{users}` | Players connected right now | `4` |
| `{system}` | Game system | `dnd5e` |
| `{version}` | Foundry version | `13.351` |

**Presence artwork:** `largeImageKey` accepts any public image URL — the
default is the official Foundry logo. Point it at your campaign art if you
like:

```json
"largeImageKey": "https://example.com/my-campaign-banner.png",
"largeImageText": "Foundry VTT — {world}"
```

**Rename a world on Discord:** map its id (logged the first time the world is
seen — tray → *View log…*) to any title:

```json
"worldDisplayNames": {
  "campaign_2": "The Third Expeditionary Fleet"
}
```

**Use your own Discord application:** create one in the
[Discord Developer Portal](https://discord.com/developers/applications) — its
*name* is what shows after "Playing" — and put its Application ID in
`discordApplicationId`.

## Troubleshooting

- **Discord shows nothing while a world is running** — check
  **Game Settings → Triston's Bridge Fork** in Foundry: if the bridge status
  isn't **Connected**, toggle **Enable MCP Bridge** off and on. The app
  recovers on its own within a poll or two.
- **"Playing …" never appears at all** — make sure the Discord *desktop* app is
  running (browser Discord can't show Rich Presence), and that
  **Enable Rich Presence** is checked in the tray menu.
- **Still stuck?** Tray → **View log…** (set `"logVerbosity": "Debug"` in
  config.json for a play-by-play). The log lives at
  `%APPDATA%\Tristons FoundryRPC\foundryrpc.log`.

## Credits

**Triston's FoundryRPC** is written and maintained by
**[triston-dev](https://github.com/triston-dev)**.

- World detection depends on
  [**Triston's Bridge Fork**](https://github.com/triston-dev/tristons-bridge-fork)
  of the Foundry MCP bridge.
- Discord Rich Presence via
  [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp) by Lachee.
- Not affiliated with Foundry Gaming LLC or Discord Inc. "Foundry VTT" and
  "Discord" are trademarks of their respective owners.

## License

[MIT](LICENSE) — © triston-dev
