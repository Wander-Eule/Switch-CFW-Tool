# Switch-CFW-Tool

Windows tool (C# / WPF, .NET 8) for automatically assembling a ready-to-use `SDMC` folder
from Hekate, Atmosphère, sys-patch and other homebrew tools, or for updating an existing SD
card directly.

## Building

Requirement: [.NET 8 SDK](https://dotnet.microsoft.com/download) on a Windows machine.

```
dotnet build -c Release
```

For a finished, self-contained `.exe` (no .NET runtime install needed on the user's side):

```
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
```

The `.exe` then ends up at `bin/Release/net8.0-windows/win-x64/publish/Switch-CFW-Tool.exe`.

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

## Features

- Latest/Custom selection per tool, 31 tools in the catalog (`Data/ToolCatalog.cs`)
- SysMMC/EmuMMC selection with automatic `exosphere.ini` and DNS-MITM hostlist
- Manage CFW: detect, update, or uninstall tools already on an existing SD card
- Theming for Ultrahand-Overlay (54 parameters, live preview) and Hekate background/wallpaper
- Boot-integrity check after the build
- Help → Log: shows/hides the output log (hidden by default)
- Help → FAQ: placeholder window, content coming later

## Worth checking before the first real run

The `AssetNamePattern` values in `ToolCatalog.cs` are set to the best of my knowledge, but
not verified against an actual download for every tool — on failure the log names the
filenames actually present in the release, so the pattern is easy to fix.

## License

[GPL-3.0](LICENSE)
