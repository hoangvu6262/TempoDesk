# TempoDesk Server

The backend is a .NET 10 modular monolith. Projects are technical boundaries; folders inside each project are grouped by business module or use case.

## Dependency direction

```text
TempoDesk.Api ----------------------> TempoDesk.Application
       |                                      |
       +--------> TempoDesk.Infrastructure    v
                                      TempoDesk.Domain

TempoDesk.Infrastructure ----------> TempoDesk.Application + TempoDesk.Domain
TempoDesk.DocumentProcessing ------> TempoDesk.Application
TempoDesk.Worker ------------------> Application + Infrastructure + DocumentProcessing
```

`TempoDesk.Domain` must not reference another project. The generated database-first model belongs only to Infrastructure.

## Shared build settings

`Directory.Build.props` applies to all six production projects, four test projects
and the database migration tool. It enables C# 14, nullable reference types,
implicit usings, .NET 10 recommended analyzers, deterministic builds and warnings
as errors. Root `global.json` selects a stable .NET 10 SDK.

Build and test from the repository root:

```bash
dotnet build server/TempoDesk.sln
dotnet test server/TempoDesk.sln --no-build
dotnet sln server/TempoDesk.sln list
```

Build and restore output goes to `artifacts/bin/<project>/<configuration>` and
`artifacts/obj/<project>`. Do not commit these directories or local configuration.
The repository conventions test verifies project counts and dependency direction.

## Database-first persistence

Run SQL migrations before scaffolding. The pinned local `dotnet-ef` tool and
Npgsql EF Core provider generate code only into
`src/TempoDesk.Infrastructure/Persistence/Generated`.
See [the database workflow](../database/README.md#scaffold-ef-core) for the command,
credentials and generated-file rules. Domain stays independent of EF Core.

## Complete target structure

Folders are created when their first use case is implemented. This avoids empty placeholder classes while keeping the target layout explicit.

```text
server/
├── TempoDesk.sln
├── README.md
├── src/
│   ├── TempoDesk.Api/
│   │   ├── Authorization/
│   │   │   ├── Policies.cs
│   │   │   ├── WorkspaceAuthorizationHandler.cs
│   │   │   └── WorkspaceRequirement.cs
│   │   ├── Configuration/
│   │   │   ├── AuthenticationOptions.cs
│   │   │   ├── CorsOptions.cs
│   │   │   └── JiraOptions.cs
│   │   ├── Contracts/
│   │   │   ├── Auth/
│   │   │   ├── Boards/
│   │   │   ├── Calendar/
│   │   │   ├── Exports/
│   │   │   ├── Jira/
│   │   │   ├── Projects/
│   │   │   ├── Reports/
│   │   │   ├── Tasks/
│   │   │   ├── TimeTracking/
│   │   │   └── Workspaces/
│   │   ├── Controllers/V1/
│   │   │   ├── AuthController.cs
│   │   │   ├── BoardsController.cs
│   │   │   ├── CalendarController.cs
│   │   │   ├── ExportsController.cs
│   │   │   ├── JiraController.cs
│   │   │   ├── ProjectsController.cs
│   │   │   ├── ReportsController.cs
│   │   │   ├── TasksController.cs
│   │   │   ├── TimeEntriesController.cs
│   │   │   └── WorkspacesController.cs
│   │   ├── Extensions/ServiceCollectionExtensions.cs
│   │   ├── Hubs/WorkspaceHub.cs
│   │   ├── Middleware/
│   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   └── RequestContextMiddleware.cs
│   │   ├── Properties/launchSettings.json
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   │
│   ├── TempoDesk.Application/
│   │   ├── Abstractions/
│   │   │   ├── Authentication/
│   │   │   │   ├── ICurrentUser.cs
│   │   │   │   └── ITokenService.cs
│   │   │   ├── Documents/IExcelExporter.cs
│   │   │   ├── Integrations/IJiraClient.cs
│   │   │   ├── Jobs/IBackgroundJobQueue.cs
│   │   │   ├── Persistence/
│   │   │   │   ├── ITaskRepository.cs
│   │   │   │   ├── IUnitOfWork.cs
│   │   │   │   └── IWorkspaceRepository.cs
│   │   │   └── Time/IClock.cs
│   │   ├── Common/
│   │   │   ├── Behaviors/
│   │   │   ├── Exceptions/
│   │   │   ├── Models/
│   │   │   └── Validation/
│   │   ├── Identity/
│   │   │   ├── Login/
│   │   │   ├── LoginWithGoogle/
│   │   │   ├── RefreshToken/
│   │   │   ├── Register/
│   │   │   └── RevokeSession/
│   │   ├── Workspaces/
│   │   │   ├── AcceptInvitation/
│   │   │   ├── ChangeMemberRole/
│   │   │   ├── CreateWorkspace/
│   │   │   ├── GetUserWorkspaces/
│   │   │   ├── InviteMember/
│   │   │   └── RemoveMember/
│   │   ├── Projects/
│   │   │   ├── CreateProject/
│   │   │   ├── GetProjects/
│   │   │   └── UpdateProject/
│   │   ├── Tasks/
│   │   │   ├── AssignTask/
│   │   │   ├── CreateSubtask/
│   │   │   ├── CreateTask/
│   │   │   ├── DeleteTask/
│   │   │   ├── GetTask/
│   │   │   ├── MoveTask/
│   │   │   ├── SearchTasks/
│   │   │   └── UpdateTask/
│   │   ├── Boards/
│   │   │   ├── CreateBoard/
│   │   │   ├── CreateColumn/
│   │   │   ├── GetBoard/
│   │   │   └── ReorderColumn/
│   │   ├── Scheduling/
│   │   │   ├── CreateWorkSession/
│   │   │   ├── DeleteWorkSession/
│   │   │   ├── GetCalendar/
│   │   │   ├── MoveWorkSession/
│   │   │   └── ResizeWorkSession/
│   │   ├── TimeTracking/
│   │   │   ├── GetActiveTimers/
│   │   │   ├── PauseTimer/
│   │   │   ├── ResumeTimer/
│   │   │   ├── StartTimer/
│   │   │   ├── StopTimer/
│   │   │   └── UpdateTimeEntry/
│   │   ├── Jira/
│   │   │   ├── ConnectJira/
│   │   │   ├── ImportIssues/
│   │   │   ├── PublishTask/
│   │   │   ├── QueueIssueSync/
│   │   │   └── ResolveConflict/
│   │   ├── Reports/
│   │   │   ├── GetMemberWorkload/
│   │   │   ├── GetOverview/
│   │   │   ├── GetOverdueTrend/
│   │   │   └── GetTimeComparison/
│   │   └── Exports/
│   │       ├── DownloadExport/
│   │       ├── GetExportStatus/
│   │       └── RequestTaskExport/
│   │
│   ├── TempoDesk.Domain/
│   │   ├── Common/
│   │   │   ├── AggregateRoot.cs
│   │   │   ├── BusinessRuleException.cs
│   │   │   ├── DomainEvent.cs
│   │   │   ├── Entity.cs
│   │   │   └── ValueObject.cs
│   │   ├── Identity/
│   │   ├── Workspaces/
│   │   │   ├── Events/
│   │   │   ├── Rules/
│   │   │   ├── Workspace.cs
│   │   │   ├── WorkspaceMember.cs
│   │   │   ├── WorkspaceRole.cs
│   │   │   └── WorkspaceType.cs
│   │   ├── Projects/
│   │   ├── Tasks/
│   │   │   ├── Events/
│   │   │   ├── Rules/
│   │   │   ├── TaskAssignee.cs
│   │   │   ├── TaskItem.cs
│   │   │   ├── TaskPriority.cs
│   │   │   ├── TaskSource.cs
│   │   │   └── TaskStatus.cs
│   │   ├── Boards/
│   │   │   ├── Board.cs
│   │   │   └── BoardColumn.cs
│   │   ├── Scheduling/
│   │   │   ├── WorkSession.cs
│   │   │   └── WorkSessionStatus.cs
│   │   └── TimeTracking/
│   │       ├── TimeEntry.cs
│   │       ├── TimerEvent.cs
│   │       └── TimerEventType.cs
│   │
│   ├── TempoDesk.Infrastructure/
│   │   ├── Authentication/
│   │   │   ├── CurrentUser.cs
│   │   │   ├── GoogleAuthenticationService.cs
│   │   │   ├── IdentityService.cs
│   │   │   └── JwtTokenService.cs
│   │   ├── Email/
│   │   ├── Jira/
│   │   │   ├── JiraApiClient.cs
│   │   │   ├── JiraIssueMapper.cs
│   │   │   ├── JiraOAuthService.cs
│   │   │   ├── JiraTokenProtector.cs
│   │   │   └── JiraWebhookVerifier.cs
│   │   ├── Jobs/
│   │   │   ├── JobRepository.cs
│   │   │   └── PostgreSqlJobQueue.cs
│   │   ├── Persistence/
│   │   │   ├── Generated/
│   │   │   │   ├── Entities/
│   │   │   │   └── TempoDeskDbContext.cs
│   │   │   ├── Mappers/
│   │   │   ├── Queries/
│   │   │   ├── Repositories/
│   │   │   └── Transactions/UnitOfWork.cs
│   │   ├── Realtime/
│   │   ├── Storage/
│   │   └── DependencyInjection.cs
│   │
│   ├── TempoDesk.DocumentProcessing/
│   │   ├── Excel/
│   │   │   ├── Models/
│   │   │   ├── Sheets/
│   │   │   │   ├── MemberStatisticsSheetWriter.cs
│   │   │   │   ├── SummarySheetWriter.cs
│   │   │   │   ├── TaskDetailsSheetWriter.cs
│   │   │   │   └── TimeEntriesSheetWriter.cs
│   │   │   ├── ExcelTaskExporter.cs
│   │   │   └── WorkbookStyles.cs
│   │   └── DependencyInjection.cs
│   │
│   └── TempoDesk.Worker/
│       ├── Jobs/
│       │   ├── Exports/GenerateExcelExportJob.cs
│       │   ├── Jira/
│       │   │   ├── ProcessJiraWebhookJob.cs
│       │   │   ├── RenewJiraWebhookJob.cs
│       │   │   └── SyncIssueToJiraJob.cs
│       │   ├── Maintenance/
│       │   │   ├── CleanupExpiredFilesJob.cs
│       │   │   └── CleanupRefreshTokensJob.cs
│       │   ├── Notifications/SendTaskReminderJob.cs
│       │   └── Reports/AggregateDailyStatisticsJob.cs
│       ├── Processing/
│       │   ├── BackgroundJobDispatcher.cs
│       │   └── BackgroundJobProcessor.cs
│       └── Program.cs
│
└── tests/
    ├── TempoDesk.Domain.Tests/
    │   ├── Tasks/
    │   ├── Scheduling/
    │   └── TimeTracking/
    ├── TempoDesk.Application.Tests/
    │   ├── Workspaces/
    │   ├── Tasks/
    │   ├── Scheduling/
    │   ├── TimeTracking/
    │   └── Jira/
    ├── TempoDesk.IntegrationTests/
    │   ├── Persistence/
    │   ├── Jobs/
    │   └── Jira/
    └── TempoDesk.Api.Tests/
        ├── Auth/
        ├── Workspaces/
        ├── Tasks/
        └── Authorization/
```

## Placement rules

- Add one Application folder per use case; do not create large `TaskService` or `WorkspaceService` classes.
- Keep controllers thin: translate HTTP contracts, invoke one use case, and translate its result.
- Put business invariants in Domain and orchestration in Application.
- Put EF-generated classes only in `Infrastructure/Persistence/Generated`; never edit them by hand.
- Use explicit repositories for aggregates and direct read queries for dashboards and reports.
- Keep Jira HTTP/OAuth concerns in Infrastructure and Jira orchestration in Application.
- Keep job handlers thin; they dispatch Application use cases.
- Create a listed source file only when implementing its corresponding behavior and test.
