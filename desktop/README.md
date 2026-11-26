# DM Command Table Local for WinUI

This development build uses WinUI 3 for its window, navigation, forms, dialogs, and file pickers. It starts the existing DM Command Table engine as a hidden loopback process and stores data under `%LOCALAPPDATA%\DM Command Table`. Close the portable browser edition before launching the desktop client on the same data folder. Back up that folder before testing the development build.

## Build on Windows

1. Build the engine ZIP from this checkout following [Windows packaging](../docs/development.md#build-the-windows-zip).
2. On Windows with .NET 8 SDK and the Windows development tools installed, run `./desktop/package.ps1 -EngineZip <path-to-engine-zip>`.
3. Start `dist/DMCommandTable-WinUI/DMCommandTable.exe`. Keep the full output folder together.

The **Native Windows app** GitHub Actions workflow builds the self-contained desktop folder and runs the engine smoke test. The folder includes .NET, the Windows App SDK, and the existing Node engine.

## Move campaigns and import monsters

Use **Campaign → Import campaign** to open either the plain JSON or **Export with images** file from the hosted app. **Export JSON** and **Export with images** use the same formats and data validation as the web edition. To import monsters, open **Bestiary**, enter a name or source, and choose **Search online**. Select an import result. You can also open and search a local 5etools JSON file.

## Development status

The desktop client includes local login and registration, campaign transfer, roster and record editing, basic prepared encounter editing and loading, core combat, search, and Bestiary catalogue imports. Rich-text fields currently expose their stored HTML. The browser edition still provides visual rich-text editing, full prepared encounter authoring, advanced combat controls, and the full administration workflow.
