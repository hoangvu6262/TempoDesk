using System.Net;

namespace TempoDesk.Application.Authentication;

public sealed class SecurityAuditContext
{
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? TraceId { get; set; }
}
