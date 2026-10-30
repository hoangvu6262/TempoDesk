# TempoDesk — Kế hoạch triển khai chi tiết theo Sprint

**Phiên bản:** 1.0  
**Ngày lập:** 2026-09-23  
**Phạm vi:** Web React + ASP.NET Core API/Worker + PostgreSQL  
**Nhịp Sprint đề xuất:** 2 tuần  
**Tổng thời gian tham chiếu:** 24 tuần cho 12 sprint

## 1. Mục tiêu của roadmap

Roadmap này chuyển thiết kế MVP TempoDesk thành các increment có thể build, kiểm thử và demo độc lập. Trình tự ưu tiên nền tảng bảo mật và cô lập workspace trước, sau đó mới triển khai task, Kanban, lịch, time tracking, Jira, báo cáo và Excel.

Các mốc chính:

| Mốc | Sprint | Kết quả |
|---|---:|---|
| Foundation | 0 | Repository, solution, CI và local environment hoạt động |
| Account-ready | 1–2 | Đăng nhập, Personal Workspace và Team Workspace hoạt động |
| Task-ready | 3–4 | Quản lý task và Kanban dùng được |
| Planning-ready | 5–6 | Calendar planning và time tracking dùng được |
| Jira-ready | 7–8 | Jira Cloud đồng bộ hai chiều |
| Reporting-ready | 9–10 | Dashboard và Excel hoạt động |
| MVP release | 11 | Bảo mật, hiệu năng, UAT và production readiness |

## 2. Quy ước chung cho mọi Sprint

### 2.1 Definition of Ready

Một story chỉ được đưa vào sprint khi:

- Có mô tả hành vi và tiêu chí nghiệm thu rõ ràng.
- Đã xác định workspace/role nào được phép thực hiện.
- Đã xác định thay đổi database và API contract nếu có.
- Thiết kế UI đủ rõ để triển khai mà không phải suy đoán nghiệp vụ.
- Dependency từ sprint trước đã hoàn thành.
- Có phương án kiểm thử tự động phù hợp.

### 2.2 Definition of Done

Một story được xem là hoàn thành khi:

- Migration SQL được review và chạy được trên database sạch.
- EF Core database-first model được scaffold lại nếu schema thay đổi.
- Backend build không có lỗi hoặc warning mới.
- Frontend lint, type-check và build thành công.
- Unit/integration/API/E2E test liên quan đều qua.
- Authorization và workspace isolation được kiểm tra.
- API contract, error response và audit log đã được xác nhận.
- Không có secret hoặc dữ liệu nhạy cảm trong source/log.
- Tài liệu kỹ thuật và hướng dẫn vận hành được cập nhật.
- Chức năng được demo trên staging và đạt tiêu chí nghiệm thu.

### 2.3 Quy tắc database-first

Mỗi thay đổi dữ liệu theo thứ tự:

1. Viết migration SQL có `up` và phương án rollback an toàn.
2. Chạy migration trên PostgreSQL local sạch.
3. Scaffold EF Core model vào `TempoDesk.Infrastructure/Persistence/Generated`.
4. Không sửa file generated bằng tay.
5. Cập nhật mapper/repository/read query.
6. Chạy integration test với PostgreSQL thật trong container.

### 2.4 Quality gates

Các lệnh tối thiểu trước khi merge:

```bash
dotnet build server/TempoDesk.sln
dotnet test server/TempoDesk.sln --no-build
npm --prefix client/web run lint
npm --prefix client/web run build
```

Khi đã có Playwright:

```bash
npm --prefix client/web run test:e2e
```

---

## Sprint 0 — Foundation và Developer Experience

### Mục tiêu

Tạo nền tảng có thể phát triển lặp lại: repository rõ ràng, backend/frontend build được, PostgreSQL chạy local, quy trình migration và CI hoạt động.

### Backend

- Hoàn thiện solution gồm Api, Application, Domain, Infrastructure, DocumentProcessing và Worker.
- Thiết lập project references đúng dependency direction.
- Thêm middleware xử lý exception và Problem Details cơ bản.
- Thêm structured logging và `traceId` cho request.
- Thêm health endpoint cho API process.
- Thiết lập configuration binding và validation khi startup.
- Quy định namespace, nullable reference type và analyzer.

### Frontend

- Khởi tạo React + TypeScript + Vite.
- Thiết lập React Router và application shell tối thiểu.
- Thiết lập ESLint, format, environment variables và API base URL.
- Tạo lớp HTTP client và xử lý Problem Details cơ bản.
- Tạo trang health/dev landing để kiểm tra kết nối API.

### Database và infrastructure

- Tạo Docker Compose cho PostgreSQL, API, Worker và web development.
- Tạo cấu trúc `database/migrations`, `functions`, `views`, `seeds`.
- Chọn công cụ chạy migration SQL và quy ước đánh số migration.
- Tạo database bootstrap và application database role quyền hạn thấp.
- Thêm `.env.example`, không commit secret thật.

### CI/CD

- Backend restore/build/test.
- Frontend install/lint/build.
- Migration validation trên PostgreSQL container.
- Dependency vulnerability scan.

### Kiểm thử

- Architecture test kiểm tra Domain không tham chiếu layer khác.
- Smoke test health endpoint.
- Test migration chạy được trên database sạch.

### Tiêu chí nghiệm thu

- Một developer mới có thể chạy toàn bộ hệ thống local từ README.
- API và Worker khởi động, kết nối PostgreSQL và health check thành công.
- CI chạy xanh trên commit sạch.
- Không có secret trong repository.

### Không thuộc Sprint

- Chưa triển khai user, workspace hoặc task.
- Chưa thêm Redis, message broker hoặc Kubernetes.

---

## Sprint 1 — Identity và Authentication

### Mục tiêu

Người dùng có thể đăng ký, xác minh email, đăng nhập bằng email/mật khẩu hoặc Google, làm mới token và quản lý phiên đăng nhập.

### Database

Tạo các bảng:

- `users`
- `external_logins`
- `refresh_tokens`
- `email_verification_tokens`
- `password_reset_tokens`
- `security_audit_logs`

Index quan trọng:

- Unique index cho email chuẩn hóa.
- Unique index cho `(provider, provider_subject)`.
- Index cho refresh token family và expiration.

### Backend

- Cấu hình ASP.NET Core Identity với PostgreSQL store.
- Use cases: Register, VerifyEmail, Login, LoginWithGoogle, RefreshToken, Logout, RevokeSession và Forgot/ResetPassword.
- Access token ngắn hạn và refresh-token rotation.
- Chỉ lưu hash của refresh token.
- Phát hiện reuse của refresh token đã rotate và thu hồi token family.
- Rate limiting cho register, login, forgot password và refresh.
- Audit đăng nhập thành công/thất bại và thu hồi phiên.

### Frontend

- Trang đăng ký, đăng nhập và quên/đặt lại mật khẩu.
- Nút đăng nhập Google.
- Trang xử lý email verification.
- Auth state, protected route và silent token refresh.
- Trang quản lý các phiên đăng nhập theo thiết bị.

### Email

- Template xác minh email.
- Template đặt lại mật khẩu.
- Mail service giả lập cho local development.

### Kiểm thử

- Unit test password/token policy.
- Integration test unique email và refresh-token rotation.
- API test login sai, account chưa xác minh và token hết hạn.
- E2E đăng ký → xác minh → đăng nhập → đăng xuất.

### Tiêu chí nghiệm thu

- Người dùng đăng nhập được bằng cả hai phương thức.
- Một email không tạo hai account ngoài ý muốn.
- Refresh token bị đánh cắp không thể reuse sau rotation.
- Không có token hoặc password xuất hiện trong log.

---

## Sprint 2 — Personal Workspace, Team Workspace và Authorization

### Mục tiêu

Một account đồng thời sử dụng Personal Workspace và nhiều Team Workspace với quyền truy cập rõ ràng.

### Database

Tạo các bảng:

- `workspaces`
- `workspace_members`
- `workspace_invitations`
- `workspace_role_permissions` nếu quyền cần cấu hình dữ liệu

Constraint quan trọng:

- Một user chỉ có một Personal Workspace.
- Một user chỉ có một membership hoạt động trong mỗi workspace.
- Invitation token unique và có thời hạn.
- Workspace type chỉ nhận `personal` hoặc `team`.

### Backend

- Tạo Personal Workspace tự động sau khi account được tạo.
- Use cases: CreateTeamWorkspace, InviteMember, AcceptInvitation, CancelInvitation, ChangeRole, RemoveMember và LeaveWorkspace.
- Role: Owner, Admin, Member và Viewer.
- Authorization policy theo workspace và action.
- Owner không thể tự rời nếu chưa chuyển ownership.
- Audit thay đổi thành viên và quyền.

### Frontend

- Workspace switcher.
- Trang tạo Team Workspace.
- Trang thành viên, invitation và role management.
- Hiển thị Personal Workspace riêng biệt.
- Ẩn hoặc disable action không đủ quyền, nhưng backend vẫn là lớp bảo vệ chính.

### Kiểm thử

- API test không đọc/sửa được dữ liệu workspace khác.
- Test owner, admin, member và viewer theo permission matrix.
- Test invitation hết hạn, dùng lại và sai email.
- E2E tạo team → mời user → chấp nhận → đổi vai trò.

### Tiêu chí nghiệm thu

- Account mới luôn có Personal Workspace.
- User chuyển được giữa personal và nhiều team workspace.
- Dữ liệu cá nhân không xuất hiện trong team workspace.
- Permission matrix được kiểm thử tự động.

---

## Sprint 3 — Project, Task, Subtask và My Work

### Mục tiêu

Cho phép quản lý công việc cốt lõi trước khi xây Kanban và Calendar.

### Database

Tạo các bảng:

- `projects`
- `project_members`
- `tasks`
- `task_assignees`
- `labels`
- `task_labels`
- `task_comments`
- `task_activity_logs`

Task có:

- `workspace_id`, `project_id`, `parent_task_id`
- title, description, priority, status
- start date, due date, estimated minutes
- source type, sort key, row version và soft-delete timestamp

### Backend

- CRUD project với project-level membership nếu cần.
- Use cases CreateTask, UpdateTask, DeleteTask, RestoreTask và GetTask.
- Tạo subtask và kiểm tra parent cùng workspace/project.
- Gán nhiều assignee.
- Comment và activity log.
- Query My Work tổng hợp task cá nhân và task nhóm được giao.
- Optimistic concurrency bằng row version.

### Frontend

- Danh sách project.
- Task list và task detail side panel.
- Form tạo/sửa task.
- Assignee, label, priority, deadline và estimate.
- Subtask checklist.
- My Work: hôm nay, sắp đến hạn, quá hạn và đã hoàn thành gần đây.

### Kiểm thử

- Domain test title, deadline, subtask và assignment rules.
- Integration test foreign key, soft delete và concurrency conflict.
- API test permission theo workspace/project.
- E2E tạo project → task → subtask → assign → comment.

### Tiêu chí nghiệm thu

- User quản lý được task cá nhân và task nhóm.
- Hai user sửa cùng task không gây ghi đè im lặng.
- My Work chỉ trả dữ liệu người dùng được phép xem.

---

## Sprint 4 — Kanban và Realtime Task Updates

### Mục tiêu

Biến task management thành board kéo-thả có thứ tự ổn định và cập nhật realtime.

### Database

Tạo các bảng:

- `boards`
- `board_columns`

Bổ sung:

- `tasks.board_column_id`
- `tasks.sort_key`
- WIP limit tùy chọn trên column

### Backend

- CRUD board và column.
- MoveTask cập nhật column, sort key và row version trong transaction.
- Quy tắc fractional ordering và rebalance khi khoảng key quá nhỏ.
- Kiểm tra WIP limit; có thể warning hoặc block theo cấu hình board.
- SignalR hub theo workspace/project.
- Broadcast task created, updated, moved và deleted sau commit.

### Frontend

- Kanban board bằng dnd-kit.
- Kéo giữa column và sắp xếp trong column.
- Optimistic update và rollback khi API từ chối.
- Filter theo assignee, label, priority, deadline và source.
- Hiển thị realtime thay đổi từ thành viên khác.
- Badge task Jira và sync state được chuẩn bị nhưng chưa kết nối Jira.

### Kiểm thử

- Unit test tạo sort key và rebalance.
- Integration test hai thao tác move đồng thời.
- Component test task card và filters.
- Playwright test drag-and-drop, rollback và realtime giữa hai browser context.

### Tiêu chí nghiệm thu

- Task giữ đúng thứ tự sau reload.
- Kéo-thả phản hồi nhanh và rollback đúng khi lỗi.
- Hai thành viên xem cùng board nhận thay đổi mà không reload.

---

## Sprint 5 — Calendar Planning và Work Sessions

### Mục tiêu

Người dùng lập kế hoạch theo lịch, chia một task thành nhiều phiên và xem lịch cá nhân/nhóm.

### Database

Tạo các bảng:

- `work_sessions`
- `session_assignees`

Trường chính:

- planned start/end ở UTC
- estimated minutes
- task, workspace, creator và status
- row version

### Backend

- CreateWorkSession từ task chưa lên lịch.
- MoveWorkSession, ResizeWorkSession, CopyWorkSession và DeleteWorkSession.
- Cho phép session chồng lấn.
- Không tự thay đổi task deadline khi di chuyển session.
- Calendar query theo range và danh sách thành viên.
- Kiểm tra timezone input và member thuộc workspace.

### Frontend

- Day, week, month và list view.
- Sidebar task chưa được lên lịch.
- Kéo task vào calendar để tạo session.
- Kéo session đổi giờ/ngày và resize duration.
- Hiển thị overlapping sessions cạnh nhau.
- Filter all members, only me và chọn nhiều thành viên.
- Màu theo member hoặc project.

### Kiểm thử

- Unit test planned duration và timezone conversion.
- Integration test overlapping sessions được chấp nhận.
- API test không thể tạo session cho task workspace khác.
- Playwright test drag task vào lịch, move và resize.

### Tiêu chí nghiệm thu

- Một task chia được thành nhiều phiên sáng/chiều/tối.
- Session chồng nhau hiển thị và lưu chính xác.
- Team calendar lọc đúng theo thành viên.
- Personal session không xuất hiện trong team calendar.

---

## Sprint 6 — Concurrent Time Tracking

### Mục tiêu

Theo dõi thời gian thực tế, cho phép nhiều timer đồng thời và so sánh với kế hoạch.

### Database

Tạo các bảng:

- `time_entries`
- `timer_events`

Mỗi actual interval có start/end UTC, task, user, workspace, source và audit metadata.

### Backend

- StartTimer, PauseTimer, ResumeTimer và StopTimer.
- Cho phép nhiều timer mở cùng lúc.
- Trả cảnh báo danh sách timer đang chạy nhưng không chặn timer mới.
- Sửa time entry thủ công với audit log.
- Tính summed duration.
- Tính union duration để khoảng chồng nhau chỉ tính một lần.
- SignalR cập nhật timer giữa nhiều tab/thiết bị.

### Frontend

- Global timer tray hiển thị nhiều timer.
- Timer trong task detail và work session.
- Cảnh báo không chặn khi khởi động timer trùng.
- Trang time-entry history và chỉnh sửa có lý do.
- Hiển thị planned, actual summed và actual non-overlapping.

### Kiểm thử

- Domain test union của nhiều khoảng giao nhau, lồng nhau và rời nhau.
- Test pause/resume tạo interval chính xác.
- Integration test nhiều timer đồng thời.
- E2E chạy hai timer, pause một timer và kiểm tra báo cáo sơ bộ.

### Tiêu chí nghiệm thu

- Nhiều timer chạy đồng thời và đồng bộ giữa tab.
- Không bị nhân đôi interval khi người dùng nhấn thao tác lặp.
- Hai phép tính thời lượng cho kết quả đúng và giải thích được.

---

## Sprint 7 — Jira Cloud Connection và Initial Import

### Mục tiêu

Người dùng kết nối Atlassian Cloud an toàn, chọn site/project và nhập issue vào TempoDesk.

### Database

Tạo các bảng:

- `jira_connections`
- `jira_project_mappings`
- `jira_status_mappings`
- `jira_user_mappings`
- `jira_issue_mappings`
- `jira_webhook_events`

### Backend

- OAuth 2.0 authorization code flow với state/PKCE phù hợp.
- Mã hóa access/refresh token tại rest.
- Lấy accessible resources và chọn Jira cloud site.
- Đọc Jira projects, issue types, statuses và users.
- Mapping Jira project với TempoDesk project.
- Mapping Jira status với board column.
- Import lần đầu theo JQL/pagination.
- Đăng ký dynamic webhook và lưu expiration.
- Lưu raw webhook trước khi trả response.

### Frontend

- Trang Connect Jira.
- Wizard chọn site, project và mapping status.
- Hiển thị tiến trình import.
- Task Jira có issue key, external URL và source badge.
- Trang connection status và disconnect.

### Kiểm thử

- Jira client contract test bằng stub server.
- Test OAuth state mismatch và token encryption.
- Test pagination và import idempotency.
- Test webhook duplicate không tạo task trùng.

### Tiêu chí nghiệm thu

- Kết nối được tenant `*.atlassian.net`.
- Issue được import đúng project, status, assignee và deadline được map.
- Chạy import lại không tạo duplicate.
- Token Jira không xuất hiện dạng plaintext trong database/log.

---

## Sprint 8 — Jira Two-Way Sync và Conflict Resolution

### Mục tiêu

Cho phép sửa Jira task trong TempoDesk và đồng bộ ngược, đồng thời xử lý retry, rate limit và conflict mà không mất dữ liệu.

### Database

Tạo các bảng:

- `jira_sync_jobs`
- `jira_sync_conflicts`
- `background_jobs`
- `background_job_attempts`

Bổ sung idempotency key, source version, app/Jira timestamps và sync status.

### Backend và Worker

- App-to-Jira job được tạo cùng transaction với thay đổi local.
- Jira-to-app webhook được xử lý bất đồng bộ.
- Retry có exponential backoff cho timeout, HTTP 429 và 5xx.
- Dead-letter sau số lần retry giới hạn.
- Chống vòng lặp bằng source/version/correlation metadata.
- Merge tự động khi hai bên sửa field khác nhau.
- Tạo conflict khi cùng field thay đổi.
- Publish manual task thành Jira issue theo thao tác rõ ràng của user.
- Worker gia hạn webhook trước khi hết hạn.

### Frontend

- Sync status: Synced, Syncing, Failed và Conflict.
- Retry action cho sync thất bại.
- Conflict screen so sánh giá trị TempoDesk và Jira theo field.
- Nút “Tạo issue trên Jira” cho manual task.
- Hiển thị lỗi mapping/status/permission có hướng dẫn xử lý.

### Kiểm thử

- Integration test durable job và `FOR UPDATE SKIP LOCKED`.
- Test idempotency khi webhook/job được giao lại.
- Test rate-limit retry và dead-letter.
- Test same-field conflict và different-field merge.
- E2E chỉnh sửa task trong app và xác nhận payload Jira stub.

### Tiêu chí nghiệm thu

- Thay đổi trong app không bị mất khi Jira tạm thời lỗi.
- Webhook gửi lại không gây duplicate effect.
- Người dùng giải quyết được conflict theo từng field.
- Manual task chỉ lên Jira khi user chủ động chọn.

---

## Sprint 9 — Dashboard và Statistics

### Mục tiêu

Cung cấp dashboard thống nhất cho cá nhân và nhóm với filter dùng chung.

### Database

Tạo:

- `daily_task_statistics`
- `daily_member_statistics`
- View/read query cho workload và time comparison

### Backend và Worker

- Overview KPI.
- Tasks by status, priority, member, project và source.
- Overdue list/trend.
- Estimate versus actual.
- Summed time versus non-overlapping time.
- Member workload và completion trend.
- Jira sync success/failure/conflict statistics.
- Daily aggregation job có thể chạy lại an toàn.
- Authorization cho report theo workspace/project.

### Frontend

- Report dashboard và filter bar dùng chung.
- Biểu đồ có loading, empty và error states.
- Drill-down từ chart tới danh sách task.
- Lưu filter vào URL để chia sẻ trong phạm vi quyền.
- Phân biệt metric cộng dồn và metric không trùng.

### Kiểm thử

- Query test với dữ liệu nhiều workspace và timezone.
- Test daily aggregation chạy lại không nhân đôi.
- Test filter combinations.
- API performance test với tập dữ liệu tham chiếu.
- Component test chart và drill-down.

### Tiêu chí nghiệm thu

- Dashboard cá nhân và nhóm trả số liệu đúng theo quyền.
- Filter áp dụng nhất quán trên mọi widget.
- Số liệu estimate/actual khớp time entries nguồn.

---

## Sprint 10 — Excel Export

### Mục tiêu

Xuất file Excel bất đồng bộ, đúng dữ liệu dashboard đang lọc và an toàn khi tải xuống.

### Database

Tạo bảng `report_exports` với:

- requester, workspace, filters snapshot
- status, progress, file metadata
- created/started/completed/expired timestamps
- error summary không chứa dữ liệu nhạy cảm

### Backend và Worker

- RequestTaskExport tạo durable job.
- GetExportStatus và DownloadExport.
- Workbook gồm Summary, Task Details, Time Entries và Member Statistics.
- Limit date range, row count, concurrent exports và file size.
- File retention và cleanup job.
- Download chỉ cho requester hoặc role được cấp quyền.
- CSV fallback cho dữ liệu vượt Excel row limit nếu sau này cần.

### Frontend

- Nút Export Excel dùng filter dashboard hiện tại.
- Progress/status notification.
- Danh sách export gần đây.
- Download khi completed; retry khi failed.
- Hiển thị thời gian hết hạn file.

### Kiểm thử

- Test workbook sheet names, headers, number/date formats và row values.
- Test timezone trong Excel.
- Test unauthorized download.
- Test export lớn chạy nền không làm chậm API request.
- Test cleanup chỉ xóa file hết hạn.

### Tiêu chí nghiệm thu

- File Excel khớp dashboard filter và quyền truy cập.
- File lớn được tạo bởi Worker, không giữ HTTP request lâu.
- Workbook mở được và có định dạng dễ đọc.

---

## Sprint 11 — Hardening, UAT và MVP Release

### Mục tiêu

Đưa toàn bộ MVP lên trạng thái có thể triển khai production và vận hành an toàn.

### Security

- Review authorization trên toàn bộ endpoint.
- Kiểm tra tenant/workspace isolation.
- Dependency và secret scan.
- Kiểm tra CORS, headers, rate limiting và upload restrictions.
- Kiểm tra token/key rotation procedure.
- Threat-model các luồng Google, Jira webhook, Excel download và invitations.

### Performance

- Load test task list, Kanban, calendar và dashboard.
- Kiểm tra slow query, index và query plan.
- Test Worker concurrency và queue backlog recovery.
- Test SignalR reconnect và burst updates.
- Thiết lập giới hạn request/export hợp lý.

### Reliability

- Backup PostgreSQL và diễn tập restore.
- Kiểm thử API/Worker restart khi có job đang xử lý.
- Kiểm thử Jira outage, rate limiting và webhook delay.
- Kiểm thử object storage/email outage.
- Health/readiness checks và operational alerts.

### UX và accessibility

- Keyboard navigation cơ bản cho form, Kanban và calendar.
- Color contrast và accessible labels.
- Responsive tablet và mobile-browser essential flows.
- Empty/loading/error states thống nhất.
- Việt hóa/Anh hóa các chuỗi chính nếu i18n nằm trong MVP release.

### UAT

Các kịch bản bắt buộc:

1. Đăng ký account và sử dụng Personal Workspace.
2. Tạo Team Workspace, mời thành viên và đổi role.
3. Tạo task/subtask và kéo qua Kanban.
4. Chia task thành nhiều work session và resize trên lịch.
5. Chạy nhiều timer chồng nhau.
6. Kết nối Jira, import issue, sửa trong TempoDesk và sync ngược.
7. Tạo và giải quyết conflict Jira.
8. Xem dashboard theo member/project/date.
9. Xuất và tải Excel.
10. Xác minh user không truy cập được workspace ngoài quyền.

### Release artifacts

- Production Docker images.
- Migration release package.
- Environment variable/secret inventory.
- Runbook deploy, rollback, backup/restore và incident handling.
- Release notes và known limitations.
- Post-release monitoring dashboard.

### Tiêu chí nghiệm thu

- Không còn lỗi blocker/critical.
- Tất cả critical E2E flows qua trên staging.
- Backup restore được kiểm chứng.
- Production deployment và rollback đã diễn tập.
- Product owner chấp thuận UAT.

---

## 3. Dependency giữa các Sprint

```text
Sprint 0
  └── Sprint 1 Identity
        └── Sprint 2 Workspace/Authorization
              └── Sprint 3 Projects/Tasks
                    ├── Sprint 4 Kanban
                    └── Sprint 5 Calendar
                          └── Sprint 6 Time Tracking

Sprint 2 + Sprint 3
  └── Sprint 7 Jira Connection
        └── Sprint 8 Jira Two-Way Sync

Sprint 3 + Sprint 5 + Sprint 6
  └── Sprint 9 Reports
        └── Sprint 10 Excel Export

Sprint 1–10
  └── Sprint 11 Hardening/UAT/Release
```

Sprint 4 và Sprint 5 có thể thực hiện song song nếu có hai nhóm frontend/backend độc lập. Sprint 7 có thể bắt đầu nghiên cứu OAuth/Jira contract sớm, nhưng production integration vẫn phụ thuộc Identity, Workspace và Task model ổn định.

## 4. Phân công tham chiếu cho nhóm nhỏ

Với nhóm 3 người:

| Vai trò | Trọng tâm |
|---|---|
| Backend | API, Domain/Application, PostgreSQL, Worker và Jira |
| Frontend | React, Kanban, Calendar, Reports và E2E |
| Full-stack/QA | Identity, integration tests, CI/CD, Excel và UAT |

Với một developer, giữ nguyên thứ tự sprint và giảm số story mỗi sprint; không bỏ quality gate, workspace isolation hoặc test của time/Jira vì đây là các vùng rủi ro cao.

## 5. Rủi ro và biện pháp giảm thiểu

| Rủi ro | Tác động | Giảm thiểu |
|---|---|---|
| Jira workflow khác nhau giữa project | Sync status thất bại | Mapping wizard và validation trước import |
| Jira rate limit/outage | Job tồn đọng | Durable queue, retry/backoff và dead-letter |
| Sai workspace authorization | Rò rỉ dữ liệu | Policy tập trung và integration test tenant isolation |
| Timer chồng nhau khó hiểu | Báo cáo gây nhầm | Hiển thị cả summed và non-overlapping duration |
| Calendar timezone/DST | Sai giờ | Lưu UTC, test timezone và DST boundary |
| Export quá lớn | Tốn RAM/CPU | Background Worker, giới hạn row/file và streaming khi phù hợp |
| Database-first overwrite code | Mất custom logic | Generated folder riêng và không sửa generated files |
| Scope tăng trong Sprint | Trễ MVP | Giữ danh sách deferred và change-control rõ ràng |

## 6. Backlog sau MVP

Ưu tiên sau khi MVP ổn định:

1. Chrome extension tạo nhanh task từ trang đang xem.
2. Mobile app dùng chung API.
3. Push notifications và offline-friendly flows.
4. Epic/Sprint nâng cao.
5. Recurring tasks và calendar integration ngoài Jira.
6. Redis chỉ khi metrics chứng minh cần cache/lock/rate-limit phân tán.
7. Tách service chỉ khi module cần scale/deploy độc lập hoặc có team sở hữu riêng.

## 7. Chỉ số theo dõi tiến độ

Theo dõi theo sprint:

- Story đạt Definition of Done.
- Build/test success rate.
- Số defect lọt sang sprint sau.
- API p95 latency trên staging.
- Slow queries mới.
- Jira sync success/retry/dead-letter rate.
- E2E flakiness.
- Thời gian từ mở pull request đến merge.

Không đánh giá tiến độ chỉ bằng số lượng file hoặc dòng code. Increment chỉ có giá trị khi đạt tiêu chí nghiệm thu và có thể demo trên staging.
