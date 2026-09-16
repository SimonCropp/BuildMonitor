using ModelContextProtocol.Server;

/// <summary>
/// The MCP prompts. Thin in the way <see cref="BuildTools"/> is: the text is built by
/// <see cref="TriagePrompt"/>, which is what the tests exercise; this class only carries the
/// attributes the SDK reads.
/// <para>
/// A prompt rather than a tool because this is a command the user runs, not a call the assistant
/// chooses to make: a client lists it where the user can pick it, and Claude Code gives it a slash
/// command. Shipping it with the server is what puts it in reach of everyone who registers
/// BuildMonitor, rather than only whoever worked the recipe out.
/// </para>
/// </summary>
[McpServerPromptType]
sealed class BuildPrompts(MonitorTools tools)
{
    [McpServerPrompt(Name = "triage")]
    [Description("Work through the builds failing now whose repository is checked out locally: read their logs, group the ones that share a cause, and reproduce each from its checkout. Covers every configured connection.")]
    public async Task<string> Triage(
        [Description("Optional substring to filter on pipeline, repository, branch or connection name, as list_builds takes. Left empty, or given as *, every failing build is covered.")] string? filter = null,
        [Description("Pass true to have each failure fixed where it was reproduced and the tests run, with the changes left uncommitted. Anything else, or nothing, diagnoses and reports the failures and changes no source files.")] string? fix = null,
        Cancel cancel = default)
    {
        var scope = TriagePrompt.Filter(filter);
        var failing = await tools.ListFailing(scope, cancel);
        return TriagePrompt.Build(failing, TriagePrompt.Fixing(fix), scope);
    }
}
