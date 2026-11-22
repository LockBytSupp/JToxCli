# JTox CLI

A lightweight, feature-rich terminal Tox messenger written in C# (Mono/.NET), featuring an interactive multi-pane TUI, real-time status updates, local friend aliases, and detailed profile inspection.

---

## Features

- **Interactive Terminal UI (TUI)**: Split-pane terminal layout with a friend list sidebar, active chat history, and notification banner.
- **Advanced Input & Navigation**:
  - Left/Right cursor arrow navigation.
  - Home/End key support.
  - Command & message history (Up/Down arrow keys).
  - Chat history scrolling (Up/Down arrows when input is empty, PageUp/PageDown, Home/End).
- **Presence Status & Custom Messages**:
  - Presets: `/status ok` (Online), `/status afk` (Away), `/status dnb` (Busy / Do Not Disturb).
  - Custom status text messages via `/status <message>` (preserving presence state).
- **Real-Time Status & Color Badges**: Instant synchronization of friend online/away/busy states with color-coded badges (🟢 Online, 🟡 Away, 🔴 Busy, ⚫ Offline).
- **Local Friend Alias Book (`/alias`)**: Assign and persist local nicknames for friends in `aliases.txt`.
- **Friend Profile & Info View (`/info <n>`)**: Inspect real names, aliases, status messages, connection types (TCP/UDP), and 76-character public keys.
- **Flexible Friend Addition (`/add`)**: Supports both **76-character full Tox IDs** and **64-character raw public keys**.

---

## Command Reference

| Command | Description |
| :--- | :--- |
| `/add <toxid> [msg]` | Send friend request (76-char Tox ID or 64-char public key) |
| `/accept <n>` | Accept pending friend request `n` |
| `/decline <n>` | Decline pending friend request `n` |
| `/chat <n>` | Open chat with friend `n` (or use `Tab` / `Shift+Tab`) |
| `/del <n>` | Delete friend `n` and their chat history |
| `/name <name>` | Change your display name |
| `/status ok\|afk\|dnb` | Set your presence status mode |
| `/status <message>` | Set your status message text |
| `/alias <n> [alias]` | Set or clear a local nickname for friend `n` |
| `/info <n>` | View detailed profile and connection info for friend `n` |
| `/id` | Display your Tox ID |
| `/help` | Display command help |
| `/quit` (or `Ctrl+C`) | Exit the application |

---

## Compilation & Usage

### Linux / Termux
```bash
sh Compile_Linux.sh
mono jTox.exe
```

### Windows
```cmd
Compile_Windows.bat
jTox.exe
```

---

## Project Structure

- `Program.cs` - Entry point and initialization.
- `ToxClient.cs` - Core Tox networking thread, event callbacks, profile persistence, and alias management.
- `Ui.cs` - Interactive terminal UI rendering, input handling, and command processor.
- `Native.cs` - P/Invoke wrappers for `libtoxcore`.
- `Nodes.cs` - Default DHT bootstrap nodes.
- `Hex.cs` - Hex encoding/decoding utilities.

## Contact me
Session;
```
05360e21fa52901681b01304f7fa9890b6f53f23e2867800f8403f475a4209b92f
```
Tox;
```
0429CEEE49268225DF310D9A9A47A0EF6B2154857E8D9F7DB820F6639D1CDA3EDCBDCF5D2D12
```
