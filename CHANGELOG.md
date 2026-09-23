# Changelog

## Version 2.1.1

- Switched local text classification to Ollama structured output with a 45-second request limit.
- Kept files visible for review with a filename/context suggestion if Ollama is unavailable.
- Added automated outage coverage and a live two-file Ollama smoke check.
- Replaced the illustrative preview with an actual app-interface image using synthetic files.

## Version 2.1

- Added a custom source-folder picker so sample files can be scanned without selecting personal folders.
- Added isolated automated checks for moves, filename conflicts, duplicates, Undo, and deletion confirmation.
- Enforced deletion confirmation in the file-operation method as well as the desktop interface.
- Updated app packaging to ad-hoc sign and verify the macOS bundle.
- Added direct release download and installation guidance to the README.

## Version 2

- Added the Avalonia Mac desktop interface.
- Added custom scan sizes from 1 to 1000 files.
- Added per file review checkboxes and destination previews.
- Added custom and existing destination folder selection.
- Added exact duplicate checks for current scan results.
- Added local move history and Undo.
- Added an optional Downloads watcher.
- Added standalone Apple Silicon Mac app packaging.
