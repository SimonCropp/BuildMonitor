/// <summary>
/// The ids of every form field, shared by the transitions that fill them, the applier that
/// reads them and the heads that key controls by them.
/// </summary>
static class FormFields
{
    // Options
    public const string RunAtStartup = "runAtStartup";
    public const string ShowWindowAtStart = "showWindowAtStart";
    public const string ShowOtherBranches = "showOtherBranches";
    public const string ShowForks = "showForks";
    public const string NotifyOnFailure = "notifyOnFailure";
    public const string GroupPrefixes = "groupPrefixes";
    public const string Theme = "theme";
    public const string PollInterval = "pollInterval";
    public const string RunningPollInterval = "runningPollInterval";
    public const string HistoryDays = "historyDays";
    public const string Port = "port";
    public const string CodeDirectory = "codeDirectory";
    public const string AddConnection = "addConnection";
    public const string Version = "version";
    public const string Documentation = "documentation";
    public const string OpenLogs = "openLogs";
    public const string RaiseIssue = "raiseIssue";
    public const string Update = "update";
    public const string ConnectionPrefix = "connection:";

    // Update. Labels rather than widgets, so every server shares one id: nothing is keyed by it
    // because nothing on this page can be clicked or typed in.
    public const string UpdateSummary = "updateSummary";
    public const string UpdateServers = "updateServers";
    public const string McpServer = "mcpServer";

    // Remove connection. Labels too: its only choices are the footer's.
    public const string RemovedConnection = "removedConnection";
    public const string RemoveSummary = "removeSummary";
    public const string RemoveReturn = "removeReturn";

    // Filters
    public const string FilterKind = "filterKind";
    public const string FilterTarget = "filterTarget";
    public const string FilterText = "filterText";
    public const string AddFilter = "addFilter";
    public const string FilterPrefix = "filter:";
    public const string NoFilters = "noFilters";

    // Connection
    public const string Provider = "provider";
    public const string Name = "name";
    public const string Server = "server";
    public const string ScopePrefix = "scope:";
    public const string Auth = "auth";
    public const string User = "user";
    public const string Token = "token";
    public const string TokenHelp = "tokenHelp";
    public const string ProviderDocs = "providerDocs";
    public const string ClientId = "clientId";
    public const string CallbackPort = "callbackPort";
    public const string TokenNote = "tokenNote";
    public const string SignInNote = "signInNote";
    public const string Message = "message";

    // Sign in
    public const string SignInMessage = "signInMessage";
    public const string UserCode = "userCode";
    public const string VerificationUrl = "verificationUrl";

    public static string Connection(string id) => $"{ConnectionPrefix}{id}";
    public static string Filter(int index) => $"{FilterPrefix}{index}";
    public static string Scope(string id) => $"{ScopePrefix}{id}";
}
