# Triston's FoundryRPC

**by [triston-dev](https://github.com/triston-dev)**

Show off which **Foundry VTT** campaign you're running — right on your Discord
profile. Triston's FoundryRPC is a tiny Windows tray app that sets your Discord
Rich Presence to the world you have loaded, with a live session timer:

> **Playing Triston's FoundryRPC**
> Running The Third Expeditionary Fleet
> 3 online · ⏱ 1:18

- Sits quietly in the system tray — no window, no console.
- **Completely standalone** — no Foundry modules, no browser extensions, no
  companion software. Just this exe and your world's address.
- Works no matter **where your Foundry server is hosted** — localhost, VPS, or
  hosting providers like Sqyre. Talks to Foundry the same way your login
  screen does, so it shows the world's real title.
- Presence clears automatically when you close your world.

---

## Requirements

| What | Why |
|---|---|
| Windows 10/11 | It's a Windows tray app |
| [Discord desktop app](https://discord.com/download) | Rich Presence only works with the desktop client, running on the same PC |
| Your Foundry world URL(s) | The address you open to play — that's all the app needs |

## Installation

### Step 1 — Get Triston's FoundryRPC

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

### Step 2 — Run it and add your world URL(s)

Double-click the exe — a tray icon appears. Right-click it →
**Configure Foundry Server URLs…** and paste the address you open to play,
one per line (you can paste the full play link; the app trims it):

```
https://my-world.example-host.app
http://localhost:30000
```

Every URL is checked on each poll; whichever has an active world is shown.
Within ~15 seconds of a world being up, your Discord presence shows it.
That's the whole install.

To have it start with Windows: right-click the tray icon →
**Run at Windows startup**.

## How it detects your world

No modules, no logins. Each poll (every 15s) the app asks your server the same
things Foundry's own login screen asks:

1. First it tries Foundry's built-in `GET /api/status` endpoint (works on
   directly-exposed servers like `localhost:30000`).
2. If the host's proxy hides that (hosting providers often do), it speaks
   Foundry's own Socket.IO protocol as an anonymous visitor — the same
   pre-login channel your join screen uses — which returns the world's
   **actual title** and who's online. Nothing is written to your world, and
   no credentials are involved.

The working method is remembered per server. If neither works, the world just
shows as idle — the app never crashes over an unreachable server.

## Using the tray menu

Right-click the tray icon:

| Menu item | What it does |
|---|---|
| *(status line)* | Shows the current world / idle state |
| **Enable Rich Presence** | Turn the Discord presence on or off |
| **Configure Foundry Server URLs…** | The world address(es) to watch, one per line |
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

- **Discord shows nothing while a world is running** — double-check the URL in
  **Configure Foundry Server URLs…** is exactly the address you open to play
  (paste the full play link; the app trims `/game` etc. automatically), then
  wait one poll (~15s).
- **"Playing …" never appears at all** — make sure the Discord *desktop* app is
  running (browser Discord can't show Rich Presence), and that
  **Enable Rich Presence** is checked in the tray menu.
- **Still stuck?** Tray → **View log…** (set `"logVerbosity": "Debug"` in
  config.json for a play-by-play). The log lives at
  `%APPDATA%\Tristons FoundryRPC\foundryrpc.log`.

## Credits

**Triston's FoundryRPC** is written and maintained by
**[triston-dev](https://github.com/triston-dev)**.

- Discord Rich Presence via
  [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp) by Lachee.
- Versions 1.x detected worlds through
  [Triston's Bridge Fork](https://github.com/triston-dev/tristons-bridge-fork)
  of the Foundry MCP bridge; since v2.0.0 the app is fully standalone and the
  bridge is no longer used (it remains a great tool for running your game
  with Claude).
- Not affiliated with Foundry Gaming LLC or Discord Inc. "Foundry VTT" and
  "Discord" are trademarks of their respective owners.

## License

[MIT](LICENSE) — © triston-dev
