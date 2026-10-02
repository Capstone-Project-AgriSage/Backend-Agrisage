# Yêu cầu: Seed dữ liệu tham chiếu (Reference / System Data)

> Tài liệu giao việc cho người / agent khác. Chỉ làm đúng phạm vi bên dưới.
> Tiếp nối AGRI-14 (InitialCreate đã áp dụng lên Supabase `agrisage-dev`, 67 bảng, hiện **0 dòng dữ liệu**).

## 1. Mục tiêu

Thêm **dữ liệu tham chiếu tối thiểu** để hệ thống có thể bắt đầu chạy nghiệp vụ:

| Nhóm | Bảng | Số dòng |
|---|---|---|
| Vai trò | `roles` | 5 |
| Đơn vị tính | `units` | danh sách ở mục 4.2 |
| Lớp bệnh lúa (AI) | `diseases` | 5 |
| Cửa hàng vận hành | `stores` | 1 |

Nguồn quy định: `docs/reference/BACKEND_CODING_RULES.md` #55 ("Seed Only Reference/System Data"),
`DATABASE_DESIGN.md` §XXXIII bước 7 và §55 (`diseases`), `BACKEND_ARCHITECTURE.md` §11.2 (thư mục `Seed/`).

## 2. Ngoài phạm vi (KHÔNG làm)

- Không seed dữ liệu nghiệp vụ / giao dịch giả: sản phẩm, giá, phiếu nhập, tồn kho, đơn hàng, công nợ, thanh toán, chẩn đoán…
- Không tạo user, tài khoản admin, farmer, `store_members` (cần password hash, thuộc task Auth).
- Không seed `ai_models`, `ai_policy_configs`, `disease_treatments`, `credit_tiers`, `customer_groups`, `price_lists`.
- Không thêm/sửa cột, bảng, index, CHECK; **không tạo migration mới**; không đổi `DATABASE_DESIGN.md`.
- Không sửa Domain hay cấu hình EF, trừ khi phát hiện lỗi thật (khi đó dừng và báo, xem mục 8).
- Không chạy SQL thô (`INSERT ...`) trong DBeaver hay `ExecuteSql`. Không dùng `ExecuteUpdate/ExecuteDelete`.
- Không hard-code Store ID ở bất kỳ đâu.

## 3. Đọc trước khi làm (chỉ các phần cần thiết)

1. `AGENTS.md`, `.claude/rules/*`.
2. `docs/reference/DATABASE_DESIGN.md`: §1 (`roles`), `units` (bảng 12), §55 (`diseases`), bảng `stores`, §35.15.
3. `docs/reference/BACKEND_CODING_RULES.md` #16, #24, #55, #64.
4. `docs/agent/context/DECISIONS.md` (hàng "Persistence bookkeeping", "Database configuration").

## 4. Dữ liệu cần seed

### 4.1 Roles (5)

Dùng constructor `new Role(RoleCode, name, description)`. `RoleCode` lưu ở DB là chuỗi UPPER_SNAKE.

| RoleCode | Giá trị DB | name gợi ý |
|---|---|---|
| Farmer | `FARMER` | Farmer |
| StoreOwner | `STORE_OWNER` | Store Owner |
| SalesStaff | `SALES_STAFF` | Sales Staff |
| DeliveryStaff | `DELIVERY_STAFF` | Delivery Staff |
| Admin | `ADMIN` | Admin |

`name` có thể đổi sang tiếng Việt nếu team muốn. Cần xác nhận (mục 7).
AI reviewer là quyền `can_review_ai`, **không** phải role riêng (không tạo role AI reviewer).

### 4.2 Units

Dùng `new Unit(code, name, symbol)`. Danh sách dưới đây là **đề xuất, cần team xác nhận** (mục 7) trước khi seed:

| code | name | symbol |
|---|---|---|
| BOTTLE | Chai | chai |
| BOX | Hộp | hộp |
| CARTON | Thùng | thùng |
| BAG | Bao | bao |
| PACK | Gói | gói |
| KG | Kilogram | kg |
| GRAM | Gram | g |
| LITER | Lít | l |
| ML | Mililit | ml |

Đơn vị cơ sở của tồn kho do `product_packagings` quyết định (`is_base_unit`); seed chỉ tạo danh mục đơn vị.

### 4.3 Diseases (5) — sản xuất AI chỉ gồm rice

Dùng `new Disease(code, name, isHealthyClass, cropType: "RICE", ...)`. Mã theo `DATABASE_DESIGN.md` §55:

| code | name | is_healthy_class |
|---|---|---|
| `LEAF_BLAST` | Leaf Blast | false |
| `BACTERIAL_LEAF_BLIGHT` | Bacterial Leaf Blight | false |
| `BROWN_SPOT` | Brown Spot | false |
| `SHEATH_BLIGHT` | Sheath Blight | false |
| `HEALTHY` | Healthy | **true** (đúng một dòng) |

- Các cột `description`, `symptoms`, `causes`, `prevention`, `scientific_name`: **để null**. Nội dung tri thức do người có chuyên môn cung cấp sau; không tự bịa nội dung nông nghiệp.
- Không thêm lớp khác (Cucumber/Anthracnose không thuộc phạm vi production).

### 4.4 Store (1)

Chỉ **một** cửa hàng vận hành. Dùng `new Store(code, name, addressLine, province, ...)`.
Thông tin thật (mã, tên, địa chỉ, tỉnh, SĐT…) **chưa có** — cần team cung cấp (mục 7).
Đọc từ cấu hình không nhạy cảm (mục 5.4), không hard-code trong code và không hard-code Store ID.

## 5. Yêu cầu kỹ thuật

### 5.1 Vị trí và kiến trúc
- Đặt trong `src/AgriSage.Infrastructure/Persistence/Seed/`: `RoleSeeder`, `UnitSeeder`, `DiseaseSeeder`, `StoreSeeder`, cùng một điểm điều phối (ví dụ `DatabaseSeeder`).
- Infrastructure không sở hữu business policy: seed chỉ tạo bản ghi tham chiếu, không chứa logic nghiệp vụ.
- Không thêm phụ thuộc ngược giữa các project; không thêm package mới.

### 5.2 Cách ghi dữ liệu
- Tạo entity **bằng constructor Domain** (qua validation), thêm vào `AgriSageDbContext`, gọi `SaveChangesAsync`. Interceptor tự gán `created_at`/`updated_at`; không gán tay.
- Không có user hiện tại nên `deleted_by` không liên quan; không tạo "system user" giả.
- Tất cả trong **một transaction** (dùng `BeginTransactionAsync`); lỗi thì rollback, không để seed dở dang. Truyền `CancellationToken`.

### 5.3 Idempotent (chạy lại nhiều lần vẫn đúng)
- Mỗi bản ghi kiểm tra tồn tại theo **mã tự nhiên** rồi mới thêm: `roles.code`, `units.code`, `diseases.code`, `stores.code`.
- Kiểm tra phải dùng `IgnoreQueryFilters()` (dòng đã soft delete vẫn tính). Lý do: `units.code`, `diseases.code`, `stores.code` có unique index **không lọc `deleted_at`**, nên dòng đã xoá mềm vẫn chặn thêm mới. Nếu gặp dòng đã xoá mềm thì không tự khôi phục hay ghi đè, mà báo lỗi rõ ràng.
- Bản ghi đã tồn tại thì **không ghi đè** giá trị (tránh phá dữ liệu do người dùng sửa sau này).
- Chạy lần 2 phải cho 0 dòng mới và không lỗi.

### 5.4 Cấu hình
- Thông tin Store đặt ở mục `Seed:Store` trong `appsettings.Development.json` (không nhạy cảm, được commit). **Không đặt bí mật nào** vào appsettings (đã có test `CommittedConfigurationTests` chặn).
- Danh sách roles/units/diseases là hằng số trong code (cố định theo thiết kế).

### 5.5 Kích hoạt seed
- Seed **không** chạy tự động khi app khởi động và không nằm trong migration.
- Cần một cách kích hoạt tường minh, ví dụ tham số dòng lệnh `--seed` của Api hoặc một command riêng; người thực hiện đề xuất phương án nhỏ gọn nhất, không ảnh hưởng luồng HTTP, rồi mô tả trong README.
- Không thêm endpoint HTTP để seed.

### 5.6 Kết nối
- Cấu hình mặc định đã dùng Supabase Session Pooler; password lấy từ `Database:Password` (User Secrets hoặc `Database__Password`). Không in mật khẩu / connection string ra log hay báo cáo.

## 6. Kiểm thử

Theo mẫu `tests/AgriSage.IntegrationTests/Infrastructure/Persistence/RealDb.cs`:

1. **Test trên PostgreSQL thật** (opt-in `AGRISAGE_DB_TESTS=1`, dùng `[RealDbFact]`), mỗi test trong transaction luôn **rollback**:
   - seed tạo đúng số dòng: 5 roles, N units, 5 diseases, 1 store;
   - chạy seed lần 2 → không thêm dòng, không lỗi;
   - đúng một disease `is_healthy_class = true`; các mã disease/role đúng danh sách;
   - dòng đã xoá mềm với cùng mã → báo lỗi, không ghi đè.
2. **Test offline** (không cần DB): danh sách hằng số đúng (5 role, 5 disease, mã không trùng, mã ≤ độ dài cột).
3. Không để lại dữ liệu test trong `agrisage-dev`.

## 7. Cần team xác nhận TRƯỚC khi seed thật

1. Danh sách units cuối cùng (mục 4.2).
2. Tên hiển thị của roles (tiếng Anh hay tiếng Việt).
3. Thông tin Store: `code`, `name`, `address_line`, `province` (bắt buộc); `ward`, `district`, `phone_number`, `email`, `tax_code` (tuỳ chọn).
4. Cách kích hoạt seed (`--seed` hay command riêng).

Nếu chưa có, người thực hiện có thể làm code + test trước, nhưng **chưa chạy seed lên `agrisage-dev`** cho đến khi có xác nhận.

## 8. Quy tắc dừng và báo

Dừng và hỏi thay vì tự quyết nếu:
- cần đổi schema, Domain, cấu hình EF, hoặc thêm migration;
- design mâu thuẫn với code (báo, đừng tự chọn);
- cần thêm dữ liệu ngoài 4 nhóm ở mục 1.

## 9. Tiêu chí hoàn thành

- [ ] 4 seeder + điểm điều phối, đúng kiến trúc, không thêm package, không migration mới.
- [ ] Idempotent, một transaction, có `CancellationToken`, không ghi đè.
- [ ] Không có dữ liệu nghiệp vụ giả, không user, không Store ID hard-code, không bí mật bị commit.
- [ ] Test offline + test PostgreSQL thật (rollback) pass; `dotnet build` 0 warning/0 lỗi; toàn bộ test hiện có không hồi quy (hiện 233 unit + 79 integration, 13 test thật bị bỏ qua khi không bật cờ).
- [ ] README ghi cách chạy seed; `CURRENT_STATE.md` và `DECISIONS.md` cập nhật.
- [ ] Sau khi team duyệt: chạy seed lên `agrisage-dev`, đối chiếu số dòng bằng truy vấn chỉ đọc, chạy lần 2 xác nhận idempotent.
- [ ] Báo cáo: file đã tạo/sửa, số dòng mỗi bảng, kết quả build/test, mọi sai khác so với tài liệu này.
