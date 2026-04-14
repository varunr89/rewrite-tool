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
            {
                // Clipboard changed — wait a bit for all formats to be populated
                Thread.Sleep(150);
                break;
            }
            Thread.Sleep(20);
        }

        // Try multiple text formats
        string text = Clipboard.GetText(TextDataFormat.UnicodeText);
        if (string.IsNullOrEmpty(text))
            text = Clipboard.GetText(TextDataFormat.Text);
        if (string.IsNullOrEmpty(text))
            text = Clipboard.GetText();

        // Log available formats for diagnostics
        var data = Clipboard.GetDataObject();
        if (data != null)
        {
            var formats = data.GetFormats();
            Log("Clipboard formats: " + string.Join(", ", formats));
            Log("Text result: " + (text == null ? "null" : $"'{text.Substring(0, Math.Min(text.Length, 50))}'"));
        }

        if (string.IsNullOrEmpty(text))
        {
            RestoreClipboard();
            return null;
        }

        return text;
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RewriteTool", "debug.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Clipboard] {message}{Environment.NewLine}");
        }
        catch { }
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
            try
            {
                Clipboard.SetDataObject(_savedClipboard, true);
            }
            catch
            {
                // Clipboard may be locked by another app — silently fail
            }
            _savedClipboard = null;
        }
    }
}
