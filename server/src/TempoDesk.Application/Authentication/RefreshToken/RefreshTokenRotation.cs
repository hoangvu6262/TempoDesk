namespace TempoDesk.Application.Authentication;

public sealed record RefreshTokenRotation(
    RefreshTokenRotation.Status Outcome,
    Guid? UserId)
{
    public enum Status
    {
        Succeeded,
        Missing,
        Invalid,
        Reused
    }
}
