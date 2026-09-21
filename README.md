# AI File Organizer

A local .NET console application that helps organize files on a personal computer. It reads supported files, uses Ollama for local document classification, proposes destinations, moves approved files, and records every move so it can be undone.

## What it can do

- Scan Downloads, Desktop, Documents, or a custom folder.
- Classify resumes, job documents, finance records, school documents, identity documents, media, archives, spreadsheets, and presentations.
- Use local Ollama with `qwen3:4b` for documents that need AI classification.
- Apply fast filename and folder rules before using AI.
- Show scan progress and reuse saved classifications after an interrupted scan.
- Skip common generated folders such as `bin`, `obj`, `.git`, and `node_modules`.
- Warn about read-only source folders before a move is attempted.
- Detect exact duplicates and filename conflicts.
- Review only files that could not be confidently categorized.
- Undo previous organization batches and restore files to their original folders.

## Requirements

- .NET 10 SDK
- [Ollama](https://ollama.com/) running locally at `http://localhost:11434`
- The `qwen3:4b` model installed:

```bash
ollama pull qwen3:4b
```

## Run the application

```bash
dotnet run
```

## Recommended workflow

1. Start with a small scan from one folder, such as Downloads.
2. Review files the organizer leaves as `Other/Uncategorized`.
3. Choose **Organize files** and confirm the batch only when the summary looks correct.
4. Use **Undo last organization** if you want to restore the most recent batch.

## Safety

Files are never deleted. The application asks for confirmation before moving files and stores local SQLite history for undo.

The application data is stored under `~/Documents/AIFileOrganizer/Database`. Local history, build output, and source backups are excluded from Git.

## Current status

Version 1 is a working console-based organizer. Future work may add a desktop interface, saved user preferences, and optional background monitoring for new files.
