# AI File Organizer

A .NET console application that scans common laptop folders, classifies supported files with a local Ollama model, previews proposed moves, detects filename conflicts and exact duplicates, and records each organization batch in SQLite so it can be undone.

## Current workflow

1. Scan Downloads, Desktop, and Documents.
2. Choose how many files to include in the scan.
3. Review the proposed destinations.
4. Confirm the move operation.
5. Undo the most recent organization batch if needed.

## Requirements

- .NET 10 SDK
- Ollama running locally at `http://localhost:11434`
- The `qwen3:4b` model installed in Ollama

Run the project with:

```bash
dotnet run
```

The application creates its local SQLite history under `~/Documents/AIFileOrganizer/Database`. That data and build output are intentionally excluded from Git.
