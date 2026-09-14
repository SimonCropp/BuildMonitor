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
    public const string Theme = "theme";
    public const string PollInterval = "pollInterval";
    public const string RunningPollInterval = "runningPollInterval";
    public const string Port = "port";
    public const string AddConnection = "addConnection";
    public const string Version = "version";
    public const string Documentation = "documentation";
    public const string OpenLogs = "openLogs";
    public const string RaiseIssue = "raiseIssue";
    public const string Update = "update";
    public const string ConnectionPrefix = "connection:";

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
    public const string ClientId = "clientId";
    public const string CallbackPort = "callbackPort";
    public const string Notes = "notes";
    public const string Message = "message";

    // Sign in
    public const string SignInMessage = "signInMessage";
    public const string UserCode = "userCode";
    public const string VerificationUrl = "verificationUrl";

    public static string Connection(string id) => $"{ConnectionPrefix}{id}";
    public static string Filter(int index) => $"{FilterPrefix}{index}";
    public static string Scope(string id) => $"{ScopePrefix}{id}";
}
