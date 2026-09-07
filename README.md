# Switch-CFW-Tool

Windows-Tool (C# / WPF, .NET 8) zum automatischen Zusammenstellen eines fertigen
`sdmc`-Ordners aus Hekate, Atmosphère, sys-patch und weiteren Homebrew-Tools, oder zum
direkten Aktualisieren einer bestehenden SD-Karte.

## Bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download) auf einem Windows-Rechner.

```
dotnet build -c Release
```

Für eine fertige, eigenständige `.exe` (User braucht keine .NET-Runtime installiert):

```
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
```

Die `.exe` liegt danach unter `bin/Release/net8.0-windows/win-x64/publish/Switch-CFW-Tool.exe`.

## Projektstruktur

- `Models/CfwTool.cs` – Beschreibung eines Tools (Name, Autor, Repo, Tooltip-Text, Asset-Muster)
- `Data/ToolCatalog.cs` – **einzige Stelle**, an der neue Tools ergänzt werden
- `Services/GitHubService.cs` – GitHub-API: neuestes Release / alle Releases, Asset-Auswahl per Wildcard
- `Services/DownloadService.cs` – Download + ZIP-Extraktion in einen Staging-Ordner
- `Services/SdCardBuilder.cs` – merged die Staging-Ordner in den finalen `sdmc`-Ordner, lässt bekannte User-Configs beim erneuten Bauen unangetastet
- `Services/MmcConfigService.cs` – schreibt `exosphere.ini` (SD-Root) passend zu SysMMC/EmuMMC und pflegt die DNS-MITM-Blockliste für Nintendo-Server
- `Services/BundledAssetsService.cs` – packt die eingebettete `hekate_ipl.ini` (+ zweite ini in `bootloader/ini/`) und die vier `bootloader/res`-Icons aus
- `Services/CompatibilityService.cs` – vergleicht Firmware-Hinweise aus den Release Notes, warnt bei Abweichungen
- `MainWindow.xaml(.cs)` – UI: Menüleiste, Checkboxen mit Hover-Tooltips, Latest/Custom, SysMMC/EmuMMC, Build-Button, ausblendbares Log
- `InfoWindow.xaml(.cs)` – Help → Info Fenster mit Credits

## Features

- Latest/Custom-Auswahl pro Tool
- SysMMC/EmuMMC-Auswahl mit automatischer `exosphere.ini` (inkl. `blank_prodinfo_*` passend zum gewählten Modus) und DNS-MITM-Hostliste
- Datei → **Select your SD Card with CFW to update**: wählt eine bestehende SD-Karte direkt als Ziel (Update-Modus, Configs bleiben erhalten)
- Help → **Tutorial**: Platzhalter-Fenster, Inhalt folgt
- Help → **Info**: Credits für alle enthaltenen Homebrew-Tools + Patreon-Link
- Help → **Log**: blendet das Ausgabe-Log ein/aus (standardmäßig ausgeblendet)
- Standard-Ausgabeordner: `%USERPROFILE%\Downloads\sdmc`
- 24 Tools insgesamt, siehe `Data/ToolCatalog.cs` für die vollständige Liste inkl. Sonderbehandlungen (Emuiibo SdOut-Entpacken, EdiZon-SE mit gebündeltem Overlay, TegraExplorer als Payload, etc.)

## Wichtig, vor dem ersten echten Lauf prüfen

Ich hatte in dieser Sandbox **keinen Internetzugriff**, konnte die tatsächlichen Release-
Asset-Namen der Repos also nicht verifizieren. Die `AssetNamePattern`-Werte in
`ToolCatalog.cs` sind nach bestem Wissen gesetzt, aber bei jedem Tool solltest du beim
ersten Durchlauf im Log kontrollieren, ob wirklich die richtige Datei aus dem Release
gezogen wurde.

Die zweite `hekate_ipl.ini` (in `bootloader/ini/`) referenziert `bootloader/payloads/TegraExplorer.bin` –
Tegra Explorer steht nicht auf der Tool-Liste und wird von diesem Programm nicht heruntergeladen,
dieser Menüpunkt in Hekate würde also ins Leere laufen.

Die Kompatibilitätsprüfung ist ein Best-Effort-Abgleich der Firmware-Angaben aus den
Release-Notes-Texten, keine offizielle Matrix – sie warnt nur, blockiert aber keinen Build.
