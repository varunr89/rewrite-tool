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
