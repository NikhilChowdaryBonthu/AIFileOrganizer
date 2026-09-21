# AI File Organizer

AI File Organizer is a local Mac desktop assistant for reviewing and organizing personal files. It runs on .NET and uses Ollama with `qwen3:4b` on the same Mac for document classification. Files are not uploaded to a cloud AI service.

The app is designed for review first: it scans files, shows a suggested destination for every item, and waits for the user to choose which files to move.

## What the app does

- Scan Downloads, Desktop, Documents, or a custom folder.
- Enter any scan size from 1 to 1000 files.
- Classify supported files using filename rules, folder context, and local Ollama when needed.
- Show the full destination path before a file is moved.
- Let the user tick or untick each scanned file.
- Move selected files into the default organized structure, a newly created Documents folder, or any existing folder selected in the app.
- Detect exact duplicate files among the current selected scan results by comparing file contents.
- Detect filename conflicts and keep separate versions safely.
- Send exact duplicate move conflicts to `~/Downloads/Duplicates_Review`.
- Undo the most recent app managed organization batch.
- Watch Downloads while the app is open and add new supported files to review. The watcher never moves files by itself.
- Permanently delete selected files only after a separate deletion confirmation is checked.

## Supported file types

The organizer supports PDFs, Word documents, text files, images, videos, audio files, ZIP/RAR/7Z archives, installers, spreadsheets, and presentations.

## How it works

1. Start Ollama and make sure the `qwen3:4b` model is installed.
2. Open the AI File Organizer app.
3. Choose the folders and the number of files to scan.
4. Review each result and its destination path.
5. Untick files you do not want to change.
6. Choose one action: organize using the suggested category, create a new Documents folder, choose an existing destination folder, find exact duplicates, or permanently delete selected files after the deletion confirmation.
7. Use Undo if you want to restore the latest app managed organization batch.

## Privacy and safety

- Classification runs locally through Ollama at `http://localhost:11434`.
- The app does not automatically move files.
- The Downloads watcher only creates review suggestions while the app is open.
- Move history is stored locally in SQLite at `~/Documents/AIFileOrganizer/Database`.
- Undo applies to moves made by the app. Manual Finder changes are not part of app Undo history.
- Permanent deletion cannot be undone by the app. The deletion control requires its own explicit checkbox.

## Requirements

- macOS on Apple Silicon for the packaged app
- Ollama running locally
- The `qwen3:4b` model:

```bash
ollama pull qwen3:4b
```

- .NET 10 SDK only when building or running from source

## Run from source

```bash
dotnet run
```

## Build the standalone Mac app

Create the app bundle with:

```bash
zsh scripts/package-macos.sh
```

This creates `dist/AI File Organizer.app`. It includes the .NET runtime, so VS Code and the .NET SDK are not required to open the packaged app. Ollama must still be running for AI based classification.

## Project files

- `Program.cs` contains scanning, classification, organization, duplicate detection, and local history logic.
- `Views/MainWindow.axaml` defines the desktop interface.
- `Views/MainWindow.axaml.cs` connects the buttons and review screen to the organizer logic.
- `scripts/package-macos.sh` builds the Apple Silicon `.app` bundle.

## Current status

Version 2 is a working local desktop organizer with manual review, custom destination folders, duplicate detection, Undo, optional Downloads watching, and standalone Mac app packaging.
