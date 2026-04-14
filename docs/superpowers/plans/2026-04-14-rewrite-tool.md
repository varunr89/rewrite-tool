# RewriteTool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a lightweight Windows system tray app that rewrites selected text in any app using the GitHub Copilot SDK.

**Architecture:** Single-process C# WinForms tray app. Global hotkey captures selected text via SendInput Ctrl+C, shows a popup menu, sends text to Copilot SDK, pastes result back via SendInput Ctrl+V. All P/Invoke in one NativeMethods static class. Clipboard saved/restored for safety. Focus-loss detection prevents pasting into wrong app.

**Tech Stack:** .NET 8 / C# 12, WinForms, GitHub.Copilot.SDK NuGet, Win32 P/Invoke (SendInput, RegisterHotKey, GetForegroundWindow, GetAsyncKeyState, GetClipboardSequenceNumber)

**Parallelization:** Tasks 1-4 are independent and can be built simultaneously by separate agents. Task 5 integrates them. Task 6 is final wiring.

---

## File Structure

```
RewriteTool/
├── RewriteTool.csproj       # Project file, NuGet refs, publish config
├── Program.cs               # Entry point, single-instance mutex
├── NativeMethods.cs         # All Win32 P/Invoke declarations (static class)
├── ClipboardHelper.cs       # Clipboard save/restore, copy/paste via SendInput
├── Prompts.cs               # RewriteMode enum + static prompt templates
├── RewriteEngine.cs         # Copilot SDK wrapper
├── TrayApp.cs               # ApplicationContext: tray icon, hotkey, menu, orchestration
└── RewriteTool.sln          # Solution file
```

---

## Task 1: Project Scaffold + NativeMethods (PARALLEL)

**Files:**
- Create: `RewriteTool/RewriteTool.sln`
- Create: `RewriteTool/RewriteTool.csproj`
- Create: `RewriteTool/Program.cs`
- Create: `RewriteTool/NativeMethods.cs`

- [ ] **Step 1: Create solution and project**

```bash
cd ~/projects/rewrite-tool
dotnet new sln -n RewriteTool -o RewriteTool
dotnet new winforms -n RewriteTool -o RewriteTool/RewriteTool --use-program-main
dotnet sln RewriteTool/RewriteTool.sln add RewriteTool/RewriteTool/RewriteTool.csproj
```

- [ ] **Step 2: Configure csproj for single-file publish**

Edit `RewriteTool/RewriteTool/RewriteTool.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>rewrite.ico</ApplicationIcon>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="GitHub.Copilot.SDK" Version="*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write Program.cs with single-instance mutex**

```csharp
namespace RewriteTool;

static class Program
{
    private const string MutexName = "Global\\RewriteTool_SingleInstance";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
            return; // Another instance is running

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
```

- [ ] **Step 4: Write NativeMethods.cs with all P/Invoke declarations**

```csharp
using System.Runtime.InteropServices;

namespace RewriteTool;

internal static class NativeMethods
{
    // --- Hotkey ---
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    // --- Focus ---
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // --- Keyboard state ---
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;
    public const int VK_MENU = 0x12; // Alt

    // --- Clipboard ---
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    // --- SendInput ---
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const ushort VK_C = 0x43;
    public const ushort VK_V = 0x56;
    public const ushort VK_LCONTROL = 0xA2;

    /// <summary>Send a key chord (e.g. Ctrl+C). Presses modifier, presses key, releases key, releases modifier.</summary>
    public static void SendKeyChord(ushort modifier, ushort key)
    {
        var inputs = new INPUT[4];
        int size = Marshal.SizeOf<INPUT>();

        inputs[0].Type = INPUT_KEYBOARD;
        inputs[0].Union.Keyboard.VirtualKey = modifier;

        inputs[1].Type = INPUT_KEYBOARD;
        inputs[1].Union.Keyboard.VirtualKey = key;

        inputs[2].Type = INPUT_KEYBOARD;
        inputs[2].Union.Keyboard.VirtualKey = key;
        inputs[2].Union.Keyboard.Flags = KEYEVENTF_KEYUP;

        inputs[3].Type = INPUT_KEYBOARD;
        inputs[3].Union.Keyboard.VirtualKey = modifier;
        inputs[3].Union.Keyboard.Flags = KEYEVENTF_KEYUP;

        SendInput((uint)inputs.Length, inputs, size);
    }
}
```

- [ ] **Step 5: Verify it builds**

```bash
cd ~/projects/rewrite-tool/RewriteTool
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
committer "feat: project scaffold with Program.cs and NativeMethods P/Invoke" RewriteTool/
```

---

## Task 2: Prompts (PARALLEL)

**Files:**
- Create: `RewriteTool/RewriteTool/Prompts.cs`

- [ ] **Step 1: Write Prompts.cs**

```csharp
namespace RewriteTool;

public enum RewriteMode
{
    FixGrammar,
    MakeProfessional,
    MakeConcise,
    Expand,
    Custom
}

internal static class Prompts
{
    private const string Suffix = "\nReturn only the rewritten text. Do not include explanations, preamble, or formatting.";

    private static readonly Dictionary<RewriteMode, string> Templates = new()
    {
        [RewriteMode.FixGrammar] = "Fix all grammar, spelling, and punctuation errors in the following text. Preserve the original tone and meaning." + Suffix,
        [RewriteMode.MakeProfessional] = "Rewrite the following text in a professional, polished tone suitable for business communication." + Suffix,
        [RewriteMode.MakeConcise] = "Rewrite the following text to be shorter and more concise. Remove unnecessary words. Preserve the core meaning." + Suffix,
        [RewriteMode.Expand] = "Expand the following text with more detail and elaboration. Maintain the same tone and intent." + Suffix,
    };

    public static string GetSystemPrompt(RewriteMode mode, string? customInstruction = null)
    {
        if (mode == RewriteMode.Custom && customInstruction != null)
            return customInstruction + "\nApply the above instruction to the following text." + Suffix;

        return Templates[mode];
    }
}
```

- [ ] **Step 2: Verify it builds**

```bash
cd ~/projects/rewrite-tool/RewriteTool
dotnet build
```

- [ ] **Step 3: Commit**

```bash
committer "feat: add Prompts.cs with rewrite mode templates" RewriteTool/RewriteTool/Prompts.cs
```

---

## Task 3: ClipboardHelper (PARALLEL)

**Files:**
- Create: `RewriteTool/RewriteTool/ClipboardHelper.cs`

Depends on: NativeMethods.cs existing (from Task 1), but can be written in parallel if the agent has the NativeMethods signatures above.

- [ ] **Step 1: Write ClipboardHelper.cs**

```csharp
namespace RewriteTool;

internal static class ClipboardHelper
{
    private static IDataObject? _savedClipboard;

    /// <summary>
    /// Waits for Ctrl, Shift, Alt modifier keys to be released.
    /// Prevents hotkey modifiers from leaking into simulated keystrokes.
    /// </summary>
    public static void WaitForModifierRelease(int timeoutMs = 500)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool shiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
            bool altDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

            if (!ctrlDown && !shiftDown && !altDown)
                return;

            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Saves clipboard, simulates Ctrl+C, waits for clipboard to change, returns captured text.
    /// Returns null if no text was captured.
    /// </summary>
    public static string? CaptureSelection(int timeoutMs = 2000)
    {
        // Save current clipboard
        _savedClipboard = Clipboard.GetDataObject();

        // Record clipboard sequence number before copy
        uint seqBefore = NativeMethods.GetClipboardSequenceNumber();

        // Simulate Ctrl+C
        NativeMethods.SendKeyChord(NativeMethods.VK_LCONTROL, NativeMethods.VK_C);

        // Poll until clipboard sequence changes or timeout
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            uint seqNow = NativeMethods.GetClipboardSequenceNumber();
            if (seqNow != seqBefore)
                break;
            Thread.Sleep(20);
        }

        // Read text
        string text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text))
        {
            RestoreClipboard();
            return null;
        }

        return text;
    }

    /// <summary>
    /// Sets clipboard to the rewritten text and simulates Ctrl+V.
    /// </summary>
    public static void PasteResult(string text)
    {
        Clipboard.SetText(text);
        // Small delay to let clipboard settle
        Thread.Sleep(50);
        NativeMethods.SendKeyChord(NativeMethods.VK_LCONTROL, NativeMethods.VK_V);
    }

    /// <summary>
    /// Sets clipboard to text without pasting. Used when focus has changed.
    /// </summary>
    public static void SetClipboardOnly(string text)
    {
        Clipboard.SetText(text);
    }

    /// <summary>
    /// Restores previously saved clipboard contents.
    /// </summary>
    public static void RestoreClipboard()
    {
        if (_savedClipboard != null)
        {
            Clipboard.SetDataObject(_savedClipboard, true);
            _savedClipboard = null;
        }
    }
}
```

- [ ] **Step 2: Verify it builds**

```bash
cd ~/projects/rewrite-tool/RewriteTool
dotnet build
```

- [ ] **Step 3: Commit**

```bash
committer "feat: add ClipboardHelper with SendInput copy/paste and sequence polling" RewriteTool/RewriteTool/ClipboardHelper.cs
```

---

## Task 4: RewriteEngine (PARALLEL)

**Files:**
- Create: `RewriteTool/RewriteTool/RewriteEngine.cs`

- [ ] **Step 1: Write RewriteEngine.cs**

```csharp
using GitHub.CopilotSdk;

namespace RewriteTool;

internal sealed class RewriteEngine : IAsyncDisposable
{
    private readonly CopilotClient _client;

    public RewriteEngine()
    {
        _client = new CopilotClient();
    }

    public async Task InitializeAsync()
    {
        await _client.StartAsync();
    }

    /// <summary>
    /// Sends text to Copilot for rewriting. Returns the rewritten text.
    /// Throws on auth failure, network error, or timeout.
    /// </summary>
    public async Task<string> RewriteAsync(RewriteMode mode, string inputText, string? customInstruction = null, CancellationToken ct = default)
    {
        string systemPrompt = Prompts.GetSystemPrompt(mode, customInstruction);

        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = "gpt-4o",
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemPrompt,
            },
        });

        var result = new System.Text.StringBuilder();
        var done = new TaskCompletionSource();

        session.On(evt =>
        {
            if (evt is AssistantMessageEvent msg)
            {
                result.Append(msg.Data.Content);
            }
            else if (evt is SessionIdleEvent)
            {
                done.TrySetResult();
            }
            else if (evt is ErrorEvent err)
            {
                done.TrySetException(new Exception(err.Data?.Message ?? "Copilot SDK error"));
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = inputText });

        // Wait with cancellation support
        using var reg = ct.Register(() => done.TrySetCanceled());
        await done.Task;

        return result.ToString().Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
```

**Note:** The exact Copilot SDK namespace and event types may differ from the published package. After `dotnet restore`, check the actual API surface with `dotnet build` and adjust imports/types accordingly. The pattern (create client → create session → subscribe to events → send message → await idle) is confirmed from the SDK docs. The model can be any model available to the user's Copilot subscription — `gpt-4o` is a safe default.

- [ ] **Step 2: Verify it builds**

```bash
cd ~/projects/rewrite-tool/RewriteTool
dotnet restore && dotnet build
```

If the build fails due to incorrect SDK types, inspect the package:
```bash
dotnet new console -o /tmp/sdk-check
cd /tmp/sdk-check
dotnet add package GitHub.Copilot.SDK
# Check the actual public API:
find ~/.nuget/packages/github.copilot.sdk -name "*.xml" | head -1 | xargs cat
```

Adjust `RewriteEngine.cs` imports and types to match the actual SDK API.

- [ ] **Step 3: Commit**

```bash
committer "feat: add RewriteEngine wrapping Copilot SDK" RewriteTool/RewriteTool/RewriteEngine.cs
```

---

## Task 5: TrayApp — Orchestration (SEQUENTIAL — depends on Tasks 1-4)

**Files:**
- Create: `RewriteTool/RewriteTool/TrayApp.cs`
- Modify: `RewriteTool/RewriteTool/Program.cs` (remove generated Form1 if present)

- [ ] **Step 1: Delete any generated Form1 files**

```bash
rm -f RewriteTool/RewriteTool/Form1.cs RewriteTool/RewriteTool/Form1.Designer.cs
```

- [ ] **Step 2: Write TrayApp.cs**

```csharp
using Microsoft.Win32;

namespace RewriteTool;

internal sealed class TrayApp : ApplicationContext
{
    private const int HotkeyId = 1;
    private const uint VK_R = 0x52;
    private const string AppName = "RewriteTool";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly NotifyIcon _trayIcon;
    private readonly RewriteEngine _engine;
    private bool _busy;

    public TrayApp()
    {
        _engine = new RewriteEngine();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // TODO: replace with custom icon
            Text = AppName,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };

        // Initialize Copilot SDK in background
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _engine.InitializeAsync();
        }
        catch (Exception ex)
        {
            ShowBalloon("Copilot SDK init failed: " + ex.Message);
        }
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var startWithWindows = new ToolStripMenuItem("Start with Windows");
        startWithWindows.Checked = IsStartupEnabled();
        startWithWindows.Click += (_, _) =>
        {
            ToggleStartup();
            startWithWindows.Checked = IsStartupEnabled();
        };
        menu.Items.Add(startWithWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    protected override void OnMainFormClosed(EventArgs e)
    {
        Cleanup();
        base.OnMainFormClosed(e);
    }

    // --- Hotkey registration via hidden message window ---

    private HotkeyWindow? _hotkeyWindow;

    public void RegisterHotkey()
    {
        _hotkeyWindow = new HotkeyWindow(this);
        bool ok = NativeMethods.RegisterHotKey(
            _hotkeyWindow.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            VK_R);

        if (!ok)
            ShowBalloon("Hotkey Ctrl+Shift+R is in use by another app.");
    }

    internal void OnHotkeyPressed()
    {
        _ = HandleHotkeyAsync();
    }

    private async Task HandleHotkeyAsync()
    {
        if (_busy)
        {
            ShowBalloon("Rewrite already in progress.");
            return;
        }

        _busy = true;
        try
        {
            // Wait for modifier keys to release
            ClipboardHelper.WaitForModifierRelease();

            // Capture foreground window
            IntPtr targetHwnd = NativeMethods.GetForegroundWindow();

            // Copy selected text
            string? text = ClipboardHelper.CaptureSelection();
            if (text == null)
            {
                ShowBalloon("No text selected.");
                return;
            }

            // Show rewrite mode menu
            var (mode, customInstruction) = await ShowRewriteMenuAsync();
            if (mode == null)
            {
                ClipboardHelper.RestoreClipboard();
                return; // User cancelled
            }

            // Call Copilot SDK
            string result;
            try
            {
                result = await _engine.RewriteAsync(mode.Value, text, customInstruction);
            }
            catch (Exception ex)
            {
                ShowBalloon("Rewrite failed: " + ex.Message);
                ClipboardHelper.RestoreClipboard();
                return;
            }

            // Paste result — but only if original window is still foreground
            IntPtr currentFg = NativeMethods.GetForegroundWindow();
            if (currentFg == targetHwnd)
            {
                ClipboardHelper.PasteResult(result);
            }
            else
            {
                ClipboardHelper.SetClipboardOnly(result);
                ShowBalloon("Rewrite ready — paste with Ctrl+V.");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private Task<(RewriteMode? mode, string? customInstruction)> ShowRewriteMenuAsync()
    {
        var tcs = new TaskCompletionSource<(RewriteMode?, string?)>();
        var menu = new ContextMenuStrip();

        void AddItem(string label, RewriteMode mode)
        {
            menu.Items.Add(label, null, (_, _) => tcs.TrySetResult((mode, null)));
        }

        AddItem("Fix grammar", RewriteMode.FixGrammar);
        AddItem("Make professional", RewriteMode.MakeProfessional);
        AddItem("Make concise", RewriteMode.MakeConcise);
        AddItem("Expand", RewriteMode.Expand);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Custom...", null, (_, _) =>
        {
            string? instruction = ShowCustomPromptDialog();
            if (instruction != null)
                tcs.TrySetResult((RewriteMode.Custom, instruction));
            else
                tcs.TrySetResult((null, null)); // Cancelled
        });

        menu.Closed += (_, _) =>
        {
            // If menu was closed without selection (Escape, click away)
            tcs.TrySetResult((null, null));
        };

        menu.Show(Cursor.Position);
        return tcs.Task;
    }

    private static string? ShowCustomPromptDialog()
    {
        using var form = new Form
        {
            Text = "Custom Rewrite",
            Width = 420,
            Height = 150,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
        };

        var textBox = new TextBox { Left = 12, Top = 12, Width = 380 };
        var okBtn = new Button { Text = "OK", Left = 230, Top = 50, Width = 75, DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "Cancel", Left = 315, Top = 50, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange([textBox, okBtn, cancelBtn]);
        form.AcceptButton = okBtn;
        form.CancelButton = cancelBtn;

        return form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text)
            ? textBox.Text.Trim()
            : null;
    }

    private void ShowBalloon(string message)
    {
        _trayIcon.BalloonTipTitle = AppName;
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(3000);
    }

    // --- Startup toggle ---

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) != null;
    }

    private static void ToggleStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)!;
        if (key.GetValue(AppName) != null)
            key.DeleteValue(AppName);
        else
            key.SetValue(AppName, Application.ExecutablePath);
    }

    private void Cleanup()
    {
        if (_hotkeyWindow != null)
            NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _ = _engine.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cleanup();
        base.Dispose(disposing);
    }

    /// <summary>Hidden window to receive WM_HOTKEY messages.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly TrayApp _owner;

        public HotkeyWindow(TrayApp owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                _owner.OnHotkeyPressed();
                return;
            }
            base.WndProc(ref m);
        }
    }
}
```

- [ ] **Step 3: Update Program.cs to register hotkey after app starts**

```csharp
namespace RewriteTool;

static class Program
{
    private const string MutexName = "Global\\RewriteTool_SingleInstance";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        var app = new TrayApp();
        app.RegisterHotkey();
        Application.Run(app);
    }
}
```

- [ ] **Step 4: Delete any remaining generated files (Form1, etc.)**

```bash
rm -f RewriteTool/RewriteTool/Form1.cs RewriteTool/RewriteTool/Form1.Designer.cs
```

- [ ] **Step 5: Verify it builds**

```bash
cd ~/projects/rewrite-tool/RewriteTool
dotnet build
```

Fix any build errors. Common issues:
- Missing `using` statements → add them.
- SDK namespace mismatch → check actual package namespace after restore.
- `ApplicationConfiguration.Initialize()` not found → ensure `<UseWindowsForms>true</UseWindowsForms>` in csproj.

- [ ] **Step 6: Commit**

```bash
committer "feat: add TrayApp with hotkey, rewrite menu, and focus-safe paste" RewriteTool/RewriteTool/TrayApp.cs RewriteTool/RewriteTool/Program.cs
```

---

## Task 6: Build, Test, Ship (SEQUENTIAL — depends on Task 5)

**Files:**
- Modify: `RewriteTool/RewriteTool/RewriteTool.csproj` (if needed)

- [ ] **Step 1: Restore packages and build release**

```bash
cd ~/projects/rewrite-tool/RewriteTool
dotnet restore
dotnet build -c Release
```

- [ ] **Step 2: Publish self-contained single file**

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

Expected: `publish/RewriteTool.exe` exists, ~15-20 MB.

```bash
ls -lh publish/RewriteTool.exe
```

- [ ] **Step 3: Smoke test on Windows**

Copy the exe to a Windows-accessible path and run it:

```bash
cp publish/RewriteTool.exe /mnt/c/Temp/RewriteTool.exe
echo "RewriteTool.exe copied to C:\\Temp\\RewriteTool.exe"
echo "Manual test steps:"
echo "  1. Double-click RewriteTool.exe on Windows"
echo "  2. Verify tray icon appears"
echo "  3. Open Notepad, type some text, select it"
echo "  4. Press Ctrl+Shift+R"
echo "  5. Verify popup menu appears with 5 options"
echo "  6. Click 'Fix grammar'"
echo "  7. Verify text is replaced"
echo "  8. Press Ctrl+Z to verify undo works"
echo "  9. Right-click tray icon → Exit"
```

- [ ] **Step 4: Commit final state**

```bash
cd ~/projects/rewrite-tool
committer "build: add publish output and smoke test instructions" RewriteTool/
```

---

## Parallelization Summary

```
    ┌──────────┐  ┌──────────┐  ┌──────────────┐  ┌──────────────┐
    │ Task 1   │  │ Task 2   │  │ Task 3       │  │ Task 4       │
    │ Scaffold │  │ Prompts  │  │ Clipboard    │  │ RewriteEngine│
    │ +Native  │  │          │  │ Helper       │  │              │
    └────┬─────┘  └────┬─────┘  └──────┬───────┘  └──────┬───────┘
         │             │               │                  │
         └─────────────┴───────────────┴──────────────────┘
                                │
                       ┌────────┴────────┐
                       │ Task 5          │
                       │ TrayApp         │
                       │ (integration)   │
                       └────────┬────────┘
                                │
                       ┌────────┴────────┐
                       │ Task 6          │
                       │ Build + Ship    │
                       └─────────────────┘
```

Tasks 1-4 are fully independent and can be dispatched to 4 parallel agents. Task 5 integrates them. Task 6 is final build/test.
