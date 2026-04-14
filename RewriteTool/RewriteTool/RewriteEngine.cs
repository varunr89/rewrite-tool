using GitHub.Copilot.SDK;

namespace RewriteTool;

internal sealed class RewriteEngine : IAsyncDisposable
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RewriteTool");
    private static readonly string ModelConfigPath = Path.Combine(ConfigDir, "model.txt");

    private readonly CopilotClient _client;
    private List<string> _availableModels = [];
    private string? _selectedModel;

    public IReadOnlyList<string> AvailableModels => _availableModels;
    public string? SelectedModel => _selectedModel;

    public RewriteEngine()
    {
        _client = new CopilotClient();
    }

    public async Task InitializeAsync()
    {
        await _client.StartAsync();
        Log("Client started");

        // Discover available models with timeout
        try
        {
            var modelTask = DiscoverModelsAsync();
            if (await Task.WhenAny(modelTask, Task.Delay(10000)) == modelTask)
                await modelTask;
            else
                Log("Model discovery timed out");
        }
        catch (Exception ex)
        {
            Log("Model discovery failed: " + ex.Message);
        }

        // Load last used model from config, fall back to first available
        _selectedModel = LoadSavedModel();
        if (_selectedModel != null && !_availableModels.Contains(_selectedModel))
        {
            Log($"Saved model '{_selectedModel}' no longer available, clearing");
            _selectedModel = null;
        }
        _selectedModel ??= _availableModels.FirstOrDefault();

        Log($"Selected model: {_selectedModel ?? "(SDK default)"}");
        Log($"Available models: {string.Join(", ", _availableModels)}");
    }

    private async Task DiscoverModelsAsync()
    {
        var models = await _client.ListModelsAsync();
        if (models != null && models.Count > 0)
            _availableModels = models.Select(m => m.Id ?? m.Name ?? "?").ToList();
    }

    public void SetModel(string modelId)
    {
        _selectedModel = modelId;
        SaveModel(modelId);
        Log($"Model changed to: {modelId}");
    }

    private static string? LoadSavedModel()
    {
        try
        {
            if (File.Exists(ModelConfigPath))
                return File.ReadAllText(ModelConfigPath).Trim();
        }
        catch { }
        return null;
    }

    private static void SaveModel(string modelId)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ModelConfigPath, modelId);
        }
        catch { }
    }

    public async Task<string> RewriteAsync(RewriteMode mode, string inputText, string? customInstruction = null, CancellationToken ct = default)
    {
        string systemPrompt = Prompts.GetSystemPrompt(mode, customInstruction);

        var config = new SessionConfig
        {
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemPrompt,
            },
        };

        if (_selectedModel != null)
            config.Model = _selectedModel;

        await using var session = await _client.CreateSessionAsync(config);

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
            else if (evt is SessionErrorEvent err)
            {
                done.TrySetException(new Exception(err.Data?.Message ?? "Copilot SDK error"));
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = inputText });

        using var reg = ct.Register(() => done.TrySetCanceled());
        await done.Task;

        return result.ToString().Trim();
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(ConfigDir, "debug.log");
            Directory.CreateDirectory(ConfigDir);
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Engine] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
