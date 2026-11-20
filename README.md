# Lunar Roleplay Launcher

Windows launcher for the Lunar Roleplay HogwartsMP server. The player signs in with Discord, the launcher
checks they hold an allowed role (whitelist or staff), installs the ArcaneMP build approved by staff, then starts
the game through the ArcaneMP launcher with a join link:

```
arcanemp://<host>:<port>?password=<server password>&ticket=<Discord access token>
```

The game server verifies the ticket with Discord itself and links the player's signed ArcaneMP
`accountId` to their Discord account. The launcher is **not trusted**: it holds no secrets, and a
hand-made link still needs a real Discord sign-in with the role. Its own role check exists only to show a
clear message early.

This repository is public so anyone can check what the launcher does. It contains no secrets, server
addresses or passwords; those live only in a git-ignored `launcher.json`.

## What it does on your PC

- Opens your browser on Discord's official sign-in page. The launcher never sees your Discord password.
- Asks Discord only for `identify` (your user ID and name) and `guilds.members.read` (your roles in the
  Lunar Roleplay server). It cannot read messages, servers you are in, or your email.
- Listens on `127.0.0.1` (your own PC only, not the network) for up to 3 minutes to receive the sign-in.
- Keeps the Discord token in memory only and passes it to the game when you press **Gioca**. It is never
  written to disk.
- Reads [`client-version.json`](client-version.json) from this repository to learn which ArcaneMP build is
  approved, downloads that build from MafiaHub's official server (`api.mafiahub.dev`), checks its SHA-256, and
  extracts only the `client` folder to `%LOCALAPPDATA%\LunarRoleplay\ArcaneMP\client`. The server folders
  in the same zip are skipped. The launcher never hosts or changes ArcaneMP files.
- Copies `ArcaneMP_launcher.json` (your Hogwarts Legacy folder) from an existing ArcaneMP install, if any, so
  you do not have to pick the game again.
- Reads your public ArcaneMP account ID from `client\logs\ArcaneMP.log` to show it for support. It never
  opens `account.key`, the private key that identifies you: never share that file with anyone. The key lives
  in `%LOCALAPPDATA%\MafiaHub`, so installs and updates never change your account ID.

## How it works

1. **Accedi con Discord** opens Discord's authorize page with the scopes above. Discord redirects to
   `http://127.0.0.1:<redirectPort>/callback`, where the launcher listens on a raw loopback socket (no
   admin rights needed).
2. The token is used once against `GET /users/@me/guilds/<guildId>/member` to read the display name and roles.
3. **Gioca** first makes sure the approved ArcaneMP build is installed (see below), then starts
   `ArcaneMPLauncher.exe` directly with the link as its argument. It does not rely on the
   `arcanemp://` / `hogwartsmp://` URL handlers, which can point to an outdated exe.
4. **Ripara** downloads and reinstalls the approved build.

## ArcaneMP version control

Staff decide when players update. The approved build is in [`client-version.json`](client-version.json) on
`main`; the launcher reads it from `raw.githubusercontent.com` each time **Gioca** is pressed.

| Field | Meaning |
| --- | --- |
| `version` | Shown to players, e.g. `1.15.1` |
| `modId`, `buildId` | The build's IDs in its MafiaHub download URL `https://api.mafiahub.dev/mods/<modId>/builds/<buildId>/download` |
| `clientFolder` | Folder inside the zip that holds the client, e.g. `ArcaneMP-1.15.1/client` |
| `sha256`, `size` | Checksum and size in bytes of the zip; a download that does not match is refused |

On **Gioca** the launcher:

1. Reinstalls when the installed build differs from the approved one, or when `ArcaneMPClient.dll` or
   `ArcaneMPLauncher.exe` no longer match the hashes recorded at install (for example after ArcaneMP updated
   itself).
2. Downloads the zip with a progress readout, verifies size and SHA-256, extracts the client folder to a
   staging folder, then swaps it in. The previous client is kept until the swap succeeds. The game must be closed.
3. If `client-version.json` cannot be reached, keeps playing on the build already installed.

To approve a new build:

1. Update the game server to the new HogwartsMP/ArcaneMP version first, or at the same time.
2. Download the build's zip from its MafiaHub URL, then compute its checksum and size:
   ```powershell
   Get-FileHash ArcaneMP-x.y.z.zip -Algorithm SHA256   # sha256 (write it in lowercase)
   (Get-Item ArcaneMP-x.y.z.zip).Length               # size
   ```
3. Check the zip's top folder name for `clientFolder`, update `client-version.json`, and push it to `main`.
   Players get the new build the next time they press **Gioca** (GitHub may cache the file for a few minutes).

ArcaneMP's own launcher also checks MafiaHub for updates on start. If it moves to a newer build, the launcher
notices the changed files on the next **Gioca** and puts the approved build back.

## Discord application setup

1. In the [Discord Developer Portal](https://discord.com/developers/applications), create an application.
2. **OAuth2 → Redirects**: add exactly `http://127.0.0.1:53134/callback` (or your `redirectPort`).
3. **OAuth2 → Public Client**: turn it on for `authFlow: "pkce"`, so the code can be exchanged without a
   client secret. If Discord still answers `invalid_client`, set `authFlow` to `"implicit"`.
4. Never put the client secret in the launcher. It is not needed.

## Configuration

Copy `launcher.example.json` to `launcher.json` in the repository root and fill it in. `launcher.json` is
git-ignored and embedded into the exe at build time; a `launcher.json` next to the exe overrides it.

| Field | Meaning |
| --- | --- |
| `clientId` | Discord application ID (public) |
| `guildId` | Lunar Roleplay Discord server ID |
| `roleIds` | Role IDs that may play, e.g. the whitelist role and the staff role; any one is enough |
| `serverHost`, `serverPort` | Game server address (default port 27015) |
| `serverPassword` | Server password, or `""` |
| `redirectPort` | Loopback port registered as the Discord redirect |
| `authFlow` | `"pkce"` (preferred) or `"implicit"` |

Everything in `launcher.json` ends up inside the published exe and can be read by anyone who has it,
including `serverPassword`. Treat it as public; the whitelist on the server is what keeps people out.

## Build

Requires the .NET 9 SDK.

```powershell
dotnet build src/LunarLauncher                       # development build
dotnet publish src/LunarLauncher -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The published `publish/LunarLauncher.exe` runs without installing .NET. It is unsigned, so Windows
SmartScreen warns on first launch until it is code-signed.

## Security notes

- The ticket is a Discord access token limited to `identify` and `guilds.members.read`. It passes to the game
  as a process argument and is not saved by the launcher. It cannot be revoked from the launcher (Discord's
  revoke endpoint needs the client secret); it expires on its own.
- The server must treat the ticket as the only proof of Discord identity. Steam, Epic, Discord and hardware
  IDs reported by the game client can be forged.
- A future website with Discord login can issue the same link; the server check is the same.
