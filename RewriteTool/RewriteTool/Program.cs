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
        // Application.Run(new TrayApp()); // Added in Task 5
    }
}
