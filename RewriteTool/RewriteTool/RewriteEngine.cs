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
