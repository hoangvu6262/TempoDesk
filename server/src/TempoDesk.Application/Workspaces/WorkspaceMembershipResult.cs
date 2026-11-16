namespace TempoDesk.Application.Workspaces;

public enum WorkspaceMembershipResultStatus
{
    Success,
    InvalidRequest,
    InvalidInvitation,
    NotFound,
    Forbidden,
    Conflict
}

public sealed record WorkspaceMembershipResult(
    WorkspaceMembershipResultStatus Status,
    WorkspaceMemberSummary? Member = null,
    WorkspaceInvitationSummary? Invitation = null,
    IReadOnlyList<WorkspaceMemberSummary>? Members = null,
    IReadOnlyList<WorkspaceInvitationSummary>? Invitations = null,
    string? ValidationField = null,
    string? ValidationError = null)
{
    public static WorkspaceMembershipResult Succeeded { get; } =
        new(WorkspaceMembershipResultStatus.Success);

    public static WorkspaceMembershipResult WithMember(
        WorkspaceMemberSummary member) =>
        new(WorkspaceMembershipResultStatus.Success, Member: member);

    public static WorkspaceMembershipResult WithInvitation(
        WorkspaceInvitationSummary invitation) =>
        new(WorkspaceMembershipResultStatus.Success, Invitation: invitation);

    public static WorkspaceMembershipResult WithMembers(
        IReadOnlyList<WorkspaceMemberSummary> members) =>
        new(WorkspaceMembershipResultStatus.Success, Members: members);

    public static WorkspaceMembershipResult WithInvitations(
        IReadOnlyList<WorkspaceInvitationSummary> invitations) =>
        new(WorkspaceMembershipResultStatus.Success,
            Invitations: invitations);

    public static WorkspaceMembershipResult Invalid(
        string field,
        string error) => new(
            WorkspaceMembershipResultStatus.InvalidRequest,
            ValidationField: field,
            ValidationError: error);

    public static WorkspaceMembershipResult InvalidToken { get; } =
        new(WorkspaceMembershipResultStatus.InvalidInvitation);

    public static WorkspaceMembershipResult Missing { get; } =
        new(WorkspaceMembershipResultStatus.NotFound);

    public static WorkspaceMembershipResult Denied { get; } =
        new(WorkspaceMembershipResultStatus.Forbidden);

    public static WorkspaceMembershipResult Conflicted { get; } =
        new(WorkspaceMembershipResultStatus.Conflict);
}
