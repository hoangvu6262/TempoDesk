namespace TempoDesk.Application.Authentication;

public sealed record EmailContent(
    string Subject,
    string TextBody,
    string HtmlBody);
