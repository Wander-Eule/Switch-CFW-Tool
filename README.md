<p align="center">
  <img src="Assets/appicon.ico" width="96" alt="Switch-CFW-Tool" />
</p>

<h1 align="center">Switch-CFW-Tool</h1>

<p align="center">
  <strong>Latest version: v1.3</strong> · Windows · Linux · macOS
</p>

Cross-platform tool (C# / Avalonia UI, .NET 8) for automatically assembling a ready-to-use `SDMC` folder
from Hekate, Atmosphère, sys-patch and other homebrew tools, or for updating an existing SD
card directly on Windows, Linux, and macOS.

## Downloads

Pre-built self-contained binaries (no .NET runtime required) are available for:

- **Windows** (`win-x64`) – `Switch-CFW-Tool.exe`, a single file that runs as is
- **Linux** (`linux-x64`) – `Switch-CFW-Tool-linux-x64.tar.gz`
- **macOS Apple Silicon** (`osx-arm64`) – `Switch-CFW-Tool-macos-arm64.tar.gz`
- **macOS Intel** (`osx-x64`) – `Switch-CFW-Tool-macos-x64.tar.gz`

Download the latest release from the [Releases](../../releases) page, or grab the latest CI build artifacts from [GitHub Actions](../../actions/workflows/build.yml).

**Linux:** unpack and start `Switch CFW Tool/Switch-CFW-Tool` (the archive keeps the executable bit).

**macOS:** the app isn't signed with an Apple developer certificate, so Gatekeeper blocks the first
start. Either right-click the file → *Open* → *Open*, or allow it under *System Settings → Privacy &
Security → Open Anyway*. From a terminal, `xattr -dr com.apple.quarantine "Switch CFW Tool"` does the
same for the unpacked folder.

## Building

Requirement: [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer).

```
dotnet build -c Release
```

For a finished, self-contained binary (no .NET runtime install needed on the user's side):

```
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
dotnet publish -c Release -r linux-x64 -p:SelfContained=true -p:PublishSingleFile=true
dotnet publish -c Release -r osx-arm64 -p:SelfContained=true -p:PublishSingleFile=true
dotnet publish -c Release -r osx-x64 -p:SelfContained=true -p:PublishSingleFile=true
```

The binary then ends up at `bin/Release/net8.0/<rid>/publish/Switch-CFW-Tool` (or `.exe` on Windows).

Releases are built by GitHub Actions (`.github/workflows/build.yml`): every push to `main` builds all
four platforms as workflow artifacts, and pushing a tag `v<Version>` (e.g. `v1.3`) additionally
publishes them as a GitHub release. Keep `<Version>` in `Switch-CFW-Tool.csproj` in step with the tag.

For a local Windows build, `build-release.cmd` (double-click) still works: it publishes as above and
packs the result into `release/Switch-CFW-Tool-v<Version>.zip`, containing a `Switch CFW Tool` folder
with the `.exe` and `LICENSE`.

## Project structure

- `Models/CfwTool.cs` – description of a tool (name, author, repo, asset pattern, footprint, ...)
- `Data/ToolCatalog.cs` – **the only place** where new tools are added
- `Services/GitHubService.cs` – GitHub API: release lookup, asset selection, rate-limit messages
- `Services/DownloadService.cs` – download + extraction into a staging folder
- `Services/SdCardBuilder.cs` – merges staging folders into the SD structure, deriving the destination from the actual archive contents rather than assumptions
- `Services/ToolInstaller.cs` – installs one tool, used by both Build and Manage CFW
- `Services/InstallManifest.cs` / `Services/ToolDetector.cs` – install record and detection of tools installed by other means
- `Services/HekateIniService.cs` – secmon lines, KIP upkeep, section filtering in `hekate_ipl.ini`
- `Services/MmcConfigService.cs` – writes `exosphere.ini` for SysMMC/EmuMMC and the DNS-MITM blocklist
- `Services/BundledAssetsService.cs` – extracts the embedded `hekate_ipl.ini` (+ a second ini in `bootloader/ini/`) and the `bootloader/res` icons
- `Services/BootIntegrityCheck.cs` – checks after the build whether the files needed to boot are actually complete
- `Services/UltrahandTheme.cs` / `Services/UltrahandWallpaper.cs` / `Services/HekateWallpaper.cs` – theming
- `MainWindow.xaml(.cs)` – main window: tool list, Latest/Custom, SysMMC/EmuMMC, Build
- `ManageCfwWindow.xaml(.cs)` – manage tools already installed on an SD card
- `ThemingWindow.xaml(.cs)` – Ultrahand theme, Hekate background, wallpaper
- `InfoWindow.xaml(.cs)` – Help → Info: credits for every included homebrew tool
- `Localization/` – all user-facing text: `en.json` (reference) and one file per translation, loaded by `Loc.cs`

## Features

- Latest/Custom selection per tool, 31 tools in the catalog (`Data/ToolCatalog.cs`)
- SysMMC/EmuMMC selection with automatic `exosphere.ini` and DNS-MITM hostlist
- Manage CFW: detect, update, or uninstall tools already on an existing SD card
- Theming for Ultrahand-Overlay (54 parameters, live preview) and Hekate background/wallpaper
- Boot-integrity check after the build
- Help → Log: shows/hides the output log (hidden by default)
- Help → FAQ: placeholder window, content coming later
- Language menu: English (default), Deutsch, Español, Français and Português (Brasil), switchable at runtime. Settings (`language.txt`, `quicktour.done`, `github_token.bin`) are stored in your user config directory (`%AppData%\Switch-CFW-Tool` on Windows, `~/Library/Application Support/Switch-CFW-Tool` on macOS, `~/.config/Switch-CFW-Tool` on Linux), so the program's own folder stays nothing but the binary and `LICENSE`

## Translations

Every text the program shows lives in `Localization/en.json`, keyed like `"Main.Build": "Build CFW"`.
To add a language, copy `en.json` to `<code>.json` in the same folder (e.g. `it.json`), set
`"_meta.languageName"` to the language's own name and translate the values. The file is embedded
automatically and shows up in the Language menu after a rebuild. Keys a translation leaves out
fall back to English; placeholders like `{0}` must stay as they are.

## Worth checking before the first real run

The `AssetNamePattern` values in `ToolCatalog.cs` are set to the best of my knowledge, but
not verified against an actual download for every tool — on failure the log names the
filenames actually present in the release, so the pattern is easy to fix.

## License

[GPL-3.0](LICENSE)
