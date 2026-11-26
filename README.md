# DM Command Table Local

DM Command Table Local is a portable Windows app for planning and running tabletop RPG campaigns. Use it to manage Players and NPCs, build a Bestiary, prepare Stories, Sessions and encounters, and track combat. The app opens in your browser and stores campaigns and images on your PC.

## Install and start

1. Download the Windows x64 ZIP from [GitHub Releases](https://github.com/CoxkeLamagra/DM-Command-Table-Local/releases/latest). The current package is [12.1.5-r1](https://github.com/CoxkeLamagra/DM-Command-Table-Local/releases/tag/windows-v12.1.5-r1).
2. Right-click the ZIP and choose **Extract All**. Use a short, writable path on a local NTFS drive, such as `C:\Games`. Open the extracted `DMCT` folder and double-click **Start.cmd**.
3. The app opens in your default browser at `http://127.0.0.1:3210`. Choose **Register** on first launch and enter the token printed in the console. Sign in with that local account on later launches.
4. Keep the console open while using the app. Save your work and press **Ctrl+C** in the console when finished.

You need Windows 10 or 11 (x64) and a modern browser. The ZIP includes Node.js, SQLite and the application dependencies. After download, installation and ordinary use work offline.

## Bring a web campaign offline

On [dm.lamagra.link](https://dm.lamagra.link), open your campaign and choose **Campaign actions → Export with images**. On your PC, open the local app and choose **Campaign actions → Import**, then select the downloaded `campaign.dmct.json`. The import creates a local campaign copy. Plain **Export** JSON transfers records as well. Use the image package to keep embedded images. Web and local accounts are separate, and later edits stay in the copy you edit.

In **Bestiary → Import monsters**, the browser connects to the online catalogue at `dnd5e.lamagra.link` when reachable. Import the monsters you want before going offline. You can also import a local 5etools-compatible JSON file or create monsters manually offline.

## Updates and backups

The app stores its database and images under `%LOCALAPPDATA%\DM Command Table`, separate from the extracted ZIP. For a full backup, stop the app and copy that entire folder. To update, back up your data, extract the new ZIP into a separate folder, and launch its **Start.cmd** under the same Windows account. See [Windows operations](docs/operations.md) for recovery and troubleshooting.

## Documentation

- [User guide](docs/user-guide.md) for campaigns, import, Bestiary, preparation, and combat.
- [Windows operations](docs/operations.md) for storage, backups, updates, and troubleshooting.
- [Architecture](docs/architecture.md) and [local API reference](docs/api.md) for implementation details.
- [Development and packaging](docs/development.md) for building from source and running checks.
- [Changelog](CHANGELOG.md) for Windows packages and inherited application changes.

This is a hobby project. No open-source license has been assigned. Retained third-party UI and stylesheet notices are in `vendor/`.
