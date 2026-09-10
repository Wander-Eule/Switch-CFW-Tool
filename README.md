# Switch-CFW-Tool

Windows-Tool (C# / WPF, .NET 8) zum automatischen Zusammenstellen eines fertigen
`SDMC`-Ordners aus Hekate, Atmosphère, sys-patch und weiteren Homebrew-Tools, oder zum
direkten Aktualisieren einer bestehenden SD-Karte.

## Bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download) auf einem Windows-Rechner.

```
dotnet build -c Release
```

Für eine fertige, eigenständige `.exe` (kein .NET-Runtime-Install beim Nutzer nötig):

```
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
```

Die `.exe` liegt danach unter `bin/Release/net8.0-windows/win-x64/publish/Switch-CFW-Tool.exe`.

## Projektstruktur

- `Models/CfwTool.cs` – Beschreibung eines Tools (Name, Autor, Repo, Asset-Muster, Footprint, ...)
- `Data/ToolCatalog.cs` – **einzige Stelle**, an der neue Tools ergänzt werden
- `Services/GitHubService.cs` – GitHub-API: Release-Abfrage, Asset-Auswahl, Rate-Limit-Meldungen
- `Services/DownloadService.cs` – Download + Entpacken in einen Staging-Ordner
- `Services/SdCardBuilder.cs` – ordnet Staging-Ordner in die SD-Struktur ein, leitet das Ziel aus dem tatsächlichen Archivinhalt ab statt aus Annahmen
- `Services/ToolInstaller.cs` – installiert ein Tool, genutzt von Build **und** Manage CFW
- `Services/InstallManifest.cs` / `Services/ToolDetector.cs` – Verzeichnisdatei bzw. Erkennung fremd installierter Tools
- `Services/HekateIniService.cs` – secmon-Zeilen, KIP-Pflege, Abschnittsfilterung in `hekate_ipl.ini`
- `Services/MmcConfigService.cs` – schreibt `exosphere.ini` passend zu SysMMC/EmuMMC und die DNS-MITM-Blockliste
- `Services/BundledAssetsService.cs` – packt die eingebettete `hekate_ipl.ini` (+ zweite ini in `bootloader/ini/`) und die `bootloader/res`-Icons aus
- `Services/BootIntegrityCheck.cs` – prüft nach dem Build, ob die für den Boot nötigen Dateien vollständig sind
- `Services/UltrahandTheme.cs` / `Services/UltrahandWallpaper.cs` / `Services/HekateWallpaper.cs` – Theming
- `MainWindow.xaml(.cs)` – Hauptfenster: Toolliste, Latest/Custom, SysMMC/EmuMMC, Build
- `ManageCfwWindow.xaml(.cs)` – installierte Tools auf einer SD-Karte verwalten
- `ThemingWindow.xaml(.cs)` – Ultrahand-Theme, Hekate-Hintergrund, Wallpaper
- `InfoWindow.xaml(.cs)` – Help → Info: Credits für alle enthaltenen Homebrew-Tools

## Features

- Latest/Custom-Auswahl pro Tool, 31 Tools im Katalog (`Data/ToolCatalog.cs`)
- SysMMC/EmuMMC-Auswahl mit automatischer `exosphere.ini` und DNS-MITM-Hostliste
- Manage CFW: installierte Tools auf einer bestehenden SD-Karte erkennen, updaten, deinstallieren
- Theming für Ultrahand-Overlay (54 Parameter, Live-Vorschau) und Hekate-Hintergrund/Wallpaper
- Boot-Integritätsprüfung nach dem Build
- Help → Log: blendet das Ausgabe-Log ein/aus (standardmäßig ausgeblendet)
- Help → FAQ: Platzhalter-Fenster, Inhalt folgt

## Wichtig, vor dem ersten echten Lauf prüfen

Die `AssetNamePattern`-Werte in `ToolCatalog.cs` sind nach bestem Wissen gesetzt, aber nicht
für jedes Tool durch einen echten Download verifiziert — bei einem Fehlschlag nennt das Log
die tatsächlich im Release vorhandenen Dateinamen, damit sich das Muster leicht korrigieren
lässt.

## Lizenz

[GPL-3.0](LICENSE)
