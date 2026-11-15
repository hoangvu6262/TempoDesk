namespace TempoDesk.Application.Workspaces;

public static class WorkspaceName
{
    public const int MaxLength = 200;
    public const string RequiredMessage = "Workspace name is required.";
    public const string TooLongMessage =
        "Workspace name must not exceed 200 characters.";

    public static string? Normalize(string? name, out string? error)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            error = RequiredMessage;
            return null;
        }

        if (normalized.Length > MaxLength)
        {
            error = TooLongMessage;
            return null;
        }

        error = null;
        return normalized;
    }
}
