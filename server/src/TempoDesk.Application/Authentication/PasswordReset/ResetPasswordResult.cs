namespace TempoDesk.Application.Authentication;

public sealed record ResetPasswordResult(
    bool Succeeded,
    bool TokenRejected,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ResetPasswordResult Success { get; } =
        new(true, false, new Dictionary<string, string[]>());

    public static ResetPasswordResult InvalidToken { get; } =
        new(false, true, new Dictionary<string, string[]>());
}
