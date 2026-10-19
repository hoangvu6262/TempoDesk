# Database-first workflow

## Chạy migration

Tại thư mục gốc repo, sau khi cấu hình `.env` từ `.env.example`:

```bash
docker compose run --build --rm migrator
```

Compose khởi động PostgreSQL, chờ health check rồi chạy migrator.
Mật khẩu được truyền từ `.env` khi chạy container; không có trong image.
Service migrator chỉ chạy khi gọi tên nó, không tự chạy theo `docker compose up`.
`--build` bảo đảm image chứa các file SQL và code mới nhất.
Build và run dùng cùng `DOCKER_DEFAULT_PLATFORM` (mặc định `linux/amd64`).
Để chạy native trên máy ARM, đặt `DOCKER_DEFAULT_PLATFORM=linux/arm64` trước lệnh Compose.

- `APPLY`: migration mới đã được commit cùng bản ghi lịch sử.
- `SKIP`: migration đã áp dụng và checksum vẫn khớp.
- Lỗi checksum: dừng lại, không sửa bản ghi lịch sử để bỏ qua lỗi.
- Lỗi SQL: rollback migration đang chạy, không ghi nhận thành công.
  Các migration đã commit trước đó vẫn được giữ lại.

Không chạy `docker/postgres/init.sql` hoặc file tạo `schema_history` thủ công trên database mới.
`init.sql` được PostgreSQL chạy khi volume rỗng; migrator tự tạo bảng lịch sử.
Bootstrap dùng psql native để đọc biến môi trường, không phụ thuộc executable bit
hoặc interpreter của shell script trên máy host. Volume cũ giữ nguyên role/dữ liệu;
không cần chạy lại bootstrap sau khi cập nhật file này.
Với database cũ đã tạo bảng thủ công, phải đăng ký đúng checksum của migration
đầu tiên như bước hướng dẫn trước; runner không tự nhận một bảng có sẵn là hợp lệ.

## Thay đổi bảng những lần sau

1. Tạo file trong `database/migrations`, tên `YYYYMMDDHHMM_description.sql`.
   Tên mới phải đứng sau migration cuối cùng theo thứ tự từ điển.
2. Viết SQL tạo/sửa bảng, constraint và index cho chức năng đang triển khai.
   Không thêm `BEGIN`/`COMMIT`: runner quản lý transaction.
3. Chạy `docker compose run --build --rm migrator`.
4. Refresh DBeaver, kiểm tra bảng và `public.schema_history` bằng migrator.
5. Scaffold lại EF Core khi có bảng nghiệp vụ, rồi cập nhật code liên quan.

Ví dụ SQL khi bảng `tasks` đã tồn tại:

```sql
ALTER TABLE public.tasks ADD COLUMN due_date timestamptz;
```

Giữ nguyên file đã áp dụng; thay đổi tiếp theo phải nằm trong file mới.
Git giữ SQL và shell script ở định dạng LF để checksum không đổi do xuống dòng
khác nhau giữa Windows và macOS/Linux.
Không đổi tên, xóa migration cũ hoặc sửa schema trực tiếp trong DBeaver mà không
lưu SQL vào repo. Runner hiện kiểm tra checksum của những file còn trong thư mục;
không tự phát hiện một file lịch sử đã bị xóa hay cấm chèn file có thứ tự cũ.

`schema_history` thuộc migrator, chứa tên file, SHA-256, thời điểm áp dụng và thời gian
chạy tính bằng milliseconds. API/Worker không có quyền đọc/ghi bảng này.
Bảng vận hành này không cần model nghiệp vụ EF Core.

## Scaffold EF Core

Cài .NET 10 SDK. Tool `dotnet-ef` và package EF Design được pin ở 10.0.10;
Npgsql EF provider 10.0.3 chỉ nằm trong Infrastructure.

Sau khi migration tạo bảng nghiệp vụ thành công, đặt mật khẩu migrator trong
biến `TEMPODESK_MIGRATION_PASSWORD`, rồi chạy từ thư mục gốc repo:

```bash
bash database/scaffold.sh public.users public.workspaces
```

Đây là ví dụ khi các bảng đó đã tồn tại. Hiện repo chỉ có `schema_history`,
chưa cần tạo entity nghiệp vụ. Script yêu cầu danh sách bảng tường minh và từ
chối `schema_history`. Mỗi lần chạy, truyền đầy đủ các bảng cần có trong context.

Trong zsh trên macOS, có thể nhập mật khẩu mà không lưu vào lịch sử lệnh:

```bash
read -s 'TEMPODESK_MIGRATION_PASSWORD?Nhập MIGRATION_PASSWORD: '
export TEMPODESK_MIGRATION_PASSWORD
```

Ghi đè database/host/port bằng `TEMPODESK_DB_NAME`, `TEMPODESK_DB_HOST`,
`TEMPODESK_DB_PORT` nếu khác `tempodesk`/`localhost`/`5432`.
Script dùng `PGPASSWORD` cho Npgsql, nên mật khẩu không xuất hiện trong đối số
CLI hoặc code generated; không chạy script với `bash -x`.

Script khôi phục local tool, rồi gọi lệnh tương đương:

```bash
dotnet ef dbcontext scaffold \
  'Host=localhost;Port=5432;Database=tempodesk;Username=tempodesk_migrator;GSS Encryption Mode=Disable' \
  Npgsql.EntityFrameworkCore.PostgreSQL \
  --project server/src/TempoDesk.Infrastructure \
  --startup-project server/src/TempoDesk.Infrastructure \
  --context TempoDeskDbContext \
  --context-dir Persistence/Generated \
  --output-dir Persistence/Generated/Entities \
  --namespace TempoDesk.Infrastructure.Persistence.Generated.Entities \
  --context-namespace TempoDesk.Infrastructure.Persistence.Generated \
  --no-onconfiguring --force --table public.users --table public.workspaces
```

Khi chạy lệnh trực tiếp, đặt `PGPASSWORD` trước. `--force` ghi đè các file
generated trùng tên: không viết business logic trong đó. Dùng partial class,
mapper hoặc repository nằm ngoài `Generated`. Khi xóa bảng/đổi tên entity,
review và xóa file generated cũ không còn được dùng vì EF không tự xóa chúng.
Không dùng `dotnet ef migrations add` hoặc `database update` cho schema SQL-first.

Tham khảo [EF CLI](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)
và [reverse engineering](https://learn.microsoft.com/en-us/ef/core/managing-schemas/scaffolding/).

## Tổ chức SQL

- `migrations/`: SQL versioned chạy theo thứ tự tên; không sửa file đã áp dụng.
- `functions/`: định nghĩa hàm SQL để tham khảo/tái sử dụng; thay đổi phải được
  đưa vào migration, runner không tự quét thư mục này.
- `views/`: định nghĩa view; thay đổi cũng phải nằm trong migration.
- `seeds/`: dữ liệu tham chiếu/dev được kiểm soát; không chứa dữ liệu cá nhân
  hoặc bí mật. Không tự chạy seed dev trên production.

Mỗi migration cần mô tả phương án hoàn tác an toàn trong comment hoặc tài liệu
thay đổi. Không rollback migration đã commit bằng cách xóa hàng trong lịch sử;
thêm migration sửa tiếp và đánh giá nguy cơ mất dữ liệu.

## Kiểm tra tự động

```bash
bash database/tests/migrator.sh
```

Script dùng database tạm và bản sao migration, kiểm tra database mới, quyền,
constraint, APPLY, SKIP, checksum, rollback và scaffold EF trên bảng thử.
Model được sinh trong bản sao source tạm rồi build; không ghi đè code generated
trong workspace của bạn. Database và file tạm được xóa khi
script kết thúc; database `tempodesk` không bị thay đổi.
Không dùng `docker compose down -v` để chạy lại kiểm tra vì lệnh đó xóa dữ liệu local.

Workflow `.github/workflows/foundation.yml` chạy build/test backend và cùng script
trên pull request/push, dùng database CI với mật khẩu thử, không cần repository secret.
Đây là CI cho SCRUM-5/7; chưa thay thế toàn bộ yêu cầu frontend/security của SCRUM-16.

## Chạy trực tiếp bằng .NET (tùy chọn)

Runner không tự đọc `.env`. Đặt `TEMPODESK_MIGRATION_PASSWORD` trong môi trường
terminal; có thể ghi đè host/port/database bằng `TEMPODESK_DB_HOST`,
`TEMPODESK_DB_PORT`, `TEMPODESK_DB_NAME`. Mặc định là `localhost:5432/tempodesk`.
Chạy từ thư mục gốc repo:

```bash
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
```

Lệnh Compose không cần các biến terminal này và hoạt động trên Docker Linux
containers của macOS, Windows hoặc Linux. Script kiểm tra Bash cần Bash/WSL/Git Bash.
