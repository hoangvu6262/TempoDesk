namespace TempoDesk.Infrastructure.Identity;

public sealed class SecurityAuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = "";
    public bool Succeeded { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
