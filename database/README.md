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

Không chạy `init.sh` hoặc file tạo `schema_history` thủ công trên database mới.
`init.sh` được PostgreSQL chạy khi volume rỗng; migrator tự tạo bảng lịch sử.
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

## Kiểm tra tự động

```bash
bash database/tests/migrator.sh
```

Script dùng database tạm và bản sao migration, kiểm tra database mới, quyền,
constraint, APPLY, SKIP, checksum và rollback. Database và file tạm được xóa khi
script kết thúc; database `tempodesk` không bị thay đổi.
Không dùng `docker compose down -v` để chạy lại kiểm tra vì lệnh đó xóa dữ liệu local.

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
