# SoundHaven

A Windows music player for local libraries and YouTube audio — playlists,
seeking, native M4A downloads, themes, and optional Last.fm.

Preview software for **Windows 10/11 x64**. Packages are self-contained; end
users do not need the .NET runtime.

![SoundHaven](SoundHavenClient/Screenshots/soundhaven_1.png)
![Search](SoundHavenClient/Screenshots/Search.png)

## Features

- Local playback with playlists, metadata, repeat, shuffle, and seeking
- YouTube Music catalogue search and general YouTube video search
- AAC/M4A streaming via YoutubeExplode, with reliable repeated seeks
- Native M4A downloads (no FFmpeg, no re-encoding)
- SQLite library under `%LocalAppData%\SoundHaven`
- Optional Last.fm recommendations and scrobbling
- Custom and artwork-derived themes

Use YouTube only for media you are allowed to access or save, and follow
YouTube’s terms and applicable law.

## Install

Grab a build from
[GitHub Releases](https://github.com/XavierRHMN/SoundHaven/releases).

**Portable ZIP** — download `SoundHaven-<version>-win-x64.zip` and its
`.sha256` checksum, verify, extract, then run `SoundHaven\SoundHavenClient.exe`:

```powershell
Get-FileHash .\SoundHaven-<version>-win-x64.zip -Algorithm SHA256
Get-Content .\SoundHaven-<version>-win-x64.zip.sha256
```

**Installer** — user-scope install under `%LocalAppData%\Programs\SoundHaven`
(no admin). Downloads a release, checks SHA-256, and upgrades atomically:

```powershell
Invoke-WebRequest `
  https://raw.githubusercontent.com/XavierRHMN/SoundHaven/master/SoundHaven_Install.ps1 `
  -OutFile .\SoundHaven_Install.ps1

powershell -NoProfile -ExecutionPolicy Bypass -File .\SoundHaven_Install.ps1
```

Use `-Version 0.1.0` for a specific release. Uninstall with
`SoundHaven_Uninstall.ps1` or the Start Menu shortcut; user data is kept unless
you pass `-PurgeUserData`.

| Path | Location |
| --- | --- |
| App files | Extraction folder, or `%LocalAppData%\Programs\SoundHaven` |
| Database & cache | `%LocalAppData%\SoundHaven` |
| YouTube downloads | Current user’s Music folder |

Upgrades and uninstalls leave the database in place.

## Configuration

SoundHaven runs without Last.fm. To enable it, create a Last.fm API app and set:

```powershell
setx LASTFM_API_KEY "your-api-key"
setx LASTFM_API_SECRET "your-api-secret"
```

Restart the app afterward. Passwords are entered in a masked control and are
never stored. Do not commit API credentials — use environment variables or a
local `.env` file (see `.env.example`). Files like `YT_API_KEY.txt` and `.env`
are gitignored.

YouTube Music song search requires an Innertube API key:

```powershell
setx YOUTUBE_INNERTUBE_API_KEY "your-innertube-api-key"
```

If credentials were ever committed to git, rotate them in the provider console
and treat the old values as compromised (`.gitignore` only prevents future
commits; it does not remove secrets from history).

## Develop

Requires Windows 10/11 x64, the
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and PowerShell.

**Run**

```powershell
dotnet restore .\SoundHaven.sln --locked-mode
dotnet run --project .\SoundHavenClient\SoundHavenClient.csproj -c Release
```

From `SoundHavenClient`, `dotnet run -c Release` works the same. From the
solution root you must pass `--project` (the root is not a single runnable
project).

**Test**

```powershell
dotnet restore .\SoundHaven.sln --locked-mode
dotnet format .\SoundHaven.sln --verify-no-changes --no-restore
dotnet build .\SoundHaven.sln -c Release --no-restore -warnaserror
dotnet test .\SoundHaven.sln -c Release --no-build --no-restore --filter "Category!=Live"
```

Default tests stay offline. Opt-in live YouTube smoke (search, stream, seek,
cache) against the public `jNQXAC9IVRw` video:

```powershell
$env:SOUNDHAVEN_RUN_LIVE_TESTS = "1"
dotnet test .\SoundHaven.Tests\SoundHaven.Tests.csproj -c Release --filter "Category=Live"
```

**Release package**

```powershell
.\scripts\Build-Release.ps1 -Version 0.1.0
```

Produces `artifacts\release\SoundHaven-0.1.0-win-x64.zip` and its `.sha256`.
Manual workflow runs upload artifacts; pushing a `v*` tag publishes a GitHub
Release.

Before shipping, spot-check from a clean ZIP: local play/seek, YouTube
search/seek/download, playlists, offline notifications, and
install/upgrade/uninstall without losing user data.

## License

[GNU LGPL 2.1 or later](LICENSE) — chosen so TagLibSharp (LGPL-2.1) metadata
and artwork support can ship cleanly. Packages include
[third-party notices](THIRD_PARTY_NOTICES.md) and dependency license texts.
