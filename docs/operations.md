# Windows operations

The Windows launcher starts one application instance at `http://127.0.0.1:3210` on the current PC. The [README](../README.md#install-and-start) covers extraction and first-time registration.

## Local data and backups

Open `%LOCALAPPDATA%\DM Command Table` in File Explorer while signed into the Windows account that runs the app.

| Entry                     | Contents                                            |
| ------------------------- | --------------------------------------------------- |
| `dm-command-table.sqlite` | Accounts, campaigns, settings, and combat history   |
| `uploads\`                | Uploaded images, including imported campaign assets |
| `backups\`                | Backups if this directory exists                    |

Save your work and press **Ctrl+C** in the launch console. Wait for the app to stop, then copy the entire data folder to private storage. Keep any SQLite sidecar files with the database. Browser drafts live in the browser profile, so save edits before backing up.

A campaign export is useful for transferring one campaign. Use the entire data folder to recover accounts, settings, campaigns and images together. Treat the folder as private because it contains account data and campaign content.

To restore, stop the app and move the existing data folder to a separate location. Copy the backed-up `DM Command Table` folder into `%LOCALAPPDATA%`, then launch a compatible version under the same Windows account. Verify login, a campaign and an embedded image before removing your previous copy.

## Update the Windows app

1. Stop the app and make a full backup.
2. Download the new Windows asset from [GitHub Releases](https://github.com/CoxkeLamagra/DM-Command-Table-Local/releases/latest) and extract it into a separate writable folder.
3. Launch the new `DMCT\Start.cmd` under the same Windows account. Sign in and verify your campaigns and images.
4. If you roll back across a database migration, use the earlier app with its matching pre-update data backup.

The **Update Application** tab comes from the shared hosted application. Windows updates use the ZIP workflow above.

## Troubleshooting

| Symptom                    | What to do                                                                                                           |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Windows rejects the ZIP    | Download the `windows-x64.zip` release asset for 12.1.5-r1 or later. Extract it to a short path such as `C:\Games`.  |
| Browser did not open       | Keep the console open and visit `http://127.0.0.1:3210` in your browser. Check the console if startup failed.        |
| Port 3210 is in use        | Return to the existing app window, or stop the process using port 3210 before starting the app.                      |
| First registration fails   | Use the token from the current launch console. After the first account is created, use **Sign in**.                  |
| Existing campaigns missing | Check the Windows account and its `%LOCALAPPDATA%\DM Command Table` folder.                                          |
| Catalogue unavailable      | Close and reopen **Import monsters** to retry, or choose a local 5etools JSON file. Saved monsters remain available. |

To compare a download with its published checksum, run `Get-FileHash -Algorithm SHA256 .\DM-Command-Table-12.1.5-windows-x64.zip` in PowerShell from the download folder. Match the displayed hash against the first value in the release's `.sha256` asset.

Administration shows storage usage and configured limits. If a save or import reports a capacity error, check free disk space and the storage metrics. Preserve the data folder when investigating a database or migration error.
