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
            Icon = LoadCopilotIcon(),
            Text = AppName,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            Log("Initializing Copilot SDK...");
            await _engine.InitializeAsync();
            Log("Copilot SDK initialized OK");
            RebuildTrayMenu();
        }
        catch (Exception ex)
        {
            Log("Copilot SDK init failed: " + ex);
            ShowBalloon("Copilot SDK init failed: " + ex.Message);
        }
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        // Model submenu — populated after SDK init
        var modelMenu = new ToolStripMenuItem("Model");
        if (_engine.AvailableModels.Count > 0)
        {
            foreach (var model in _engine.AvailableModels)
            {
                var item = new ToolStripMenuItem(model);
                item.Checked = model == _engine.SelectedModel;
                item.Click += (_, _) =>
                {
                    _engine.SetModel(model);
                    RebuildTrayMenu();
                    ShowBalloon($"Model: {model}");
                };
                modelMenu.DropDownItems.Add(item);
            }
        }
        else
        {
            modelMenu.DropDownItems.Add(new ToolStripMenuItem("(loading...)") { Enabled = false });
        }
        menu.Items.Add(modelMenu);

        menu.Items.Add(new ToolStripSeparator());

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

    private void RebuildTrayMenu()
    {
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
    }

    private HotkeyWindow? _hotkeyWindow;

    public void RegisterHotkey()
    {
        _hotkeyWindow = new HotkeyWindow(this);
        bool ok = NativeMethods.RegisterHotKey(
            _hotkeyWindow.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            VK_R);

        Log("RegisterHotKey result: " + ok);
        if (!ok)
            ShowBalloon("Hotkey Ctrl+Shift+R is in use by another app.");
    }

    internal void OnHotkeyPressed()
    {
        _ = HandleHotkeyAsync().ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                var msg = t.Exception.InnerException?.Message ?? t.Exception.Message;
                Log("Unhandled error: " + msg);
                ShowBalloon("Error: " + msg);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task HandleHotkeyAsync()
    {
        Log("Hotkey pressed");
        if (_busy)
        {
            ShowBalloon("Rewrite already in progress.");
            return;
        }

        _busy = true;
        try
        {
            ClipboardHelper.WaitForModifierRelease();
            Log("Modifiers released");

            IntPtr targetHwnd = NativeMethods.GetForegroundWindow();
            Log("Target HWND: " + targetHwnd);

            string? text = ClipboardHelper.CaptureSelection();
            Log("Captured text: " + (text == null ? "null" : $"{text.Length} chars"));
            if (text == null)
            {
                ShowBalloon("No text selected.");
                return;
            }

            var (mode, customInstruction) = await ShowRewriteMenuAsync();
            Log("Menu result: " + (mode?.ToString() ?? "cancelled"));
            if (mode == null)
            {
                ClipboardHelper.RestoreClipboard();
                return;
            }

            string result;
            try
            {
                Log("Calling Copilot SDK...");
                NativeMethods.SetBusyCursor();
                result = await _engine.RewriteAsync(mode.Value, text, customInstruction);
                Log("Rewrite result: " + result.Length + " chars");
            }
            catch (Exception ex)
            {
                ShowBalloon("Rewrite failed: " + ex.Message);
                ClipboardHelper.RestoreClipboard();
                return;
            }
            finally
            {
                NativeMethods.RestoreCursor();
            }

            // Re-focus original window and paste
            NativeMethods.SetForegroundWindow(targetHwnd);
            Thread.Sleep(200);
            ClipboardHelper.PasteResult(result);
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
                tcs.TrySetResult((null, null));
        });

        menu.Closed += (_, e) =>
        {
            // Only treat as cancel if user clicked away, not if an item was selected
            // Small delay to let item click handlers fire first
            Task.Delay(100).ContinueWith(_ => tcs.TrySetResult((null, null)));
        };

        // Show via NotifyIcon's internal ShowContextMenu which handles focus properly
        _trayIcon.ContextMenuStrip = menu;
        var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        mi?.Invoke(_trayIcon, null);

        // Restore tray menu after selection
        tcs.Task.ContinueWith(_ => RebuildTrayMenu());

        return tcs.Task;
    }

    private static bool InvokeRequired() => false; // Simplification — we're always on the UI thread

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

    private static Icon LoadCopilotIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var stream = asm.GetManifestResourceStream("RewriteTool.copilot.ico");
            if (stream != null)
                return new Icon(stream, 32, 32);
        }
        catch { }
        return SystemIcons.Application;
    }

    private void ShowBalloon(string message)
    {
        Log("Balloon: " + message);
        _trayIcon.BalloonTipTitle = AppName;
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(3000);
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName, "debug.log");

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

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
