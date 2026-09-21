# Architecture

```mermaid
flowchart LR
    A[User selects folders] --> B[Safe file scan]
    B --> C[Filename and folder rules]
    C --> D{Needs document AI?}
    D -- Yes --> E[Local Ollama qwen3:4b]
    D -- No --> F[File context classification]
    E --> G[Review list]
    F --> G
    G --> H[User chooses destination and confirms]
    H --> I[Move, duplicate handling, and local history]
    I --> J[Undo latest app managed batch]
```

The desktop user interface is built with Avalonia. Core scanning and organization logic is in `Program.cs`. SQLite stores local classification cache and move history. Ollama runs locally and is used only when filename and folder rules are not enough.
