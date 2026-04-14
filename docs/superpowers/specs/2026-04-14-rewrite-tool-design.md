---
summary: Design spec for RewriteTool — a lightweight Windows tray app for AI-assisted text rewriting using the GitHub Copilot SDK
read_when: implementing, reviewing, or extending RewriteTool
---

# RewriteTool Design Spec

## Overview

A lightweight Windows system tray application that rewrites selected text in any app using the GitHub Copilot SDK. The user selects text, presses a global hotkey, picks a rewrite mode, and the rewritten text replaces the selection. Ctrl+Z undoes it.

## Goals

- **Universal:** Works in any Windows app that supports Ctrl+C / Ctrl+V.
- **Lightweight:** Single `.exe`, no installer, no dependencies on modern Windows.
- **Simple:** Five rewrite presets + custom prompt. No settings UI, no config files.
- **Robust:** Non-destructive — clipboard is saved/restored, failures are silent (tray notification only), Ctrl+Z always reverts.
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
3. App saves current clipboard contents
4. App simulates Ctrl+C to capture selection
5. App reads clipboard (plain text)
6. If clipboard is empty → show tray notification "No text selected" → restore clipboard → stop
7. App shows ContextMenuStrip at cursor position:
   - Fix grammar
   - Make professional
   - Make concise
   - Expand
   - ───────────── (separator)
   - Custom...
8. User clicks an option (or presses Escape to cancel → restore clipboard → stop)
9. For "Custom...": show simple InputBox dialog for the instruction
10. App calls Copilot SDK with system prompt + selected text
11. On success: write rewritten text to clipboard → simulate Ctrl+V → done
12. On failure: show tray notification with error → restore clipboard → stop
```

### Clipboard Safety

The clipboard is treated as a shared resource that belongs to the user:

- **Before capture:** Save clipboard contents (text format at minimum).
- **On cancel/failure:** Restore the saved clipboard contents.
- **On success:** Clipboard contains the rewritten text (user can paste it again if needed). The original text is not restored because the paste already happened.

### Undo

Most Windows text controls support Ctrl+Z which will undo the paste operation, restoring the original text. This is inherent to the target app — RewriteTool doesn't need to implement undo.

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
  1. Calls `ClipboardHelper.CaptureSelection()` to save clipboard + simulate Ctrl+C.
  2. If no text captured, shows balloon notification and returns.
  3. Creates a `ContextMenuStrip` with the 5 preset items + "Custom...".
  4. Shows the menu at `Cursor.Position`.
  5. On menu item click, calls `RewriteEngine.RewriteAsync(mode, text)`.
  6. On result, calls `ClipboardHelper.PasteResult(result)`.
- Unregisters hotkey on dispose.

### ClipboardHelper.cs

- `CaptureSelection() → string?`
  - Saves current clipboard contents via `Clipboard.GetDataObject()`.
  - Clears clipboard.
  - Sends `Ctrl+C` via `SendKeys.SendWait("^c")` or `SendInput` P/Invoke.
  - Waits ~150ms for clipboard to populate.
  - Reads `Clipboard.GetText()`.
  - If empty, restores saved clipboard and returns null.
  - Returns the captured text.
- `PasteResult(string text)`
  - Sets clipboard to the rewritten text.
  - Sends `Ctrl+V` via `SendKeys.SendWait("^v")`.
- `RestoreClipboard()`
  - Restores previously saved clipboard data.

**Note:** `SendKeys` may not work reliably in all apps (especially elevated/UWP). If issues arise, fall back to `SendInput` Win32 API which is more reliable.

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
- Win32 P/Invoke: `RegisterHotKey`, `UnregisterHotKey`, `SendInput`
- No other dependencies

## Open Questions

None — design is intentionally minimal. Future enhancements (configurable hotkey, more presets, prompt history) can be added later if needed.
