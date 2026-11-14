using System.Net;

namespace TempoDesk.Infrastructure.Identity;

public sealed class SecurityAuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = "";
    public bool Succeeded { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? TraceId { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}
