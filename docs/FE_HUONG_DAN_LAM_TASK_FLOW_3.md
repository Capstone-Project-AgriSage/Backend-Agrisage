# Hướng dẫn FE: dựng BE trên máy và làm các task công nợ theo vụ mùa (Luồng 3)

Dành cho: người làm FE (Management-Webs, Farmer-Web — React; Flutter). Soạn 2026-11-04.
Đi kèm: [FE_GUIDE_FLOW_3.md](FE_GUIDE_FLOW_3.md) (hợp đồng API, lỗi, kiểu TypeScript), [agent/requests/FE_TASKS_FLOW_3.md](agent/requests/FE_TASKS_FLOW_3.md) (danh sách task và ưu tiên), [F3_SEASON_SWAGGER_TEST.md](F3_SEASON_SWAGGER_TEST.md) (thử tay bằng Swagger).

> **Tình trạng BE (đọc trước).** Các API này **chưa vào `main`** và **chưa chạy trên DB dùng chung**: nhánh `feature/f3-21-debt-permission-lock` đang chờ review của Lead (L1/L2) và duyệt 4 migration. Vì vậy FE nối vào **BE chạy trên máy bạn, với PostgreSQL cục bộ** (mục 2). Hợp đồng đã ổn định nhưng **có thể đổi sau review**; nếu đổi sẽ báo trong PR. **Không** trỏ BE này vào Supabase / DB dùng chung.

## 1. Cần cài

- .NET 10 SDK, PostgreSQL 16 (có `psql`), công cụ `dotnet-ef` (`dotnet tool install --global dotnet-ef` nếu chưa có), Git.
- Lấy nhánh: `git fetch origin` rồi `git checkout feature/f3-21-debt-permission-lock` (hoặc đọc PR nháp của nhánh này).

## 2. Dựng BE cục bộ (làm một lần, khoảng 10 phút)

Các lệnh dưới là PowerShell, chạy ở thư mục gốc repo. Thay `<MẬT_KHẨU_PG>` bằng mật khẩu user `postgres` **trên máy bạn**. **Không dán mật khẩu thật vào chat hay commit.**

```powershell
# 1) Tạo DB trống và áp toàn bộ migration (16 migration, gồm 4 migration của luồng này)
$env:PGPASSWORD = "<MẬT_KHẨU_PG>"
psql -h localhost -U postgres -d postgres -c "CREATE DATABASE agrisage_local"
dotnet ef migrations script --project src/AgriSage.Infrastructure --startup-project src/AgriSage.Api --output "$env:TEMP\agrisage-migrations.sql"
psql -h localhost -U postgres -d agrisage_local -v ON_ERROR_STOP=1 -q -f "$env:TEMP\agrisage-migrations.sql"

# 2) Biến môi trường dùng cho MỌI lệnh dotnet bên dưới (chạy lại trong mỗi cửa sổ PowerShell mới)
$env:ASPNETCORE_ENVIRONMENT = "Development"          # cần để có Swagger
$env:LocalSettings__Disabled = "true"                 # BỎ QUA appsettings.Local.json của bạn (có thể chứa khóa/mật khẩu DB dùng chung)
$env:Database__Host = "localhost"; $env:Database__Port = "5432"; $env:Database__Database = "agrisage_local"
$env:Database__Username = "postgres"; $env:Database__Password = "<MẬT_KHẨU_PG>"; $env:Database__SslMode = "Disable"
$env:Jwt__SigningKey = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
$env:Cors__AllowedOrigins__0 = "http://localhost:5173"    # origin của FE đang chạy; thêm __1, __2… cho app khác
$env:Seed__Store__Code = "DEMO"; $env:Seed__Store__Name = "Cua hang demo"; $env:Seed__Store__AddressLine = "1 Demo Street"; $env:Seed__Store__Province = "Demo"
$env:DebtDemo__Password = "<MẬT_KHẨU_DEMO>"             # tự đặt, ít nhất 8 ký tự; dùng để đăng nhập các tài khoản demo

# 3) Dữ liệu tham chiếu rồi dữ liệu demo công nợ
dotnet run --project src/AgriSage.Api --no-launch-profile -- --seed
dotnet run --project src/AgriSage.Api --no-launch-profile -- --seed-debt-demo
```

Kết quả mong đợi: `Reference seed added roles=5, units=9, diseases=5, stores=1` rồi `Debt demo data added: users=2, seasons=2, customers=4, debts=4`. Chạy `--seed-debt-demo` lần nữa → `already exists; nothing changed`.

**An toàn — đọc kỹ.**
- `appsettings.Development.json` trong repo trỏ về DB dùng chung. Biến `Database__*` ở bước 2 ghi đè nó; **luôn kiểm tra `Database__Host = localhost` trước khi chạy**.
- `appsettings.Local.json` (nếu bạn có) được đọc **sau** biến môi trường và đè lên chúng; vì vậy cần `LocalSettings__Disabled=true`. Thiếu dòng này BE sẽ dùng mật khẩu trong file đó (đăng nhập DB cục bộ sẽ lỗi `password authentication failed`, hoặc tệ hơn là nối nhầm DB khác).
- Lệnh `--seed-debt-demo` **tự từ chối** nếu `Database__Host` không phải localhost/127.0.0.1/::1 hoặc môi trường là Production.

Muốn dựng lại từ đầu: `DROP DATABASE agrisage_local` rồi làm lại bước 1 và 3.

## 3. Chạy API và đăng nhập

```powershell
# trong cửa sổ đã đặt biến ở bước 2:
dotnet run --project src/AgriSage.Api --no-launch-profile --urls http://localhost:5299
```

- API: `http://localhost:5299` · Swagger: `http://localhost:5299/swagger` (chỉ có ở môi trường Development).
- Nếu trình duyệt báo CORS: thêm origin của FE vào `Cors__AllowedOrigins__N` rồi chạy lại API. (Đã thử: preflight từ `http://localhost:5173` trả `204` và đúng `Access-Control-Allow-Origin`.)
- Đăng nhập: `POST /api/auth/login` với `{ "identifier": "<email>", "password": "<MẬT_KHẨU_DEMO>" }`. Phản hồi có `accessToken`, `expiresAt`, `user`, `refreshToken`, `refreshExpiresAt`, `sessionId`. Gửi `Authorization: Bearer <accessToken>` cho mọi API sau đó; làm mới bằng `POST /api/auth/refresh`.

| Tài khoản (email) | Vai trò | Dùng để thử |
|---|---|---|
| `demo.owner@agrisage.demo` | Chủ cửa hàng | mọi màn quản lý, kể cả các nút chỉ-Chủ |
| `demo.staff@agrisage.demo` | Nhân viên bán hàng | kiểm tra các nút chỉ-Chủ bị ẩn / trả `403` |
| `demo-0001@agrisage.demo` | Nông dân (nợ bình thường) | Farmer-Web: nợ chưa đến hạn |
| `demo-0002@agrisage.demo` | Nông dân (nợ quá hạn) | nợ quá hạn |
| `demo-0003@agrisage.demo` | Nông dân (đang khiếu nại) | khoản khiếu nại |
| `demo-0004@agrisage.demo` | Nông dân (đã gia hạn A→B) | lịch sử gia hạn |

Mật khẩu tất cả là `<MẬT_KHẨU_DEMO>` bạn đặt ở bước 2.

## 4. Dữ liệu demo có gì (để biết kết quả đúng khi thử)

- Hai vụ: `DEMO-A` (đang thu, **đã quá hạn trả**) và `DEMO-B` (đang nhận đơn). Bốn khách, mỗi khách một khoản: 3.000.000 (vụ B, chưa đến hạn), 5.000.000 (vụ A, quá hạn), 2.000.000 (vụ A, đang khiếu nại), 4.000.000 (phát sinh vụ A, đã gia hạn sang vụ B).
- Kết quả chuẩn khi gọi bằng tài khoản Chủ (đã kiểm): `GET /api/crop-seasons` → `DEMO-B/OPEN`, `DEMO-A/COLLECTING`; `GET /api/debt-entries/dashboard` → tổng nợ `14000000`, nợ quá hạn `7000000`, `needsDecisionCount = 1`; `GET /api/debt-policy` → `isDefault: true`, `remindersStartDate: null`; `POST /api/debt-reminders/initialize` với `{}` (xem thử) → `dryRun: true`, `activeGroups: 3`, `skippedTotal: 1`; `GET /api/debt-entries?needsDecision=true` → 1 khoản.
- Nhắc nợ **chưa bắt đầu** và không có thông báo nào được gửi. Muốn thử tin nhắc nợ, Chủ gọi `POST /api/debt-reminders/initialize` với `{"dryRun": false}` (đặt một lần; dựng lại DB để thử lại).
- Đơn hàng, kho, bảng giá **không có** trong bộ demo. Màn "kiểm tra tín dụng khi tạo đơn chịu" và chi tiết đơn (task M9) cần tạo thêm đơn bằng API/Swagger, hoặc hỏi owner L3.

## 5. Cách làm từng task (theo thứ tự)

Chi tiết màn hình, route, kiểu dữ liệu, lỗi: xem `FE_GUIDE_FLOW_3.md` (số mục ghi bên dưới là mục của file đó). Mỗi task đều làm theo vòng: mở Swagger xem phản hồi thật → viết lớp gọi API/kiểu dữ liệu → dựng màn → thử với từng tài khoản demo → đối chiếu kết quả chuẩn ở mục 4.

**Bước 0 — chuẩn bị (0,5 ngày):** dựng BE cục bộ (mục 2–3), đăng nhập bằng Swagger, đọc `FE_GUIDE_FLOW_3.md` mục 0–2.

**Bước 1 — ưu tiên P0 (làm trước, độc lập nhau):**
- **M1 `RolesPage`** (Management-Webs): FE `main` đã lọc theo `allowedRoles`, nên chỉ cần **xác nhận bằng một lần thử trình duyệt** sau khi BE mới chạy: lưu vai trò Sale không báo `403/422`, mã chỉ-Chủ hiển thị khóa/ẩn. Chỉ sửa nếu lần thử này lỗi.
- **M2 ẩn nút chỉ-Chủ với nhân viên:** giảm/xóa nợ, ghi nợ thủ công, đổi hạn (trước đây nhân viên được, nay chỉ Chủ), xác nhận/từ chối chuyển khoản trả nợ, tạm ngưng/khóa/mở lại tín dụng, đổi `allowCreditPurchase`. *Thử:* đăng nhập `demo.staff@…` không thấy các nút; gọi API trực tiếp bằng tài khoản này nhận `403`.
- **F1 (Flutter) kiểm tra mã hiện có:** liệt kê màn/hàm gọi `adjust`, `cancel`, `change-due-date`, `credit/activate|suspend|block`, xác nhận/từ chối chuyển khoản trả nợ, `manual-entries`, kèm vai trò người dùng của màn đó. Gửi bảng cho owner L3 (trả lời câu C: có ứng dụng/cửa hàng thật đang dùng không). Nếu app không có thì ghi rõ "không có".

**Bước 2 — nền (P1):**
- **M3 vụ mùa** (mục 4.1): danh sách, tạo, sửa. Vụ đầu nhập `orderWindowStart`, vụ sau server tự điền. Lưu với "áp hạn mới cho các khoản đang thu" cần `reason`; sau khi lưu hiển thị `debtExtension.moved` và `debtExtension.skippedDisputed`, và **chịu được `debtExtension = null`** (mọi lần đọc thường và khi không chọn tùy chọn). Thử: sửa `DEMO-A` đổi ân hạn, bật tùy chọn → khoản khiếu nại phải báo là "giữ hạn cũ".
- **M4 chính sách + Bắt đầu nhắc nợ** (mục 4.2): form chính sách (luật: `ownerReviewAfterRollovers ≥ max(staffRolloverLimit, 1)`); cảnh báo "Nhắc nợ chưa bắt đầu" khi `remindersStartDate = null`; nút (chỉ Chủ): gọi `dryRun: true` → hộp xác nhận (`startDate`, `skippedTotal`, `skipped.N1…N5`, `groupsWithLaterMilestones`) → `dryRun: false`. Ẩn nút khi đã có `remindersStartDate`. Ghi rõ "đặt một lần, không đổi được".
- **M5 danh sách + chi tiết khoản nợ** (mục 4.3): cột vụ phát sinh/đang thu, hạn gốc, số lần gia hạn, trạng thái hạn (`dueState`), bộ lọc, khối lịch sử hạn (hành động cũ không có mã lý do hiển thị "Trước khi áp dụng chính sách").

**Bước 3 — thao tác (P2):** M6 gia hạn một khoản / cả vụ (cả vụ: **xem trước rồi xác nhận**, ghi chú rằng xem trước có thể khác kết quả thật); M7 "Cần quyết định" + bảng điều khiển; M8 đóng vụ, báo cáo nợ theo vụ, gợi ý hạn mức; M9 tạo đơn chịu (hạn dự kiến). **Farmer-Web:** W1 nợ của tôi, W2 khoản nợ + lịch sử hạn, W3 hạn dự kiến + khiếu nại + thông báo.

**Làm khung, chưa hoàn tất (chờ hợp đồng):**
- **M10 form mã lý do:** làm trường chọn mã **tùy chọn**, danh sách mã lấy từ **một hằng/cấu hình duy nhất**. **Không** đặt bắt buộc, **không** tự thêm/bớt mã — Lead chưa duyệt danh sách (7 mã giảm/xóa/đổi hạn, 4 mã gia hạn) và nhóm chưa trả lời có bắt buộc không.
- **M11 biên nhận theo vụ:** BE chưa làm; ẩn đi.

## 6. Quy tắc khi làm

1. Hợp đồng lấy từ `FE_GUIDE_FLOW_3.md` và phản hồi thật trên Swagger. Thấy lệch → ghi lại (route, request, phản hồi nhận được, điều mong đợi) và báo owner L3; **đừng sửa vòng quanh ở FE**.
2. Tiền là số thập phân (`number`), hiển thị định dạng `vi-VN`; ngày trả là `YYYY-MM-DD` (giờ Việt Nam).
3. Lỗi hiển thị theo `FE_GUIDE_FLOW_3.md` mục 5 (400 sai đầu vào, 403 không đủ quyền, 409 dữ liệu đã đổi → tải lại, 422 vi phạm quy tắc nghiệp vụ).
4. Nút "Xin gia hạn" của nông dân **không có** ở giai đoạn này. Nợ quá hạn **không** chặn mua chịu (chỉ báo `hasOverdueDebt`); bỏ mọi nhãn cũ nói ngược lại.
5. Mọi thay đổi trạng thái đều do server tính; FE không tự tính hạn, vụ, trạng thái.

## 7. Báo cáo / hỏi

Ghi vào bình luận PR của nhánh hoặc gửi owner L3. Nêu: task (mã M/W/F), route, request đã gửi, phản hồi nhận được, điều mong đợi. Vấn đề về mã lý do, bắt buộc mã lý do, quy tắc gợi ý hạn mức, biên nhận theo vụ: **ghi nhận là "chờ hợp đồng"**, chưa phải lỗi.
