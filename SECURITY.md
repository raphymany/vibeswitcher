# Security Policy

## What the app touches

VibeSwitcher is a Windows-only desktop utility. Understanding what it accesses helps assess its security surface:

- **Windows audio COM APIs** — sets the system default audio device via `IPolicyConfig`. No audio content is read or recorded.
- **Windows Registry (`HKCU\...\Run`)** — written only when "Start with Windows" is enabled, to add or remove the startup entry.
- **`%APPDATA%\VibeSwitcher\`** — stores `config.json` (profile names, device endpoint IDs, hotkey definitions), `error.log`, and per-profile custom icons (`Icons\`) and switch sounds (`Sounds\`). No passwords or personal data are written.
- **Global hotkeys** — registers key combinations via `RegisterHotKey` (WinAPI). These are user-configured and can be cleared or disabled at any time.
- **`.ico` files** — loaded from paths the user explicitly selects via a file picker or the built-in gallery. Paths outside `%APPDATA%\VibeSwitcher\Icons\` are rejected.
- **Custom switch sounds (`.wav`)** — a user-picked sound is copied into `%APPDATA%\VibeSwitcher\Sounds\`; when a config is imported, any sound path outside that folder is dropped, so an imported file can't point the app at an arbitrary location on disk.

The app does not make network requests, does not run with elevated privileges, and does not access any other user data.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report privately by emailing **raphaelmansour0@gmail.com** with:
- A description of the vulnerability and its potential impact
- Steps to reproduce
- Any proof-of-concept code if applicable

You will receive a response within 7 days. If the issue is confirmed, a fix will be prioritised and credited to you in the release notes (unless you prefer to remain anonymous).

## Supported versions

Only the latest release is actively maintained. Older versions do not receive security patches.
