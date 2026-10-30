namespace TempoDesk.Application.Authentication;

public sealed record RegistrationResult(
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static RegistrationResult Accepted { get; } =
        new(new Dictionary<string, string[]>());
}
