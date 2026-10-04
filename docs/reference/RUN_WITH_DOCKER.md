# Chạy API AgriSage bằng Docker

Dành cho người **không cài .NET** (ví dụ đội FE). Không cần SDK, chỉ cần Docker. Image build từ `Dockerfile` ở thư mục gốc repo; mật khẩu và khoá **không** nằm trong
image hay trong git, mà truyền vào lúc chạy qua file `.env`.

## Cần có

- **Docker Desktop** (Windows/Mac) hoặc Docker Engine (Linux), đang chạy. Kiểm tra: `docker version` hiện cả *Client* và *Server*.
- Mã nguồn backend (clone/pull nhánh có `Dockerfile`), hoặc chỉ cần các file `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example` và thư mục `src/`.
- **Mật khẩu database dev** do người quản lý backend gửi **riêng** cho bạn (không đưa lên git, không gửi vào nhóm chat chung).

## Các bước

1. Mở terminal ở **thư mục gốc repo** (nơi có `docker-compose.yml`).
2. Tạo file cấu hình từ mẫu:
   ```bash
   copy .env.example .env        # Windows (PowerShell/cmd)
   cp .env.example .env          # Mac/Linux
   ```
3. Mở `.env` và điền:
   | Biến | Điền gì |
   |---|---|
   | `Database__Password` | mật khẩu database dev được gửi riêng |
   | `Jwt__SigningKey` | chuỗi ngẫu nhiên dài **ít nhất 32 ký tự**, tự đặt, chỉ dùng cho container của bạn |
   | `Storage__SecretKey` | để trống nếu chưa upload ảnh (luồng bán hàng không cần) |
   `.env` đã bị git bỏ qua; **không commit** và **không dán nội dung** vào chat hay vào AI agent.
4. Build và chạy:
   ```bash
   docker compose up --build
   ```
   Lần đầu mất vài phút (tải image nền và khôi phục gói NuGet), các lần sau nhanh nhờ cache. Thêm `-d` nếu muốn chạy nền.
5. Kiểm tra:
   - Tài liệu API (Swagger): <http://localhost:5206/swagger>
   - Thử nhanh: <http://localhost:5206/api/catalog/categories> phải trả `200` (nghĩa là container đã nối được database).
6. FE gọi API: `VITE_API_URL=http://localhost:5206`. Web chạy ở `http://localhost:5173` hoặc `http://localhost:3000` đã được phép (CORS). Origin khác:
   thêm `Cors__AllowedOrigins__0=http://localhost:4173` vào `.env` rồi chạy lại.
7. Dừng: `Ctrl+C` (nếu chạy nổi) hoặc `docker compose down`.

## Các lệnh hay dùng

| Việc | Lệnh |
|---|---|
| Xem log | `docker compose logs -f api` |
| Chạy lại sau khi sửa `.env` | `docker compose up -d --force-recreate` |
| Cập nhật khi backend có bản mới | `git pull` rồi `docker compose up --build -d` |
| Dừng và xoá container | `docker compose down` |
| Xoá cả image đã build | `docker rmi agrisage-api:local` |

## Chạy không dùng compose

```bash
docker build -t agrisage-api .
docker run --rm -p 5206:8080 --env-file .env -e ASPNETCORE_ENVIRONMENT=Development agrisage-api
```

## Gặp lỗi

| Hiện tượng | Nguyên nhân thường gặp |
|---|---|
| `failed to connect to the docker API` | Docker Desktop chưa chạy: mở Docker Desktop, đợi biểu tượng báo sẵn sàng |
| Container tự thoát ngay, log báo `Jwt:SigningKey` | `Jwt__SigningKey` trống hoặc ngắn hơn 32 ký tự |
| `/api/catalog/categories` trả `500` hoặc log báo lỗi đăng nhập database | `Database__Password` sai hoặc trống |
| Cổng 5206 đã bị dùng | tắt chương trình đang dùng cổng đó, hoặc đổi `"5206:8080"` trong `docker-compose.yml` thành `"5300:8080"` rồi gọi `http://localhost:5300` |
| Trình duyệt báo lỗi CORS | origin của web chưa nằm trong danh sách; thêm vào `.env` như bước 6 |
| Swagger không mở được | container đang chạy ở chế độ Production; dùng `docker compose` (đã đặt Development) hoặc thêm `-e ASPNETCORE_ENVIRONMENT=Development` |

## Ghi chú

- Image chạy bằng người dùng không phải root và **không chứa** `appsettings.Local.json`, `.env` hay bí mật nào (đã loại bằng `.dockerignore` và file dự án).
- Trong container API nghe **HTTP** cổng 8080 (map ra 5206). Không có HTTPS trong container; muốn HTTPS thì đặt reverse proxy phía trước khi triển khai thật.
- Container dùng **database dev dùng chung**: không xoá hay sửa dữ liệu của người khác. Migration và dọn dữ liệu cần người quản lý backend đồng ý.
- Triển khai thật: truyền các biến môi trường (`Database__*`, `Jwt__SigningKey`, `Cors__AllowedOrigins__N`, …) bằng cơ chế bí mật của nơi chạy, để `ASPNETCORE_ENVIRONMENT=Production`.
