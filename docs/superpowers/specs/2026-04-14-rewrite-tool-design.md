---
summary: Design spec for RewriteTool — a lightweight Windows tray app for AI-assisted text rewriting using the GitHub Copilot SDK
read_when: implementing, reviewing, or extending RewriteTool
---

# RewriteTool Design Spec

## Overview

A lightweight Windows system tray application that rewrites selected text in any app using the GitHub Copilot SDK. The user selects text, presses a global hotkey, picks a rewrite mode, and the rewritten text replaces the selection. Ctrl+Z undoes it.

## Goals

- **Universal (best-effort):** Works in standard Windows desktop apps with editable text controls. Not guaranteed in elevated/admin apps, custom-rendered editors, or terminal emulators.
- **Lightweight:** Single `.exe`, no installer, no dependencies on modern Windows.
- **Simple:** Five rewrite presets + custom prompt. No settings UI, no config files.
- **Robust:** Non-destructive — clipboard is saved/restored best-effort, failures surface as tray notifications. Paste is often undoable with Ctrl+Z depending on the target app.
- **Shareable:** Zip and send. Or GitHub Release.

## Non-Goals

- Shell context menu integration (requires registry/admin).
- Rich UI, settings panels, prompt history.
- Streaming output or progress indicators.
- Multi-platform support (Windows only).

## Architecture

Single-process C# WinForms application. No visible windows. Tray icon + popup menus only.

### Core Flow

```
1. User selects text in any application
2. User presses Ctrl+Shift+R (global hotkey)
3. App checks reentrancy guard — if rewrite already in progress, show "Rewrite in progress" toast → stop
4. App saves current clipboard contents (text format)
5. App waits for modifier keys (Ctrl, Shift) to be released (poll GetAsyncKeyState)
6. App records foreground HWND via GetForegroundWindow()
7. App simulates Ctrl+C via SendInput
8. App polls GetClipboardSequenceNumber() until it changes or timeout (~2s)
9. App reads clipboard (plain text)
10. If clipboard is empty → show tray notification "No text selected" → restore clipboard → stop
11. App shows ContextMenuStrip at Cursor.Position:
    - Fix grammar
    - Make professional
    - Make concise
    - Expand
    - ───────────── (separator)
    - Custom...
12. User clicks an option (or presses Escape to cancel → restore clipboard → stop)
13. For "Custom...": show simple InputBox dialog for the instruction
14. App calls Copilot SDK with system prompt + captured text (async await)
15. On success:
    a. Check if original HWND is still foreground (GetForegroundWindow() == savedHwnd)
    b. If YES: write rewritten text to clipboard → simulate Ctrl+V via SendInput → done
    c. If NO: write rewritten text to clipboard → show toast "Rewrite ready — paste with Ctrl+V" → done
16. On failure: show tray notification with error → restore clipboard → stop
```

### Clipboard Safety

The clipboard is treated as a shared resource that belongs to the user:

- **Before capture:** Save clipboard contents (text format at minimum).
- **On cancel/failure:** Restore the saved clipboard contents.
- **On success:** Clipboard contains the rewritten text (user can paste it again if needed). The original text is not restored because the paste already happened.

### Undo

Most Windows text controls support Ctrl+Z which will undo the paste operation. This is inherent to the target app — RewriteTool doesn't need to implement undo. Not all apps guarantee undo (e.g., web forms, custom controls).

### Reentrancy Guard

A boolean or `SemaphoreSlim(1,1)` prevents overlapping rewrite operations. If the hotkey is pressed while a rewrite is in progress, a tray notification says "Rewrite already in progress" and the new request is ignored.

### Focus Safety

The foreground window HWND is captured early (step 6) and verified before paste (step 15). If the user alt-tabs during the async SDK call, the tool skips auto-paste and leaves the result on the clipboard with a toast notification. This prevents pasting into the wrong application.

## Components

```
RewriteTool/
├── Program.cs              # Entry point, single-instance mutex
├── TrayApp.cs              # System tray icon, global hotkey, menu popup
├── RewriteEngine.cs        # Copilot SDK wrapper, prompt dispatch
├── ClipboardHelper.cs      # Clipboard save/restore, Ctrl+C capture, Ctrl+V paste
├── Prompts.cs              # Static prompt templates for each rewrite mode
└── RewriteTool.csproj      # .NET 8, single-file publish
```

### Program.cs

- Creates a named `Mutex` to enforce single instance.
- If another instance is running, exit silently.
- Runs `Application.Run(new TrayApp())`.

### TrayApp.cs

- Inherits `ApplicationContext`.
- Creates a `NotifyIcon` with a basic context menu (right-click tray icon: "About", "Start with Windows", "Exit").
- Registers global hotkey `Ctrl+Shift+R` via Win32 `RegisterHotKey` P/Invoke.
- On hotkey trigger:
  1. Checks reentrancy guard — if busy, shows "Rewrite in progress" toast and returns.
  2. Sets reentrancy guard.
  3. Calls `ClipboardHelper.WaitForModifierRelease()`.
  4. Records foreground HWND via `GetForegroundWindow()` P/Invoke.
  5. Calls `ClipboardHelper.CaptureSelection()` to save clipboard + simulate Ctrl+C.
  6. If no text captured, shows balloon notification and returns.
  7. Creates a `ContextMenuStrip` with the 5 preset items + "Custom...".
  8. Shows the menu at `Cursor.Position`.
  9. On menu item click, calls `RewriteEngine.RewriteAsync(mode, text)`.
  10. On result, checks if saved HWND is still foreground:
      - If yes: calls `ClipboardHelper.PasteResult(result)`.
      - If no: sets clipboard to result, shows toast "Rewrite ready — paste with Ctrl+V".
  11. Clears reentrancy guard.
- Unregisters hotkey on dispose.

### ClipboardHelper.cs

- `CaptureSelection() → string?`
  - Saves current clipboard contents via `Clipboard.GetDataObject()`.
  - Records `GetClipboardSequenceNumber()`.
  - Sends `Ctrl+C` via `SendInput` P/Invoke (key down/up for Ctrl, then C).
  - Polls `GetClipboardSequenceNumber()` until it changes, with retry up to ~2s.
  - Reads `Clipboard.GetText()`.
  - If empty, restores saved clipboard and returns null.
  - Returns the captured text.
- `PasteResult(string text)`
  - Sets clipboard to the rewritten text.
  - Sends `Ctrl+V` via `SendInput` P/Invoke.
- `RestoreClipboard()`
  - Restores previously saved clipboard data.
- `WaitForModifierRelease()`
  - Polls `GetAsyncKeyState` for Ctrl, Shift, Alt keys.
  - Waits until all modifier keys are released (max ~500ms).
  - Prevents hotkey modifiers from leaking into the simulated Ctrl+C.

**Input injection:** Uses `SendInput` exclusively (not `SendKeys`). `SendInput` is lower-level, gives explicit key down/up control, and is more reliable across app types. Note: `SendInput` still cannot inject into elevated processes (UIPI boundary).

### RewriteEngine.cs

- Constructor initializes the Copilot SDK client.
- `RewriteAsync(RewriteMode mode, string inputText) → Task<string>`
  - Builds prompt from `Prompts.GetSystemPrompt(mode)` + input text.
  - Calls Copilot SDK to get completion.
  - Returns the rewritten text (trimmed).
  - Throws on auth failure, timeout, or SDK error (caller handles).

### Prompts.cs

Static class with prompt templates:

```csharp
public enum RewriteMode
{
    FixGrammar,
    MakeProfessional,
    MakeConcise,
    Expand,
    Custom
}
```

System prompts (all end with "Return only the rewritten text. Do not include explanations, preamble, or formatting."):

- **FixGrammar:** "Fix all grammar, spelling, and punctuation errors in the following text. Preserve the original tone and meaning."
- **MakeProfessional:** "Rewrite the following text in a professional, polished tone suitable for business communication."
- **MakeConcise:** "Rewrite the following text to be shorter and more concise. Remove unnecessary words. Preserve the core meaning."
- **Expand:** "Expand the following text with more detail and elaboration. Maintain the same tone and intent."
- **Custom:** User-provided instruction prepended to: "Apply the above instruction to the following text."

## Global Hotkey

- Uses Win32 `RegisterHotKey` API (not keyboard hooks).
- Default: `Ctrl+Shift+R`.
- If registration fails (another app has it), show a tray notification on startup suggesting the user close the conflicting app. No hotkey customization UI — change the constant and recompile.

## Authentication

The GitHub Copilot SDK handles auth automatically:

1. Checks for `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, or `GITHUB_TOKEN` env vars.
2. Falls back to `gh` CLI credentials.
3. If no auth found, SDK throws → tray notification: "Sign in to GitHub: run `gh auth login` in a terminal."

No auth UI in the app. The SDK owns this.

## Error Handling

All errors surface as tray balloon notifications. The app never shows modal dialogs for errors.

| Scenario | Behavior |
|---|---|
| No text selected | Balloon: "No text selected" |
| Copilot SDK not authenticated | Balloon: "GitHub auth required. Run: gh auth login" |
| SDK call fails (network, timeout) | Balloon: "Rewrite failed: {error}" |
| Hotkey already registered | Balloon on startup: "Hotkey Ctrl+Shift+R is in use by another app" |
| User cancels menu (Escape/click away) | Restore clipboard silently |
| Rewrite already in progress | Balloon: "Rewrite already in progress" |
| User switched apps during rewrite | Clipboard set to result, balloon: "Rewrite ready — paste with Ctrl+V" |

## Distribution

- **Build command:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
- **Output:** Single `RewriteTool.exe` (~15-20 MB self-contained).
- **Framework-dependent alternative:** ~1 MB but requires .NET 8 runtime installed.
- **Install:** Drop `.exe` anywhere and run.
- **Auto-start:** Tray menu toggle writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key (no admin needed).

## Tech Stack

- .NET 8 / C# 12
- WinForms (for `NotifyIcon`, `ContextMenuStrip`, message loop)
- GitHub.Copilot.SDK NuGet package
- Win32 P/Invoke: `RegisterHotKey`, `UnregisterHotKey`, `SendInput`, `GetForegroundWindow`, `GetAsyncKeyState`, `GetClipboardSequenceNumber`
- No other dependencies

## Open Questions

None — design is intentionally minimal. Future enhancements (configurable hotkey, more presets, prompt history) can be added later if needed.
