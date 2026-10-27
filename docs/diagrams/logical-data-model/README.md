# AgriSage — Logical Data Model

> **Bản hiện tại theo yêu cầu: tất cả trên một sơ đồ:** [Logical Data Model Mermaid](MERMAID_LOGICAL_DATA_MODEL_2026-10-26.md).
> Giữ đủ 74 thực thể, 105 quan hệ và các cụm nghiệp vụ trên cùng một canvas.
> Đã bỏ nền/khung/tên nhóm; tất cả bảng dùng nền trắng, viền và tiêu đề xanh thống nhất.
> [Xem sơ đồ SVG](single-diagram/agrisage-single.svg) · [Mã Mermaid](single-diagram/agrisage-single.mmd).
> [Mở bản Draw.io giữ đúng bố cục](single-diagram/agrisage-fixed-layout.drawio) · [PNG độ phân giải cao](single-diagram/agrisage-single.png).
> [Bản Draw.io đã kiểm tra XML](single-diagram/agrisage-fixed-layout-validated.drawio).
> [Mã Mermaid rút gọn để dán nhanh](single-diagram/agrisage-single-compact.mmd).
> [Danh sách 56 quan hệ đã ẩn](simplified/RELATIONSHIP_REVIEW.md) giải thích từng trường hợp; không thay đổi FK hay database.
> `index.html`, SVG và DBML bên dưới vẫn là bộ tra cứu chi tiết trước khi giản lược.

Ngày đối chiếu: **20/10/2026**. Phạm vi: **74 thực thể, 1.079 thuộc tính, 278 quan hệ FK**.

## Cách dùng

- Mở `index.html` để xem sơ đồ tổng thể hoặc từng nhóm và tra cứu thuộc tính/quan hệ.
- `agrisage-logical-model.svg`: sơ đồ tổng thể; các file `01-…svg` đến `11-…svg`: sơ đồ chi tiết theo nhóm.
- `agrisage-logical-model.dbml`: mô hình đầy đủ để nhập vào công cụ hỗ trợ DBML. Không tự tạo schema từ tài liệu này.
- Phần danh mục phía dưới chứa đủ trường audit, tất cả quan hệ FK, điều kiện duy nhất và CHECK.

## Nguồn và phạm vi đối chiếu

1. `docs/reference/DATABASE_DESIGN.md`: thiết kế nghiệp vụ gốc và các bổ sung, đặc biệt §35 và §36.
2. Domain entities, EF configurations và `AgriSageDbContext`: metadata hiện tại, xuất từ EF Core design-time model mà không kết nối database.
3. `AgriSageDbContextModelSnapshot.cs`, các migration đã review và `PostgreSqlRawIndexes.cs`: đối chiếu hiện trạng triển khai và các khóa biểu thức.
4. `docs/reference/api-flows/AUTH_NOTIFICATIONS_OPERATIONS.md`: phần mở rộng xác thực/thông báo.

Ảnh người dùng cung cấp là mô hình conceptual, dùng để đối chiếu nhóm nghiệp vụ. Mô hình logical bổ sung thuộc tính, tính bắt buộc, khóa và các bảng chi tiết/bảng nối thực sự có trong project.

**Lưu ý phiên bản tài liệu:** mốc 67 bảng trong hướng dẫn và một số chú thích là baseline lịch sử. §36 của thiết kế xác nhận 74 bảng. Có 4 bảng bổ sung `auth_sessions`, `refresh_tokens`, `auth_challenges`, `notification_outbox` và 3 bảng `permissions`, `role_permissions`, `store_member_permissions`. Không có bằng chứng trong lần đối chiếu này về trạng thái triển khai database live; số liệu là mô hình local và migration snapshot.

## Quy ước logical

- `PK`: khóa chính; `FK`: khóa ngoại; `UQ`: khóa duy nhất không điều kiện trên một thuộc tính. Khóa duy nhất ghép/điều kiện nằm dưới từng thực thể.
- `Identifier`: định danh UUID; `String(n)`: chuỗi tối đa n ký tự; `Integer64`: số nguyên 64 bit; `Decimal(p,s)`: số thập phân; `DateTime`: thời điểm có múi giờ; `JSON`: dữ liệu có cấu trúc.
- `NULL = Có`: thuộc tính có thể vắng mặt. `NULL = Không`: bắt buộc. Logical type giữ độ dài/độ chính xác có ý nghĩa nghiệp vụ, không mô tả DDL, storage engine hay kế hoạch index.
- `A (1) → (0..N) B`: mỗi B bắt buộc thuộc đúng một A; A có thể chưa có B hoặc có nhiều B. `A (0..1) → (0..N) B`: FK trên B là tùy chọn. `0..1` phía B được suy từ FK/unique index không điều kiện.
- Không tự suy quan hệ 1–1 từ tên Profile/Account: phải có ràng buộc duy nhất. Các unique index có filter chỉ đúng trong tập bản ghi thỏa filter, không biến toàn bộ lịch sử thành 1–1.
- Sơ đồ lược các trường dùng chung `created_at`, `updated_at`, `deleted_at`, `deleted_by` nếu có; lược đường FK ghi nhận tác nhân tới `users` (trừ `user_id`) để giảm giao cắt. Danh mục và DBML vẫn giữ đủ 1.079 thuộc tính, 278 FK.
- Khung mờ trong sơ đồ nhóm là thực thể tham chiếu từ nhóm khác; chỉ hiển thị định danh. Các quan hệ tổng quát bằng `entity_type`/`entity_id`, `reference_type`/`reference_id` là liên kết ngữ nghĩa, không tự vẽ như FK có ràng buộc.
- Nhãn enum trong dữ liệu metadata dùng tên enum C#; biểu thức CHECK dưới từng bảng chứa các giá trị lưu thực tế (ví dụ `PENDING_CONFIRMATION`).

## Từ conceptual sang logical

| Nhóm | Bảng chi tiết/bảng nối làm rõ quan hệ |
|---|---|
| Phân quyền | `role_permissions`, `store_member_permissions`, `store_members` |
| Sản phẩm | `product_packagings`, `product_active_ingredients`, `store_products`, `product_reviews` |
| Khách hàng/giá | `customer_group_assignments`, `customer_group_price_lists`, `price_list_items` |
| Nhập/tồn | `goods_receipt_items`, `inventory_lot_balances`, `stock_movement_items`, `stocktake_items`, `inventory_reservation_items` |
| Bán/thanh toán | `cart_items`, `order_items`, `payment_allocations` |
| Giao hàng | `delivery_items`, `delivery_item_lot_allocations`, `delivery_attempt_items` |
| Trả hàng | `sales_return_items`; `refunds` có thể liên quan hoàn tiền đơn bị hủy và không phải lúc nào cũng có `sales_return_id` |
| AI | `diagnosis_images`, `ai_inferences`, `agent_reviews`, `recommendation_items`, `disease_treatments` |

Ảnh ghi “Product Preview”, trong project thực thể tương ứng là **Product Review** (`product_reviews`). “Good Receipt” tương ứng **Goods Receipt** (`goods_receipts`); “Sale Return” tương ứng **Sales Return** (`sales_returns`).

## Quan hệ và ràng buộc nghiệp vụ cần giữ

- User có đúng một role qua `users.role_id`; role và permission là N–N qua `role_permissions`. StoreMember có override quyền qua `store_member_permissions`.
- User có tối đa một FarmerProfile; FarmerProfile có các bản ghi tín dụng/công nợ theo các khóa duy nhất cụ thể dưới từng bảng.
- Product và Store liên hệ qua StoreProduct. InventoryLot gắn StoreProduct; InventoryLotBalance có tối đa một bản ghi cho mỗi InventoryLot.
- Product có nhiều ProductPackaging; đơn vị đóng gói quy đổi về đơn vị cơ sở. Product và ActiveIngredient là N–N qua ProductActiveIngredient.
- Một Order có nhiều OrderItem, Delivery, Payment và PaymentAllocation theo các FK thực tế. PaymentAllocation phân bổ tiền cho đơn hoặc công nợ; không suy công nợ chỉ từ `payments.order_id`.
- DeliveryItemLotAllocation ghi rõ lô thực tế của từng DeliveryItem. DeliveryAttemptItem ghi kết quả giao theo từng dòng trong mỗi lần giao.
- Thay đổi tồn vật lý cần StockMovement + StockMovementItem; thay đổi công nợ cần DebtTransaction. Hoàn trả truy vết nguồn bán/giao, điều chỉnh nợ và nhập lại tồn.
- SalesReturnItem phải trỏ đúng một nguồn: `delivery_item_lot_allocation_id` hoặc `original_stock_movement_item_id`. Các nguồn này là tùy chọn riêng lẻ nhưng có điều kiện XOR.
- Công nợ chỉ phát sinh cho phần chưa thanh toán sau fulfillment thành công; mức phơi nhiễm tín dụng gồm dư nợ + reservation tín dụng đang hoạt động.
- AIInference là kết quả máy; AgentReview là quyết định con người được phân quyền. RecommendationItem phải có duyệt hợp lệ trước khi dùng khuyến nghị sản phẩm.
- Không hard-code Store ID. DELETE nghiệp vụ là soft-delete/hủy/đảo; bản ghi đã post không bị sửa âm thầm. Các trường trạng thái và CHECK không thay thế điều kiện liên bảng tại Application/Domain.

## Khóa biểu thức ngoài metadata EF

| Thực thể | Khóa duy nhất | Phạm vi |
|---|---|---|
| `users` | `LOWER(email)` | `deleted_at IS NULL AND email IS NOT NULL` |
| `inventory_lots` | `store_product_id, LOWER(lot_number), expiry_date` | `deleted_at IS NULL AND lot_number IS NOT NULL; NULL expiry_date được xem là cùng giá trị` |

Các khóa trên được xác nhận từ `PostgreSqlRawIndexes.cs` và thiết kế §35.14; DBML lưu chúng dưới dạng Note để không làm mất điều kiện.

## Các nhóm thực thể

| Nhóm | Số bảng | Sơ đồ |
|---|---:|---|
| Tài khoản, xác thực và cửa hàng | 11 | [01-identity-store.svg](01-identity-store.svg) |
| Khách hàng và bảng giá | 6 | [02-customers-pricing.svg](02-customers-pricing.svg) |
| Danh mục sản phẩm | 9 | [03-catalog.svg](03-catalog.svg) |
| Nhà cung cấp và nhập hàng | 3 | [04-receiving.svg](04-receiving.svg) |
| Tồn kho và đặt giữ hàng | 8 | [05-inventory.svg](05-inventory.svg) |
| Giỏ hàng, đơn hàng và thanh toán | 6 | [06-sales-payments.svg](06-sales-payments.svg) |
| Giao hàng và truy vết lô | 6 | [07-delivery.svg](07-delivery.svg) |
| Hạn mức tín dụng và công nợ | 8 | [08-credit-debt.svg](08-credit-debt.svg) |
| Trả hàng và hoàn tiền | 3 | [09-returns.svg](09-returns.svg) |
| Chẩn đoán AI và duyệt khuyến nghị | 9 | [10-ai-diagnosis.svg](10-ai-diagnosis.svg) |
| Nội dung, thông báo và nhật ký | 5 | [11-content-system.svg](11-content-system.svg) |

## Tài khoản, xác thực và cửa hàng

### `auth_challenges` — AuthChallenge

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `channel` | String(20) |  | Không | Enum: Email, Sms |
| `consumed_at` | DateTime |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `destination` | String(255) |  | Không |  |
| `expires_at` | DateTime |  | Không |  |
| `failed_attempts` | Integer |  | Không |  |
| `purpose` | String(30) |  | Không | Enum: PasswordReset, EmailVerification, PhoneVerification |
| `security_version` | Integer64 |  | Không |  |
| `token_hash` | String(64) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK | Không | `users.id` |

**Quan hệ**

- `users` (0..1) → (0..N) `auth_challenges` qua `deleted_by`.
- `users` (1) → (0..N) `auth_challenges` qua `user_id`.

**Ràng buộc trên bản ghi**

- `ck_auth_challenges_channel`: `channel IN ('EMAIL', 'SMS')`
- `ck_auth_challenges_failed_attempts`: `failed_attempts BETWEEN 0 AND 5`
- `ck_auth_challenges_purpose`: `purpose IN ('PASSWORD_RESET', 'EMAIL_VERIFICATION', 'PHONE_VERIFICATION')`
- `ck_auth_challenges_purpose_channel`: `purpose = 'PASSWORD_RESET' OR (purpose = 'EMAIL_VERIFICATION' AND channel = 'EMAIL') OR (purpose = 'PHONE_VERIFICATION' AND channel = 'SMS')`
- `ck_auth_challenges_security_version`: `security_version >= 0`
### `auth_sessions` — AuthSession

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `expires_at` | DateTime |  | Không |  |
| `last_used_at` | DateTime |  | Không |  |
| `revocation_reason` | String(100) |  | Có |  |
| `revoked_at` | DateTime |  | Có |  |
| `security_version` | Integer64 |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK | Không | `users.id` |

**Quan hệ**

- `users` (0..1) → (0..N) `auth_sessions` qua `deleted_by`.
- `users` (1) → (0..N) `auth_sessions` qua `user_id`.

**Ràng buộc trên bản ghi**

- `ck_auth_sessions_security_version`: `security_version >= 0`
### `permissions` — Permission

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(120) | UQ | Không |  |
| `is_active` | Boolean |  | Không |  |
| `is_delegable` | Boolean |  | Không |  |
| `module` | String(50) |  | Không |  |
| `name` | String(150) |  | Không |  |

**Khóa duy nhất**

- `(code)`.
### `refresh_tokens` — RefreshToken

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `consumed_at` | DateTime |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `expires_at` | DateTime |  | Không |  |
| `session_id` | Identifier | FK | Không | `auth_sessions.id` |
| `token_hash` | String(64) | UQ | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `refresh_tokens` qua `deleted_by`.
- `auth_sessions` (1) → (0..N) `refresh_tokens` qua `session_id`.

**Khóa duy nhất**

- `(token_hash)`.
### `role_permissions` — RolePermission

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `is_granted` | Boolean |  | Không |  |
| `permission_id` | Identifier | FK | Không | `permissions.id` |
| `role_id` | Identifier | FK | Không | `roles.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `permissions` (1) → (0..N) `role_permissions` qua `permission_id`.
- `roles` (1) → (0..N) `role_permissions` qua `role_id`.

**Khóa duy nhất**

- `(role_id, permission_id)`.
### `roles` — Role

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(30) |  | Không | Enum: Farmer, StoreOwner, SalesStaff, DeliveryStaff, Admin |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String(500) |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `name` | String(100) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `version` | Integer64 |  | Không | Phiên bản optimistic concurrency |

**Quan hệ**

- `users` (0..1) → (0..N) `roles` qua `deleted_by`.

**Khóa duy nhất**

- `(code)` khi `deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_roles_code`: `code IN ('FARMER', 'STORE_OWNER', 'SALES_STAFF', 'DELIVERY_STAFF', 'ADMIN')`
### `store_member_permissions` — StoreMemberPermission

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `is_granted` | Boolean |  | Không |  |
| `permission_id` | Identifier | FK | Không | `permissions.id` |
| `store_member_id` | Identifier | FK | Không | `store_members.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `permissions` (1) → (0..N) `store_member_permissions` qua `permission_id`.
- `store_members` (1) → (0..N) `store_member_permissions` qua `store_member_id`.

**Khóa duy nhất**

- `(store_member_id, permission_id)`.
### `store_members` — StoreMember

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `can_review_ai` | Boolean |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `employee_code` | String(50) |  | Có |  |
| `joined_at` | Date |  | Có |  |
| `left_at` | Date |  | Có |  |
| `status` | String(20) |  | Không | Enum: Active, Inactive, Left |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK | Không | `users.id` |
| `version` | Integer64 |  | Không | Phiên bản optimistic concurrency |

**Quan hệ**

- `users` (0..1) → (0..N) `store_members` qua `deleted_by`.
- `stores` (1) → (0..N) `store_members` qua `store_id`.
- `users` (1) → (0..N) `store_members` qua `user_id`.

**Khóa duy nhất**

- `(store_id, user_id)`.

**Ràng buộc trên bản ghi**

- `ck_store_members_status`: `status IN ('ACTIVE', 'INACTIVE', 'LEFT')`
### `stores` — Store

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `address_line` | String(500) |  | Không |  |
| `code` | String(30) | UQ | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `district` | String(150) |  | Có |  |
| `email` | String(255) |  | Có |  |
| `name` | String(200) |  | Không |  |
| `phone_number` | String(20) |  | Có |  |
| `province` | String(150) |  | Không |  |
| `status` | String(20) |  | Không | Enum: Active, Inactive |
| `tax_code` | String(50) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |
| `ward` | String(150) |  | Có |  |

**Quan hệ**

- `users` (0..1) → (0..N) `stores` qua `deleted_by`.

**Khóa duy nhất**

- `(code)`.

**Ràng buộc trên bản ghi**

- `ck_stores_status`: `status IN ('ACTIVE', 'INACTIVE')`
### `user_addresses` — UserAddress

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `address_line` | String(500) |  | Không |  |
| `address_type` | String(20) |  | Không | Enum: Home, Farm, Other |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `district` | String(150) |  | Có |  |
| `is_default` | Boolean |  | Không |  |
| `latitude` | Decimal(10,7) |  | Có |  |
| `longitude` | Decimal(10,7) |  | Có |  |
| `province` | String(150) |  | Không |  |
| `recipient_name` | String(150) |  | Không |  |
| `recipient_phone` | String(20) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK | Không | `users.id` |
| `ward` | String(150) |  | Có |  |

**Quan hệ**

- `users` (0..1) → (0..N) `user_addresses` qua `deleted_by`.
- `users` (1) → (0..N) `user_addresses` qua `user_id`.

**Khóa duy nhất**

- `(user_id)` khi `is_default AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_user_addresses_address_type`: `address_type IN ('HOME', 'FARM', 'OTHER')`
### `users` — User

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `avatar_url` | String(1000) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `email` | String(255) |  | Có |  |
| `email_verified` | Boolean |  | Không |  |
| `full_name` | String(150) |  | Không |  |
| `last_login_at` | DateTime |  | Có |  |
| `password_hash` | String(500) |  | Không |  |
| `phone_number` | String(20) |  | Có |  |
| `phone_verified` | Boolean |  | Không |  |
| `role_id` | Identifier | FK | Không | `roles.id` |
| `security_version` | Integer64 |  | Không | Phiên bản optimistic concurrency |
| `status` | String(30) |  | Không | Enum: Active, Inactive, Suspended, Locked |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `users` qua `deleted_by`.
- `roles` (1) → (0..N) `users` qua `role_id`.

**Khóa duy nhất**

- `(phone_number)` khi `deleted_at IS NULL AND phone_number IS NOT NULL`.

**Ràng buộc trên bản ghi**

- `ck_users_contact`: `NULLIF(BTRIM(email), '') IS NOT NULL OR NULLIF(BTRIM(phone_number), '') IS NOT NULL`
- `ck_users_security_version`: `security_version >= 0`
- `ck_users_status`: `status IN ('ACTIVE', 'INACTIVE', 'SUSPENDED', 'LOCKED')`

## Khách hàng và bảng giá

### `customer_group_assignments` — CustomerGroupAssignment

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `assigned_by` | Identifier | FK | Không | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `customer_group_id` | Identifier | FK | Không | `customer_groups.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `effective_from` | DateTime |  | Không |  |
| `effective_to` | DateTime |  | Có |  |
| `farmer_profile_id` | Identifier | FK | Không | `farmer_profiles.id` |
| `reason` | String(500) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `customer_group_assignments` qua `assigned_by`.
- `customer_groups` (1) → (0..N) `customer_group_assignments` qua `customer_group_id`.
- `users` (0..1) → (0..N) `customer_group_assignments` qua `deleted_by`.
- `farmer_profiles` (1) → (0..N) `customer_group_assignments` qua `farmer_profile_id`.

**Khóa duy nhất**

- `(farmer_profile_id)` khi `effective_to IS NULL AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_customer_group_assignments_effective_period`: `effective_to IS NULL OR effective_to > effective_from`
### `customer_group_price_lists` — CustomerGroupPriceList

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `assigned_by` | Identifier | FK | Không | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `customer_group_id` | Identifier | FK | Không | `customer_groups.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `effective_from` | DateTime |  | Không |  |
| `effective_to` | DateTime |  | Có |  |
| `price_list_id` | Identifier | FK | Không | `price_lists.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `customer_group_price_lists` qua `assigned_by`.
- `customer_groups` (1) → (0..N) `customer_group_price_lists` qua `customer_group_id`.
- `users` (0..1) → (0..N) `customer_group_price_lists` qua `deleted_by`.
- `price_lists` (1) → (0..N) `customer_group_price_lists` qua `price_list_id`.

**Ràng buộc trên bản ghi**

- `ck_customer_group_price_lists_effective_period`: `effective_to IS NULL OR effective_to > effective_from`
### `customer_groups` — CustomerGroup

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(30) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `default_credit_tier_id` | Identifier | FK | Có | `credit_tiers.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String(500) |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `is_default` | Boolean |  | Không |  |
| `name` | String(100) |  | Không |  |
| `priority` | Integer |  | Không |  |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `credit_tiers` (0..1) → (0..N) `customer_groups` qua `default_credit_tier_id`.
- `users` (0..1) → (0..N) `customer_groups` qua `deleted_by`.
- `stores` (1) → (0..N) `customer_groups` qua `store_id`.

**Khóa duy nhất**

- `(store_id, code)`.
- `(store_id)` khi `is_default AND is_active AND deleted_at IS NULL`.
### `farmer_profiles` — FarmerProfile

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `date_of_birth` | Date |  | Có |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `gender` | String(20) |  | Có |  |
| `notes` | String(1000) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK, UQ | Không | `users.id` |

**Quan hệ**

- `users` (0..1) → (0..N) `farmer_profiles` qua `deleted_by`.
- `users` (1) → (0..1) `farmer_profiles` qua `user_id`.

**Khóa duy nhất**

- `(user_id)`.
### `price_list_items` — PriceListItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `price_list_id` | Identifier | FK | Không | `price_lists.id` |
| `product_packaging_id` | Identifier | FK | Không | `product_packagings.id` |
| `selling_price` | Decimal(18,2) |  | Không |  |
| `store_product_id` | Identifier | FK | Không | `store_products.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `price_list_items` qua `deleted_by`.
- `price_lists` (1) → (0..N) `price_list_items` qua `price_list_id`.
- `product_packagings` (1) → (0..N) `price_list_items` qua `product_packaging_id`.
- `store_products` (1) → (0..N) `price_list_items` qua `store_product_id`.

**Khóa duy nhất**

- `(price_list_id, store_product_id, product_packaging_id)`.

**Ràng buộc trên bản ghi**

- `ck_price_list_items_selling_price`: `selling_price >= 0`
### `price_lists` — PriceList

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(50) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String(500) |  | Có |  |
| `effective_from` | DateTime |  | Không |  |
| `effective_to` | DateTime |  | Có |  |
| `is_walk_in_default` | Boolean |  | Không |  |
| `name` | String(150) |  | Không |  |
| `status` | String(20) |  | Không | Enum: Draft, Active, Inactive |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `price_lists` qua `deleted_by`.
- `stores` (1) → (0..N) `price_lists` qua `store_id`.

**Khóa duy nhất**

- `(store_id)` khi `is_walk_in_default AND status = 'ACTIVE' AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_price_lists_effective_period`: `effective_to IS NULL OR effective_to > effective_from`
- `ck_price_lists_status`: `status IN ('DRAFT', 'ACTIVE', 'INACTIVE')`

## Danh mục sản phẩm

### `active_ingredients` — ActiveIngredient

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(50) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `name` | String(200) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `active_ingredients` qua `deleted_by`.
### `brands` — Brand

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(50) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String(500) |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `logo_url` | String(1000) |  | Có |  |
| `name` | String(150) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `brands` qua `deleted_by`.

**Khóa duy nhất**

- `(name)` khi `deleted_at IS NULL`.
### `categories` — Category

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(50) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String(500) |  | Có |  |
| `display_order` | Integer |  | Không |  |
| `is_active` | Boolean |  | Không |  |
| `name` | String(150) |  | Không |  |
| `parent_id` | Identifier | FK | Có | `categories.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `categories` qua `deleted_by`.
- `categories` (0..1) → (0..N) `categories` qua `parent_id`.
### `product_active_ingredients` — ProductActiveIngredient

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `active_ingredient_id` | Identifier | FK | Không | `active_ingredients.id` |
| `concentration` | String(100) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `note` | String(500) |  | Có |  |
| `product_id` | Identifier | FK | Không | `products.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `active_ingredients` (1) → (0..N) `product_active_ingredients` qua `active_ingredient_id`.
- `users` (0..1) → (0..N) `product_active_ingredients` qua `deleted_by`.
- `products` (1) → (0..N) `product_active_ingredients` qua `product_id`.

**Khóa duy nhất**

- `(product_id, active_ingredient_id)`.
### `product_packagings` — ProductPackaging

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `barcode` | String(100) |  | Có |  |
| `conversion_to_base` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `is_base_unit` | Boolean |  | Không |  |
| `is_purchase_unit` | Boolean |  | Không |  |
| `is_sale_unit` | Boolean |  | Không |  |
| `packaging_name` | String(150) |  | Có |  |
| `product_id` | Identifier | FK | Không | `products.id` |
| `status` | String(20) |  | Không |  |
| `unit_id` | Identifier | FK | Không | `units.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `product_packagings` qua `deleted_by`.
- `products` (1) → (0..N) `product_packagings` qua `product_id`.
- `units` (1) → (0..N) `product_packagings` qua `unit_id`.

**Khóa duy nhất**

- `(barcode)` khi `barcode IS NOT NULL AND deleted_at IS NULL`.
- `(product_id, unit_id)` khi `deleted_at IS NULL`.
- `(product_id)` khi `is_base_unit AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_product_packagings_base_unit_conversion`: `NOT is_base_unit OR conversion_to_base = 1`
- `ck_product_packagings_conversion_to_base`: `conversion_to_base > 0`
### `product_reviews` — ProductReview

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `comment` | String |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_profile_id` | Identifier | FK | Không | `farmer_profiles.id` |
| `order_item_id` | Identifier | FK | Không | `order_items.id` |
| `rating` | Integer |  | Không |  |
| `status` | String(20) |  | Không |  |
| `store_product_id` | Identifier | FK | Không | `store_products.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `product_reviews` qua `deleted_by`.
- `farmer_profiles` (1) → (0..N) `product_reviews` qua `farmer_profile_id`.
- `order_items` (1) → (0..N) `product_reviews` qua `order_item_id`.
- `store_products` (1) → (0..N) `product_reviews` qua `store_product_id`.

**Khóa duy nhất**

- `(farmer_profile_id, order_item_id)`.

**Ràng buộc trên bản ghi**

- `ck_product_reviews_rating`: `rating BETWEEN 1 AND 5`
### `products` — Product

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `brand_id` | Identifier | FK | Có | `brands.id` |
| `category_id` | Identifier | FK | Không | `categories.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String |  | Có |  |
| `image_url` | String(1000) |  | Có |  |
| `name` | String(255) |  | Không |  |
| `requires_expiry_date` | Boolean |  | Không |  |
| `requires_lot_tracking` | Boolean |  | Không |  |
| `sku` | String(50) | UQ | Không |  |
| `status` | String(20) |  | Không | Enum: Active, Inactive, Discontinued |
| `updated_at` | DateTime |  | Không |  |
| `usage_instructions` | String |  | Có |  |

**Quan hệ**

- `brands` (0..1) → (0..N) `products` qua `brand_id`.
- `categories` (1) → (0..N) `products` qua `category_id`.
- `users` (0..1) → (0..N) `products` qua `deleted_by`.

**Khóa duy nhất**

- `(sku)`.

**Ràng buộc trên bản ghi**

- `ck_products_status`: `status IN ('ACTIVE', 'INACTIVE', 'DISCONTINUED')`
### `store_products` — StoreProduct

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `is_active` | Boolean |  | Không |  |
| `is_sellable` | Boolean |  | Không |  |
| `min_stock_level_base` | Integer64 |  | Có |  |
| `product_id` | Identifier | FK | Không | `products.id` |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `store_sku` | String(50) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `store_products` qua `deleted_by`.
- `products` (1) → (0..N) `store_products` qua `product_id`.
- `stores` (1) → (0..N) `store_products` qua `store_id`.

**Khóa duy nhất**

- `(store_id, product_id)`.
### `units` — Unit

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(30) | UQ | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `is_active` | Boolean |  | Không |  |
| `name` | String(100) |  | Không |  |
| `symbol` | String(20) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `units` qua `deleted_by`.

**Khóa duy nhất**

- `(code)`.

## Nhà cung cấp và nhập hàng

### `goods_receipt_items` — GoodsReceiptItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `base_quantity` | Integer64 |  | Không |  |
| `base_unit_cost` | Decimal(20,6) |  | Không |  |
| `conversion_to_base_snapshot` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `expiry_date` | Date |  | Có |  |
| `goods_receipt_id` | Identifier | FK | Không | `goods_receipts.id` |
| `inventory_lot_id` | Identifier | FK | Có | `inventory_lots.id` |
| `line_total_amount` | Decimal(18,2) |  | Không |  |
| `manufacturing_date` | Date |  | Có |  |
| `note` | String(500) |  | Có |  |
| `product_packaging_id` | Identifier | FK | Không | `product_packagings.id` |
| `purchase_unit_cost` | Decimal(18,2) |  | Không |  |
| `received_quantity` | Integer64 |  | Không |  |
| `store_product_id` | Identifier | FK | Không | `store_products.id` |
| `supplier_lot_number` | String(100) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `goods_receipt_items` qua `deleted_by`.
- `goods_receipts` (1) → (0..N) `goods_receipt_items` qua `goods_receipt_id`.
- `inventory_lots` (0..1) → (0..N) `goods_receipt_items` qua `inventory_lot_id`.
- `product_packagings` (1) → (0..N) `goods_receipt_items` qua `product_packaging_id`.
- `store_products` (1) → (0..N) `goods_receipt_items` qua `store_product_id`.

**Ràng buộc trên bản ghi**

- `ck_goods_receipt_items_base_unit_cost`: `base_unit_cost >= 0`
- `ck_goods_receipt_items_conversion_to_base_snapshot`: `conversion_to_base_snapshot > 0`
- `ck_goods_receipt_items_line_total_amount`: `line_total_amount >= 0`
- `ck_goods_receipt_items_purchase_unit_cost`: `purchase_unit_cost >= 0`
- `ck_goods_receipt_items_received_quantity`: `received_quantity > 0`
### `goods_receipts` — GoodsReceipt

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `cancel_reason` | String(1000) |  | Có |  |
| `cancelled_at` | DateTime |  | Có |  |
| `cancelled_by` | Identifier | FK | Có | `users.id` |
| `confirmed_at` | DateTime |  | Có |  |
| `confirmed_by` | Identifier | FK | Có | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `note` | String(1000) |  | Có |  |
| `receipt_number` | String(50) |  | Không |  |
| `received_at` | DateTime |  | Không |  |
| `received_by` | Identifier | FK | Không | `users.id` |
| `source_file_name` | String(255) |  | Có |  |
| `source_file_url` | String(1000) |  | Có |  |
| `source_type` | String(20) |  | Không | Enum: Manual, ExcelTemplate |
| `status` | String(20) |  | Không | Enum: Draft, Confirmed, Cancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `subtotal_amount` | Decimal(18,2) |  | Không |  |
| `supplier_id` | Identifier | FK | Không | `suppliers.id` |
| `supplier_invoice_date` | Date |  | Có |  |
| `supplier_invoice_number` | String(100) |  | Có |  |
| `total_amount` | Decimal(18,2) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `goods_receipts` qua `cancelled_by`.
- `users` (0..1) → (0..N) `goods_receipts` qua `confirmed_by`.
- `users` (0..1) → (0..N) `goods_receipts` qua `deleted_by`.
- `users` (1) → (0..N) `goods_receipts` qua `received_by`.
- `stores` (1) → (0..N) `goods_receipts` qua `store_id`.
- `suppliers` (1) → (0..N) `goods_receipts` qua `supplier_id`.

**Khóa duy nhất**

- `(store_id, receipt_number)`.

**Ràng buộc trên bản ghi**

- `ck_goods_receipts_source_type`: `source_type IN ('MANUAL', 'EXCEL_TEMPLATE')`
- `ck_goods_receipts_status`: `status IN ('DRAFT', 'CONFIRMED', 'CANCELLED')`
- `ck_goods_receipts_subtotal_amount`: `subtotal_amount >= 0`
- `ck_goods_receipts_total_amount`: `total_amount >= 0`
### `suppliers` — Supplier

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `address_line` | String(500) |  | Có |  |
| `code` | String(50) |  | Có |  |
| `contact_person` | String(150) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `district` | String(150) |  | Có |  |
| `email` | String(255) |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `name` | String(255) |  | Không |  |
| `note` | String(1000) |  | Có |  |
| `phone_number` | String(20) |  | Có |  |
| `province` | String(150) |  | Có |  |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `tax_code` | String(50) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |
| `ward` | String(150) |  | Có |  |

**Quan hệ**

- `users` (0..1) → (0..N) `suppliers` qua `deleted_by`.
- `stores` (1) → (0..N) `suppliers` qua `store_id`.

**Khóa duy nhất**

- `(store_id, code)` khi `code IS NOT NULL AND deleted_at IS NULL`.

## Tồn kho và đặt giữ hàng

### `inventory_lot_balances` — InventoryLotBalance

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `inventory_lot_id` | Identifier | FK, UQ | Không | `inventory_lots.id` |
| `quantity_on_hand` | Integer64 |  | Không |  |
| `quantity_reserved` | Integer64 |  | Không |  |
| `total_cost_value` | Decimal(20,6) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `version` | Integer64 |  | Không | Phiên bản optimistic concurrency |

**Quan hệ**

- `users` (0..1) → (0..N) `inventory_lot_balances` qua `deleted_by`.
- `inventory_lots` (1) → (0..1) `inventory_lot_balances` qua `inventory_lot_id`.

**Khóa duy nhất**

- `(inventory_lot_id)`.

**Ràng buộc trên bản ghi**

- `ck_inventory_lot_balances_quantity_on_hand`: `quantity_on_hand >= 0`
- `ck_inventory_lot_balances_quantity_reserved`: `quantity_reserved >= 0`
- `ck_inventory_lot_balances_reserved_within_on_hand`: `quantity_reserved <= quantity_on_hand`
- `ck_inventory_lot_balances_total_cost_value`: `total_cost_value >= 0`
- `ck_inventory_lot_balances_zero_quantity_zero_cost`: `quantity_on_hand > 0 OR total_cost_value = 0`
### `inventory_lots` — InventoryLot

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `expiry_date` | Date |  | Có |  |
| `lot_number` | String(100) |  | Có |  |
| `manufacturing_date` | Date |  | Có |  |
| `status` | String(30) |  | Không | Enum: Active, Quarantined, Expired, Blocked, Depleted |
| `store_product_id` | Identifier | FK | Không | `store_products.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `inventory_lots` qua `deleted_by`.
- `store_products` (1) → (0..N) `inventory_lots` qua `store_product_id`.

**Khóa duy nhất**

- `(store_product_id)` khi `lot_number IS NULL AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_inventory_lots_status`: `status IN ('ACTIVE', 'QUARANTINED', 'EXPIRED', 'BLOCKED', 'DEPLETED')`
### `inventory_reservation_items` — InventoryReservationItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `base_quantity_consumed` | Integer64 |  | Không |  |
| `base_quantity_released` | Integer64 |  | Không |  |
| `base_quantity_reserved` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `inventory_lot_id` | Identifier | FK | Không | `inventory_lots.id` |
| `inventory_reservation_id` | Identifier | FK | Không | `inventory_reservations.id` |
| `order_item_id` | Identifier | FK | Không | `order_items.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `inventory_reservation_items` qua `deleted_by`.
- `inventory_lots` (1) → (0..N) `inventory_reservation_items` qua `inventory_lot_id`.
- `inventory_reservations` (1) → (0..N) `inventory_reservation_items` qua `inventory_reservation_id`.
- `order_items` (1) → (0..N) `inventory_reservation_items` qua `order_item_id`.

**Khóa duy nhất**

- `(inventory_reservation_id, order_item_id, inventory_lot_id)` khi `deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_inventory_reservation_items_base_quantity_consumed`: `base_quantity_consumed >= 0`
- `ck_inventory_reservation_items_base_quantity_released`: `base_quantity_released >= 0`
- `ck_inventory_reservation_items_base_quantity_reserved`: `base_quantity_reserved > 0`
- `ck_inventory_reservation_items_within_reserved`: `base_quantity_consumed + base_quantity_released <= base_quantity_reserved`
### `inventory_reservations` — InventoryReservation

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `order_id` | Identifier | FK | Không | `orders.id` |
| `release_reason` | String(500) |  | Có |  |
| `released_at` | DateTime |  | Có |  |
| `released_by` | Identifier | FK | Có | `users.id` |
| `reserved_at` | DateTime |  | Không |  |
| `reserved_by` | Identifier | FK | Không | `users.id` |
| `status` | String(30) |  | Không | Enum: Active, PartiallyConsumed, Consumed, Released, Cancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `inventory_reservations` qua `deleted_by`.
- `orders` (1) → (0..N) `inventory_reservations` qua `order_id`.
- `users` (0..1) → (0..N) `inventory_reservations` qua `released_by`.
- `users` (1) → (0..N) `inventory_reservations` qua `reserved_by`.
- `stores` (1) → (0..N) `inventory_reservations` qua `store_id`.

**Khóa duy nhất**

- `(order_id)` khi `status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_inventory_reservations_status`: `status IN ('ACTIVE', 'PARTIALLY_CONSUMED', 'CONSUMED', 'RELEASED', 'CANCELLED')`
### `stock_movement_items` — StockMovementItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `inventory_lot_id` | Identifier | FK | Không | `inventory_lots.id` |
| `note` | String(500) |  | Có |  |
| `quantity_delta_base` | Integer64 |  | Không |  |
| `quantity_on_hand_after` | Integer64 |  | Có |  |
| `stock_movement_id` | Identifier | FK | Không | `stock_movements.id` |
| `total_cost_snapshot` | Decimal(20,6) |  | Không |  |
| `total_cost_value_after` | Decimal(20,6) |  | Có |  |
| `unit_cost_snapshot` | Decimal(20,6) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `stock_movement_items` qua `deleted_by`.
- `inventory_lots` (1) → (0..N) `stock_movement_items` qua `inventory_lot_id`.
- `stock_movements` (1) → (0..N) `stock_movement_items` qua `stock_movement_id`.

**Ràng buộc trên bản ghi**

- `ck_stock_movement_items_quantity_delta_base`: `quantity_delta_base <> 0`
- `ck_stock_movement_items_total_cost_snapshot`: `total_cost_snapshot >= 0`
- `ck_stock_movement_items_unit_cost_snapshot`: `unit_cost_snapshot >= 0`
### `stock_movements` — StockMovement

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_id` | Identifier | FK | Có | `deliveries.id` |
| `goods_receipt_id` | Identifier | FK | Có | `goods_receipts.id` |
| `movement_number` | String(50) |  | Không |  |
| `movement_type` | String(30) |  | Không | Enum: StockIn, Sale, ReturnIn, AdjustmentIn, AdjustmentOut, Reversal |
| `occurred_at` | DateTime |  | Không |  |
| `order_id` | Identifier | FK | Có | `orders.id` |
| `posted_at` | DateTime |  | Có |  |
| `posted_by` | Identifier | FK | Có | `users.id` |
| `reason` | String(1000) |  | Có |  |
| `reason_code` | String(50) |  | Có |  |
| `reversal_of_movement_id` | Identifier | FK | Có | `stock_movements.id` |
| `sales_return_id` | Identifier | FK | Có | `sales_returns.id` |
| `status` | String(20) |  | Không | Enum: Draft, Posted, Reversed, Cancelled |
| `stocktake_id` | Identifier | FK | Có | `stocktakes.id` |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `stock_movements` qua `created_by`.
- `users` (0..1) → (0..N) `stock_movements` qua `deleted_by`.
- `deliveries` (0..1) → (0..N) `stock_movements` qua `delivery_id`.
- `goods_receipts` (0..1) → (0..N) `stock_movements` qua `goods_receipt_id`.
- `orders` (0..1) → (0..N) `stock_movements` qua `order_id`.
- `users` (0..1) → (0..N) `stock_movements` qua `posted_by`.
- `stock_movements` (0..1) → (0..N) `stock_movements` qua `reversal_of_movement_id`.
- `sales_returns` (0..1) → (0..N) `stock_movements` qua `sales_return_id`.
- `stocktakes` (0..1) → (0..N) `stock_movements` qua `stocktake_id`.
- `stores` (1) → (0..N) `stock_movements` qua `store_id`.

**Ràng buộc trên bản ghi**

- `ck_stock_movements_movement_type`: `movement_type IN ('STOCK_IN', 'SALE', 'RETURN_IN', 'ADJUSTMENT_IN', 'ADJUSTMENT_OUT', 'REVERSAL')`
- `ck_stock_movements_status`: `status IN ('DRAFT', 'POSTED', 'REVERSED', 'CANCELLED')`
### `stocktake_items` — StocktakeItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `counted_at` | DateTime |  | Có |  |
| `counted_by` | Identifier | FK | Có | `users.id` |
| `counted_quantity` | Integer64 |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `difference_cost_value` | Decimal(20,6) |  | Có |  |
| `difference_quantity` | Integer64 |  | Có |  |
| `inventory_lot_id` | Identifier | FK | Không | `inventory_lots.id` |
| `note` | String(500) |  | Có |  |
| `reason_code` | String(50) |  | Có |  |
| `snapshot_at` | DateTime |  | Không |  |
| `stocktake_id` | Identifier | FK | Không | `stocktakes.id` |
| `system_quantity_snapshot` | Integer64 |  | Không |  |
| `unit_cost_snapshot` | Decimal(20,6) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `stocktake_items` qua `counted_by`.
- `users` (0..1) → (0..N) `stocktake_items` qua `deleted_by`.
- `inventory_lots` (1) → (0..N) `stocktake_items` qua `inventory_lot_id`.
- `stocktakes` (1) → (0..N) `stocktake_items` qua `stocktake_id`.

**Khóa duy nhất**

- `(stocktake_id, inventory_lot_id)`.

**Ràng buộc trên bản ghi**

- `ck_stocktake_items_counted_quantity`: `counted_quantity IS NULL OR counted_quantity >= 0`
- `ck_stocktake_items_system_quantity_snapshot`: `system_quantity_snapshot >= 0`
### `stocktakes` — Stocktake

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `completed_at` | DateTime |  | Có |  |
| `completed_by` | Identifier | FK | Có | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `note` | String(1000) |  | Có |  |
| `started_at` | DateTime |  | Có |  |
| `started_by` | Identifier | FK | Có | `users.id` |
| `status` | String(30) |  | Không | Enum: Draft, InProgress, Completed, Cancelled |
| `stocktake_number` | String(50) |  | Không |  |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `stocktakes` qua `completed_by`.
- `users` (1) → (0..N) `stocktakes` qua `created_by`.
- `users` (0..1) → (0..N) `stocktakes` qua `deleted_by`.
- `users` (0..1) → (0..N) `stocktakes` qua `started_by`.
- `stores` (1) → (0..N) `stocktakes` qua `store_id`.

**Khóa duy nhất**

- `(store_id, stocktake_number)`.

**Ràng buộc trên bản ghi**

- `ck_stocktakes_status`: `status IN ('DRAFT', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')`

## Giỏ hàng, đơn hàng và thanh toán

### `cart_items` — CartItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `cart_id` | Identifier | FK | Không | `carts.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `product_packaging_id` | Identifier | FK | Không | `product_packagings.id` |
| `quantity` | Integer64 |  | Không |  |
| `store_product_id` | Identifier | FK | Không | `store_products.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `carts` (1) → (0..N) `cart_items` qua `cart_id`.
- `users` (0..1) → (0..N) `cart_items` qua `deleted_by`.
- `product_packagings` (1) → (0..N) `cart_items` qua `product_packaging_id`.
- `store_products` (1) → (0..N) `cart_items` qua `store_product_id`.

**Khóa duy nhất**

- `(cart_id, store_product_id, product_packaging_id)` khi `deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_cart_items_quantity`: `quantity > 0`
### `carts` — Cart

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `converted_at` | DateTime |  | Có |  |
| `converted_order_id` | Identifier | FK | Có | `orders.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_profile_id` | Identifier | FK | Không | `farmer_profiles.id` |
| `status` | String(20) |  | Không | Enum: Active, Converted, Abandoned |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `orders` (0..1) → (0..N) `carts` qua `converted_order_id`.
- `users` (0..1) → (0..N) `carts` qua `deleted_by`.
- `farmer_profiles` (1) → (0..N) `carts` qua `farmer_profile_id`.
- `stores` (1) → (0..N) `carts` qua `store_id`.

**Khóa duy nhất**

- `(store_id, farmer_profile_id)` khi `status = 'ACTIVE' AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_carts_status`: `status IN ('ACTIVE', 'CONVERTED', 'ABANDONED')`
### `order_items` — OrderItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `base_quantity` | Integer64 |  | Không |  |
| `cancelled_base_quantity` | Integer64 |  | Không |  |
| `conversion_to_base_snapshot` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `fulfilled_base_quantity` | Integer64 |  | Không |  |
| `line_total_amount` | Decimal(18,2) |  | Không |  |
| `order_id` | Identifier | FK | Không | `orders.id` |
| `overridden_by` | Identifier | FK | Có | `users.id` |
| `override_reason` | String(500) |  | Có |  |
| `packaging_name_snapshot` | String(150) |  | Không |  |
| `price_overridden` | Boolean |  | Không |  |
| `product_name_snapshot` | String(255) |  | Không |  |
| `product_packaging_id` | Identifier | FK | Không | `product_packagings.id` |
| `product_sku_snapshot` | String(50) |  | Không |  |
| `quantity` | Integer64 |  | Không |  |
| `status` | String(30) |  | Không | Enum: Pending, PartiallyFulfilled, Fulfilled, Cancelled, PartiallyCancelled |
| `store_product_id` | Identifier | FK | Không | `store_products.id` |
| `suggested_unit_price` | Decimal(18,2) |  | Không |  |
| `unit_price` | Decimal(18,2) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `order_items` qua `deleted_by`.
- `orders` (1) → (0..N) `order_items` qua `order_id`.
- `users` (0..1) → (0..N) `order_items` qua `overridden_by`.
- `product_packagings` (1) → (0..N) `order_items` qua `product_packaging_id`.
- `store_products` (1) → (0..N) `order_items` qua `store_product_id`.

**Ràng buộc trên bản ghi**

- `ck_order_items_base_quantity`: `base_quantity = quantity * conversion_to_base_snapshot`
- `ck_order_items_cancelled_base_quantity`: `cancelled_base_quantity >= 0`
- `ck_order_items_conversion_to_base_snapshot`: `conversion_to_base_snapshot > 0`
- `ck_order_items_fulfilled_base_quantity`: `fulfilled_base_quantity >= 0`
- `ck_order_items_fulfilled_cancelled_within_base`: `fulfilled_base_quantity + cancelled_base_quantity <= base_quantity`
- `ck_order_items_line_total_amount`: `line_total_amount >= 0`
- `ck_order_items_price_override`: `NOT price_overridden OR (override_reason IS NOT NULL AND overridden_by IS NOT NULL)`
- `ck_order_items_quantity`: `quantity > 0`
- `ck_order_items_status`: `status IN ('PENDING', 'PARTIALLY_FULFILLED', 'FULFILLED', 'CANCELLED', 'PARTIALLY_CANCELLED')`
- `ck_order_items_suggested_unit_price`: `suggested_unit_price >= 0`
- `ck_order_items_unit_price`: `unit_price >= 0`
### `orders` — Order

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `cancel_reason` | String(1000) |  | Có |  |
| `cancelled_at` | DateTime |  | Có |  |
| `cancelled_by` | Identifier | FK | Có | `users.id` |
| `completed_at` | DateTime |  | Có |  |
| `confirmed_at` | DateTime |  | Có |  |
| `confirmed_by` | Identifier | FK | Có | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `credit_term_days_snapshot` | Integer |  | Có |  |
| `customer_group_id_snapshot` | Identifier | FK | Có | `customer_groups.id` |
| `customer_name_snapshot` | String(150) |  | Không |  |
| `customer_phone_snapshot` | String(20) |  | Có |  |
| `customer_type` | String(20) |  | Không | Enum: Registered, WalkIn |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_address_line` | String(500) |  | Có |  |
| `delivery_district` | String(150) |  | Có |  |
| `delivery_latitude` | Decimal(10,7) |  | Có |  |
| `delivery_longitude` | Decimal(10,7) |  | Có |  |
| `delivery_province` | String(150) |  | Có |  |
| `delivery_ward` | String(150) |  | Có |  |
| `farmer_profile_id` | Identifier | FK | Có | `farmer_profiles.id` |
| `fulfillment_type` | String(20) |  | Không | Enum: Pickup, Delivery |
| `note` | String(1000) |  | Có |  |
| `order_number` | String(50) |  | Không |  |
| `pickup_completed_at` | DateTime |  | Có |  |
| `pickup_completed_by` | Identifier | FK | Có | `users.id` |
| `price_list_id_snapshot` | Identifier | FK | Có | `price_lists.id` |
| `recipient_name_snapshot` | String(150) |  | Có |  |
| `recipient_phone_snapshot` | String(20) |  | Có |  |
| `settlement_type` | String(20) |  | Không | Enum: FullPayment, Credit |
| `source` | String(30) |  | Không | Enum: FarmerWeb, FarmerMobile, Counter |
| `source_address_id` | Identifier | FK | Có | `user_addresses.id` |
| `status` | String(40) |  | Không | Enum: PendingConfirmation, Confirmed, Preparing, ReadyForFulfillment, PartiallyFulfilled, Completed, Cancelled, PartiallyCancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `subtotal_amount` | Decimal(18,2) |  | Không |  |
| `total_amount` | Decimal(18,2) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `version` | Integer64 |  | Không | Phiên bản optimistic concurrency |

**Quan hệ**

- `users` (0..1) → (0..N) `orders` qua `cancelled_by`.
- `users` (0..1) → (0..N) `orders` qua `confirmed_by`.
- `users` (1) → (0..N) `orders` qua `created_by`.
- `customer_groups` (0..1) → (0..N) `orders` qua `customer_group_id_snapshot`.
- `users` (0..1) → (0..N) `orders` qua `deleted_by`.
- `farmer_profiles` (0..1) → (0..N) `orders` qua `farmer_profile_id`.
- `users` (0..1) → (0..N) `orders` qua `pickup_completed_by`.
- `price_lists` (0..1) → (0..N) `orders` qua `price_list_id_snapshot`.
- `user_addresses` (0..1) → (0..N) `orders` qua `source_address_id`.
- `stores` (1) → (0..N) `orders` qua `store_id`.

**Khóa duy nhất**

- `(store_id, order_number)`.

**Ràng buộc trên bản ghi**

- `ck_orders_credit_term_days_snapshot`: `credit_term_days_snapshot IS NULL OR credit_term_days_snapshot >= 0`
- `ck_orders_customer_settlement`: `(customer_type = 'REGISTERED' AND farmer_profile_id IS NOT NULL) OR (customer_type = 'WALK_IN' AND farmer_profile_id IS NULL AND settlement_type = 'FULL_PAYMENT')`
- `ck_orders_customer_type`: `customer_type IN ('REGISTERED', 'WALK_IN')`
- `ck_orders_fulfillment_type`: `fulfillment_type IN ('PICKUP', 'DELIVERY')`
- `ck_orders_settlement_type`: `settlement_type IN ('FULL_PAYMENT', 'CREDIT')`
- `ck_orders_source`: `source IN ('FARMER_WEB', 'FARMER_MOBILE', 'COUNTER')`
- `ck_orders_status`: `status IN ('PENDING_CONFIRMATION', 'CONFIRMED', 'PREPARING', 'READY_FOR_FULFILLMENT', 'PARTIALLY_FULFILLED', 'COMPLETED', 'CANCELLED', 'PARTIALLY_CANCELLED')`
- `ck_orders_subtotal_amount`: `subtotal_amount >= 0`
- `ck_orders_total_amount`: `total_amount >= 0`
### `payment_allocations` — PaymentAllocation

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `allocated_amount` | Decimal(18,2) |  | Không |  |
| `allocated_at` | DateTime |  | Không |  |
| `allocated_by` | Identifier | FK | Có | `users.id` |
| `allocation_type` | String(20) |  | Không | Enum: Order, Debt |
| `created_at` | DateTime |  | Không |  |
| `debt_entry_id` | Identifier | FK | Có | `debt_entries.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `order_id` | Identifier | FK | Có | `orders.id` |
| `payment_id` | Identifier | FK | Không | `payments.id` |
| `prepayment_consumed_amount` | Decimal(18,2) |  | Không |  |
| `reversal_reason` | String(500) |  | Có |  |
| `reversed_at` | DateTime |  | Có |  |
| `reversed_by` | Identifier | FK | Có | `users.id` |
| `status` | String(20) |  | Không | Enum: Active, Reversed |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `payment_allocations` qua `allocated_by`.
- `debt_entries` (0..1) → (0..N) `payment_allocations` qua `debt_entry_id`.
- `users` (0..1) → (0..N) `payment_allocations` qua `deleted_by`.
- `orders` (0..1) → (0..N) `payment_allocations` qua `order_id`.
- `payments` (1) → (0..N) `payment_allocations` qua `payment_id`.
- `users` (0..1) → (0..N) `payment_allocations` qua `reversed_by`.

**Ràng buộc trên bản ghi**

- `ck_payment_allocations_allocated_amount`: `allocated_amount > 0`
- `ck_payment_allocations_allocation_type`: `allocation_type IN ('ORDER', 'DEBT')`
- `ck_payment_allocations_consumed_within_allocated`: `prepayment_consumed_amount <= allocated_amount`
- `ck_payment_allocations_prepayment_consumed_amount`: `prepayment_consumed_amount >= 0`
- `ck_payment_allocations_single_target`: `(allocation_type = 'ORDER' AND order_id IS NOT NULL AND debt_entry_id IS NULL) OR (allocation_type = 'DEBT' AND debt_entry_id IS NOT NULL AND order_id IS NULL AND prepayment_consumed_amount = 0)`
- `ck_payment_allocations_status`: `status IN ('ACTIVE', 'REVERSED')`
### `payments` — Payment

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `amount` | Decimal(18,2) |  | Không |  |
| `cancelled_at` | DateTime |  | Có |  |
| `checkout_url` | String(1500) |  | Có |  |
| `confirmation_source` | String(30) |  | Có | Enum: Staff, PayOsWebhook |
| `confirmed_at` | DateTime |  | Có |  |
| `confirmed_by` | Identifier | FK | Có | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Có | `users.id` |
| `currency` | String(3) |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `failed_at` | DateTime |  | Có |  |
| `initiated_at` | DateTime |  | Không |  |
| `last_reconciliation_attempt_at` | DateTime |  | Có |  |
| `note` | String(1000) |  | Có |  |
| `order_id` | Identifier | FK | Có | `orders.id` |
| `payer_farmer_profile_id` | Identifier | FK | Có | `farmer_profiles.id` |
| `payment_context` | String(30) |  | Không | Enum: OrderPayment, DebtRepayment |
| `payment_method` | String(20) |  | Không | Enum: Cash, PayOs, BankTransfer |
| `payment_number` | String(50) |  | Không |  |
| `provider` | String(30) |  | Có |  |
| `provider_metadata` | JSON |  | Có |  |
| `provider_order_code` | Integer64 |  | Có |  |
| `provider_payment_link_id` | String(150) |  | Có |  |
| `provider_transaction_id` | String(150) |  | Có |  |
| `status` | String(30) |  | Không | Enum: Pending, Paid, Failed, Cancelled, PartiallyRefunded, Refunded |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `payments` qua `confirmed_by`.
- `users` (0..1) → (0..N) `payments` qua `created_by`.
- `users` (0..1) → (0..N) `payments` qua `deleted_by`.
- `orders` (0..1) → (0..N) `payments` qua `order_id`.
- `farmer_profiles` (0..1) → (0..N) `payments` qua `payer_farmer_profile_id`.
- `stores` (1) → (0..N) `payments` qua `store_id`.

**Khóa duy nhất**

- `(provider_order_code)` khi `provider_order_code IS NOT NULL`.
- `(store_id, payment_number)`.

**Ràng buộc trên bản ghi**

- `ck_payments_amount`: `amount > 0`
- `ck_payments_confirmation_source`: `confirmation_source IN ('STAFF', 'PAYOS_WEBHOOK')`
- `ck_payments_order_context`: `(payment_context = 'ORDER_PAYMENT' AND order_id IS NOT NULL) OR (payment_context = 'DEBT_REPAYMENT' AND order_id IS NULL)`
- `ck_payments_payment_context`: `payment_context IN ('ORDER_PAYMENT', 'DEBT_REPAYMENT')`
- `ck_payments_payment_method`: `payment_method IN ('CASH', 'PAYOS', 'BANK_TRANSFER')`
- `ck_payments_status`: `status IN ('PENDING', 'PAID', 'FAILED', 'CANCELLED', 'PARTIALLY_REFUNDED', 'REFUNDED')`

## Giao hàng và truy vết lô

### `deliveries` — Delivery

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `address_line_snapshot` | String(500) |  | Không |  |
| `assigned_to_member_id` | Identifier | FK | Có | `store_members.id` |
| `cancel_reason` | String(1000) |  | Có |  |
| `cancelled_at` | DateTime |  | Có |  |
| `cancelled_by` | Identifier | FK | Có | `users.id` |
| `completed_at` | DateTime |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_number` | String(50) |  | Không |  |
| `dispatched_at` | DateTime |  | Có |  |
| `district_snapshot` | String(150) |  | Có |  |
| `latitude_snapshot` | Decimal(10,7) |  | Có |  |
| `longitude_snapshot` | Decimal(10,7) |  | Có |  |
| `note` | String(1000) |  | Có |  |
| `order_id` | Identifier | FK | Không | `orders.id` |
| `province_snapshot` | String(150) |  | Không |  |
| `recipient_name_snapshot` | String(150) |  | Không |  |
| `recipient_phone_snapshot` | String(20) |  | Không |  |
| `scheduled_at` | DateTime |  | Có |  |
| `status` | String(30) |  | Không | Enum: Draft, Assigned, OutForDelivery, PartiallyDelivered, RetryPending, Delivered, Cancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |
| `ward_snapshot` | String(150) |  | Có |  |

**Quan hệ**

- `store_members` (0..1) → (0..N) `deliveries` qua `assigned_to_member_id`.
- `users` (0..1) → (0..N) `deliveries` qua `cancelled_by`.
- `users` (1) → (0..N) `deliveries` qua `created_by`.
- `users` (0..1) → (0..N) `deliveries` qua `deleted_by`.
- `orders` (1) → (0..N) `deliveries` qua `order_id`.
- `stores` (1) → (0..N) `deliveries` qua `store_id`.

**Khóa duy nhất**

- `(store_id, delivery_number)`.

**Ràng buộc trên bản ghi**

- `ck_deliveries_status`: `status IN ('DRAFT', 'ASSIGNED', 'OUT_FOR_DELIVERY', 'PARTIALLY_DELIVERED', 'RETRY_PENDING', 'DELIVERED', 'CANCELLED')`
### `delivery_attempt_items` — DeliveryAttemptItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `attempted_base_quantity` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivered_base_quantity` | Integer64 |  | Không |  |
| `delivery_attempt_id` | Identifier | FK | Không | `delivery_attempts.id` |
| `delivery_item_lot_allocation_id` | Identifier | FK | Không | `delivery_item_lot_allocations.id` |
| `failed_base_quantity` | Integer64 |  | Không |  |
| `note` | String(500) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `delivery_attempt_items` qua `deleted_by`.
- `delivery_attempts` (1) → (0..N) `delivery_attempt_items` qua `delivery_attempt_id`.
- `delivery_item_lot_allocations` (1) → (0..N) `delivery_attempt_items` qua `delivery_item_lot_allocation_id`.

**Khóa duy nhất**

- `(delivery_attempt_id, delivery_item_lot_allocation_id)` khi `deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_delivery_attempt_items_attempted_base_quantity`: `attempted_base_quantity > 0`
- `ck_delivery_attempt_items_delivered_base_quantity`: `delivered_base_quantity >= 0`
- `ck_delivery_attempt_items_delivered_failed_within_attempted`: `delivered_base_quantity + failed_base_quantity <= attempted_base_quantity`
- `ck_delivery_attempt_items_failed_base_quantity`: `failed_base_quantity >= 0`
### `delivery_attempts` — DeliveryAttempt

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `attempt_number` | Integer |  | Không |  |
| `attempted_by_member_id` | Identifier | FK | Không | `store_members.id` |
| `completed_at` | DateTime |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_id` | Identifier | FK | Không | `deliveries.id` |
| `failure_reason_code` | String(50) |  | Có |  |
| `note` | String(1000) |  | Có |  |
| `proof_image_url` | String(1000) |  | Có |  |
| `receiver_name` | String(150) |  | Có |  |
| `sale_stock_movement_id` | Identifier | FK | Có | `stock_movements.id` |
| `started_at` | DateTime |  | Không |  |
| `status` | String(30) |  | Không | Enum: InProgress, Success, PartialSuccess, Failed, Cancelled |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `store_members` (1) → (0..N) `delivery_attempts` qua `attempted_by_member_id`.
- `users` (0..1) → (0..N) `delivery_attempts` qua `deleted_by`.
- `deliveries` (1) → (0..N) `delivery_attempts` qua `delivery_id`.
- `stock_movements` (0..1) → (0..N) `delivery_attempts` qua `sale_stock_movement_id`.

**Khóa duy nhất**

- `(delivery_id, attempt_number)`.

**Ràng buộc trên bản ghi**

- `ck_delivery_attempts_status`: `status IN ('IN_PROGRESS', 'SUCCESS', 'PARTIAL_SUCCESS', 'FAILED', 'CANCELLED')`
### `delivery_incidents` — DeliveryIncident

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `affected_base_quantity` | Integer64 |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_attempt_id` | Identifier | FK | Có | `delivery_attempts.id` |
| `delivery_id` | Identifier | FK | Không | `deliveries.id` |
| `delivery_item_lot_allocation_id` | Identifier | FK | Có | `delivery_item_lot_allocations.id` |
| `description` | String(1000) |  | Không |  |
| `evidence_image_url` | String(1000) |  | Có |  |
| `incident_type` | String(40) |  | Không | Enum: CustomerAbsent, Unreachable, CustomerRefused, Damaged, Weather, VehicleIssue, AddressIssue, Other |
| `related_stock_movement_id` | Identifier | FK | Có | `stock_movements.id` |
| `reported_at` | DateTime |  | Không |  |
| `reported_by` | Identifier | FK | Không | `users.id` |
| `resolution_note` | String(1000) |  | Có |  |
| `resolution_type` | String(40) |  | Có | Enum: RetryDelivery, ReplaceGoods, ReturnToStore, WriteOff, CancelRemainder, NoAction, Other |
| `resolved_at` | DateTime |  | Có |  |
| `resolved_by` | Identifier | FK | Có | `users.id` |
| `status` | String(20) |  | Không | Enum: Open, Resolved |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `delivery_incidents` qua `deleted_by`.
- `delivery_attempts` (0..1) → (0..N) `delivery_incidents` qua `delivery_attempt_id`.
- `deliveries` (1) → (0..N) `delivery_incidents` qua `delivery_id`.
- `delivery_item_lot_allocations` (0..1) → (0..N) `delivery_incidents` qua `delivery_item_lot_allocation_id`.
- `stock_movements` (0..1) → (0..N) `delivery_incidents` qua `related_stock_movement_id`.
- `users` (1) → (0..N) `delivery_incidents` qua `reported_by`.
- `users` (0..1) → (0..N) `delivery_incidents` qua `resolved_by`.

**Ràng buộc trên bản ghi**

- `ck_delivery_incidents_incident_type`: `incident_type IN ('CUSTOMER_ABSENT', 'UNREACHABLE', 'CUSTOMER_REFUSED', 'DAMAGED', 'WEATHER', 'VEHICLE_ISSUE', 'ADDRESS_ISSUE', 'OTHER')`
- `ck_delivery_incidents_resolution_type`: `resolution_type IN ('RETRY_DELIVERY', 'REPLACE_GOODS', 'RETURN_TO_STORE', 'WRITE_OFF', 'CANCEL_REMAINDER', 'NO_ACTION', 'OTHER')`
- `ck_delivery_incidents_status`: `status IN ('OPEN', 'RESOLVED')`
### `delivery_item_lot_allocations` — DeliveryItemLotAllocation

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `allocated_base_quantity` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivered_base_quantity` | Integer64 |  | Không |  |
| `delivery_item_id` | Identifier | FK | Không | `delivery_items.id` |
| `inventory_lot_id` | Identifier | FK | Không | `inventory_lots.id` |
| `inventory_reservation_item_id` | Identifier | FK | Có | `inventory_reservation_items.id` |
| `released_base_quantity` | Integer64 |  | Không |  |
| `status` | String(30) |  | Không | Enum: Allocated, PartiallyDelivered, Delivered, Released, Cancelled |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `delivery_item_lot_allocations` qua `deleted_by`.
- `delivery_items` (1) → (0..N) `delivery_item_lot_allocations` qua `delivery_item_id`.
- `inventory_lots` (1) → (0..N) `delivery_item_lot_allocations` qua `inventory_lot_id`.
- `inventory_reservation_items` (0..1) → (0..N) `delivery_item_lot_allocations` qua `inventory_reservation_item_id`.

**Ràng buộc trên bản ghi**

- `ck_delivery_item_lot_allocations_allocated_base_quantity`: `allocated_base_quantity > 0`
- `ck_delivery_item_lot_allocations_delivered_base_quantity`: `delivered_base_quantity >= 0`
- `ck_delivery_item_lot_allocations_released_base_quantity`: `released_base_quantity >= 0`
- `ck_delivery_item_lot_allocations_status`: `status IN ('ALLOCATED', 'PARTIALLY_DELIVERED', 'DELIVERED', 'RELEASED', 'CANCELLED')`
- `ck_delivery_item_lot_allocations_within_allocated`: `delivered_base_quantity + released_base_quantity <= allocated_base_quantity`
### `delivery_items` — DeliveryItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `cancelled_base_quantity` | Integer64 |  | Không |  |
| `conversion_to_base_snapshot` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivered_base_quantity` | Integer64 |  | Không |  |
| `delivery_id` | Identifier | FK | Không | `deliveries.id` |
| `order_item_id` | Identifier | FK | Không | `order_items.id` |
| `planned_base_quantity` | Integer64 |  | Không |  |
| `planned_quantity` | Integer64 |  | Không |  |
| `status` | String(30) |  | Không | Enum: Pending, PartiallyDelivered, Delivered, Cancelled, PartiallyCancelled |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `delivery_items` qua `deleted_by`.
- `deliveries` (1) → (0..N) `delivery_items` qua `delivery_id`.
- `order_items` (1) → (0..N) `delivery_items` qua `order_item_id`.

**Ràng buộc trên bản ghi**

- `ck_delivery_items_cancelled_base_quantity`: `cancelled_base_quantity >= 0`
- `ck_delivery_items_conversion_to_base_snapshot`: `conversion_to_base_snapshot > 0`
- `ck_delivery_items_delivered_base_quantity`: `delivered_base_quantity >= 0`
- `ck_delivery_items_delivered_cancelled_within_planned`: `delivered_base_quantity + cancelled_base_quantity <= planned_base_quantity`
- `ck_delivery_items_planned_base_quantity`: `planned_base_quantity = planned_quantity * conversion_to_base_snapshot`
- `ck_delivery_items_planned_quantity`: `planned_quantity > 0`
- `ck_delivery_items_status`: `status IN ('PENDING', 'PARTIALLY_DELIVERED', 'DELIVERED', 'CANCELLED', 'PARTIALLY_CANCELLED')`

## Hạn mức tín dụng và công nợ

### `credit_limit_histories` — CreditLimitHistory

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `changed_at` | DateTime |  | Không |  |
| `changed_by` | Identifier | FK | Không | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_credit_profile_id` | Identifier | FK | Không | `farmer_credit_profiles.id` |
| `new_credit_limit` | Decimal(18,2) |  | Không |  |
| `new_credit_tier_id` | Identifier | FK | Có | `credit_tiers.id` |
| `old_credit_limit` | Decimal(18,2) |  | Không |  |
| `old_credit_tier_id` | Identifier | FK | Có | `credit_tiers.id` |
| `reason` | String(1000) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `credit_limit_histories` qua `changed_by`.
- `users` (0..1) → (0..N) `credit_limit_histories` qua `deleted_by`.
- `farmer_credit_profiles` (1) → (0..N) `credit_limit_histories` qua `farmer_credit_profile_id`.
- `credit_tiers` (0..1) → (0..N) `credit_limit_histories` qua `new_credit_tier_id`.
- `credit_tiers` (0..1) → (0..N) `credit_limit_histories` qua `old_credit_tier_id`.

**Ràng buộc trên bản ghi**

- `ck_credit_limit_histories_new_credit_limit`: `new_credit_limit >= 0`
- `ck_credit_limit_histories_old_credit_limit`: `old_credit_limit >= 0`
### `credit_reservations` — CreditReservation

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `amount_consumed` | Decimal(18,2) |  | Không |  |
| `amount_released` | Decimal(18,2) |  | Không |  |
| `amount_reserved` | Decimal(18,2) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_credit_profile_id` | Identifier | FK | Không | `farmer_credit_profiles.id` |
| `order_id` | Identifier | FK | Không | `orders.id` |
| `release_reason` | String(500) |  | Có |  |
| `released_at` | DateTime |  | Có |  |
| `released_by` | Identifier | FK | Có | `users.id` |
| `reserved_at` | DateTime |  | Không |  |
| `reserved_by` | Identifier | FK | Không | `users.id` |
| `status` | String(30) |  | Không | Enum: Active, PartiallyConsumed, Consumed, Released, Cancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `credit_reservations` qua `deleted_by`.
- `farmer_credit_profiles` (1) → (0..N) `credit_reservations` qua `farmer_credit_profile_id`.
- `orders` (1) → (0..N) `credit_reservations` qua `order_id`.
- `users` (0..1) → (0..N) `credit_reservations` qua `released_by`.
- `users` (1) → (0..N) `credit_reservations` qua `reserved_by`.
- `stores` (1) → (0..N) `credit_reservations` qua `store_id`.

**Khóa duy nhất**

- `(order_id)` khi `status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_credit_reservations_amount_consumed`: `amount_consumed >= 0`
- `ck_credit_reservations_amount_released`: `amount_released >= 0`
- `ck_credit_reservations_amount_reserved`: `amount_reserved >= 0`
- `ck_credit_reservations_consumed_released_within_reserved`: `amount_consumed + amount_released <= amount_reserved`
- `ck_credit_reservations_status`: `status IN ('ACTIVE', 'PARTIALLY_CONSUMED', 'CONSUMED', 'RELEASED', 'CANCELLED')`
### `credit_tiers` — CreditTier

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `code` | String(30) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `default_credit_limit` | Decimal(18,2) |  | Không |  |
| `default_payment_term_days` | Integer |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String(500) |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `name` | String(100) |  | Không |  |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `credit_tiers` qua `deleted_by`.
- `stores` (1) → (0..N) `credit_tiers` qua `store_id`.

**Khóa duy nhất**

- `(store_id, code)`.

**Ràng buộc trên bản ghi**

- `ck_credit_tiers_default_credit_limit`: `default_credit_limit >= 0`
- `ck_credit_tiers_default_payment_term_days`: `default_payment_term_days >= 0`
### `debt_accounts` — DebtAccount

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `current_balance` | Decimal(18,2) |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_profile_id` | Identifier | FK | Không | `farmer_profiles.id` |
| `last_transaction_at` | DateTime |  | Có |  |
| `status` | String(20) |  | Không | Enum: Active, Blocked, Closed |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |
| `version` | Integer64 |  | Không | Phiên bản optimistic concurrency |

**Quan hệ**

- `users` (0..1) → (0..N) `debt_accounts` qua `deleted_by`.
- `farmer_profiles` (1) → (0..N) `debt_accounts` qua `farmer_profile_id`.
- `stores` (1) → (0..N) `debt_accounts` qua `store_id`.

**Khóa duy nhất**

- `(store_id, farmer_profile_id)`.

**Ràng buộc trên bản ghi**

- `ck_debt_accounts_current_balance`: `current_balance >= 0`
- `ck_debt_accounts_status`: `status IN ('ACTIVE', 'BLOCKED', 'CLOSED')`
### `debt_entries` — DebtEntry

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `debt_account_id` | Identifier | FK | Không | `debt_accounts.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_attempt_id` | Identifier | FK | Có | `delivery_attempts.id` |
| `delivery_id` | Identifier | FK | Có | `deliveries.id` |
| `due_date` | Date |  | Không |  |
| `entry_number` | String(50) | UQ | Không |  |
| `fulfillment_value` | Decimal(18,2) |  | Không |  |
| `order_id` | Identifier | FK | Có | `orders.id` |
| `original_amount` | Decimal(18,2) |  | Không |  |
| `outstanding_amount` | Decimal(18,2) |  | Không |  |
| `prepayment_applied_amount` | Decimal(18,2) |  | Không |  |
| `source_stock_movement_id` | Identifier | FK | Có | `stock_movements.id` |
| `source_type` | String(30) |  | Không | Enum: Delivery, Pickup, ManualAdjustment |
| `status` | String(30) |  | Không | Enum: Open, PartiallyPaid, Paid, Disputed, Adjusted, Cancelled |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `debt_entries` qua `created_by`.
- `debt_accounts` (1) → (0..N) `debt_entries` qua `debt_account_id`.
- `users` (0..1) → (0..N) `debt_entries` qua `deleted_by`.
- `delivery_attempts` (0..1) → (0..N) `debt_entries` qua `delivery_attempt_id`.
- `deliveries` (0..1) → (0..N) `debt_entries` qua `delivery_id`.
- `orders` (0..1) → (0..N) `debt_entries` qua `order_id`.
- `stock_movements` (0..1) → (0..N) `debt_entries` qua `source_stock_movement_id`.

**Khóa duy nhất**

- `(entry_number)`.
- `(source_stock_movement_id)` khi `source_stock_movement_id IS NOT NULL`.

**Ràng buộc trên bản ghi**

- `ck_debt_entries_fulfillment_value`: `fulfillment_value >= 0`
- `ck_debt_entries_original_amount`: `original_amount >= 0`
- `ck_debt_entries_original_amount_formula`: `original_amount = fulfillment_value - prepayment_applied_amount`
- `ck_debt_entries_outstanding_amount`: `outstanding_amount >= 0`
- `ck_debt_entries_outstanding_within_original`: `outstanding_amount <= original_amount`
- `ck_debt_entries_prepayment_applied_amount`: `prepayment_applied_amount >= 0`
- `ck_debt_entries_prepayment_within_fulfillment`: `prepayment_applied_amount <= fulfillment_value`
- `ck_debt_entries_source_references`: `(source_type = 'DELIVERY' AND order_id IS NOT NULL AND delivery_id IS NOT NULL AND source_stock_movement_id IS NOT NULL) OR (source_type = 'PICKUP' AND order_id IS NOT NULL AND source_stock_movement_id IS NOT NULL) OR source_type = 'MANUAL_ADJUSTMENT'`
- `ck_debt_entries_source_type`: `source_type IN ('DELIVERY', 'PICKUP', 'MANUAL_ADJUSTMENT')`
- `ck_debt_entries_status`: `status IN ('OPEN', 'PARTIALLY_PAID', 'PAID', 'DISPUTED', 'ADJUSTED', 'CANCELLED')`
### `debt_entry_actions` — DebtEntryAction

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `action_type` | String(30) |  | Không | Enum: Dispute, Keep, Adjust, Cancel, ChangeDueDate |
| `adjustment_amount` | Decimal(18,2) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `debt_entry_id` | Identifier | FK | Không | `debt_entries.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `new_due_date` | Date |  | Có |  |
| `old_due_date` | Date |  | Có |  |
| `previous_outstanding_amount` | Decimal(18,2) |  | Có |  |
| `reason` | String(1000) |  | Không |  |
| `resulting_outstanding_amount` | Decimal(18,2) |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `debt_entry_actions` qua `created_by`.
- `debt_entries` (1) → (0..N) `debt_entry_actions` qua `debt_entry_id`.
- `users` (0..1) → (0..N) `debt_entry_actions` qua `deleted_by`.

**Ràng buộc trên bản ghi**

- `ck_debt_entry_actions_action_type`: `action_type IN ('DISPUTE', 'KEEP', 'ADJUST', 'CANCEL', 'CHANGE_DUE_DATE')`
### `debt_transactions` — DebtTransaction

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `amount_delta` | Decimal(18,2) |  | Không |  |
| `balance_after` | Decimal(18,2) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Có | `users.id` |
| `debt_account_id` | Identifier | FK | Không | `debt_accounts.id` |
| `debt_entry_action_id` | Identifier | FK | Có | `debt_entry_actions.id` |
| `debt_entry_id` | Identifier | FK | Có | `debt_entries.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `note` | String(1000) |  | Có |  |
| `occurred_at` | DateTime |  | Không |  |
| `payment_allocation_id` | Identifier | FK | Có | `payment_allocations.id` |
| `reversal_of_transaction_id` | Identifier | FK | Có | `debt_transactions.id` |
| `sales_return_id` | Identifier | FK | Có | `sales_returns.id` |
| `status` | String(20) |  | Không | Enum: Posted, Reversed |
| `transaction_type` | String(30) |  | Không | Enum: CreditSale, Payment, AdjustmentIn, AdjustmentOut, Return, Reversal |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `debt_transactions` qua `created_by`.
- `debt_accounts` (1) → (0..N) `debt_transactions` qua `debt_account_id`.
- `debt_entry_actions` (0..1) → (0..N) `debt_transactions` qua `debt_entry_action_id`.
- `debt_entries` (0..1) → (0..N) `debt_transactions` qua `debt_entry_id`.
- `users` (0..1) → (0..N) `debt_transactions` qua `deleted_by`.
- `payment_allocations` (0..1) → (0..N) `debt_transactions` qua `payment_allocation_id`.
- `debt_transactions` (0..1) → (0..N) `debt_transactions` qua `reversal_of_transaction_id`.
- `sales_returns` (0..1) → (0..N) `debt_transactions` qua `sales_return_id`.

**Khóa duy nhất**

- `(payment_allocation_id)` khi `payment_allocation_id IS NOT NULL`.

**Ràng buộc trên bản ghi**

- `ck_debt_transactions_amount_delta`: `amount_delta <> 0`
- `ck_debt_transactions_balance_after`: `balance_after >= 0`
- `ck_debt_transactions_payment_allocation_source`: `transaction_type <> 'PAYMENT' OR payment_allocation_id IS NOT NULL`
- `ck_debt_transactions_status`: `status IN ('POSTED', 'REVERSED')`
- `ck_debt_transactions_transaction_type`: `transaction_type IN ('CREDIT_SALE', 'PAYMENT', 'ADJUSTMENT_IN', 'ADJUSTMENT_OUT', 'RETURN', 'REVERSAL')`
### `farmer_credit_profiles` — FarmerCreditProfile

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `approved_at` | DateTime |  | Không |  |
| `approved_by` | Identifier | FK | Không | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `credit_limit` | Decimal(18,2) |  | Không |  |
| `credit_tier_id` | Identifier | FK | Có | `credit_tiers.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_profile_id` | Identifier | FK | Không | `farmer_profiles.id` |
| `note` | String(1000) |  | Có |  |
| `status` | String(20) |  | Không | Enum: Active, Suspended, Blocked |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |
| `version` | Integer64 |  | Không | Phiên bản optimistic concurrency |

**Quan hệ**

- `users` (1) → (0..N) `farmer_credit_profiles` qua `approved_by`.
- `credit_tiers` (0..1) → (0..N) `farmer_credit_profiles` qua `credit_tier_id`.
- `users` (0..1) → (0..N) `farmer_credit_profiles` qua `deleted_by`.
- `farmer_profiles` (1) → (0..N) `farmer_credit_profiles` qua `farmer_profile_id`.
- `stores` (1) → (0..N) `farmer_credit_profiles` qua `store_id`.

**Khóa duy nhất**

- `(store_id, farmer_profile_id)`.

**Ràng buộc trên bản ghi**

- `ck_farmer_credit_profiles_credit_limit`: `credit_limit >= 0`
- `ck_farmer_credit_profiles_status`: `status IN ('ACTIVE', 'SUSPENDED', 'BLOCKED')`

## Trả hàng và hoàn tiền

### `refunds` — Refund

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `amount` | Decimal(18,2) |  | Không |  |
| `cancel_reason` | String(1000) |  | Có |  |
| `cancelled_at` | DateTime |  | Có |  |
| `cancelled_by` | Identifier | FK | Có | `users.id` |
| `completed_at` | DateTime |  | Có |  |
| `completed_by` | Identifier | FK | Có | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `currency` | String(3) |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `external_reference` | String(200) |  | Có |  |
| `note` | String(1000) |  | Có |  |
| `order_id` | Identifier | FK | Có | `orders.id` |
| `original_payment_id` | Identifier | FK | Có | `payments.id` |
| `proof_file_url` | String(1000) |  | Có |  |
| `refund_method` | String(30) |  | Không | Enum: Cash, BankTransfer, OtherExternal |
| `refund_number` | String(50) |  | Không |  |
| `requested_at` | DateTime |  | Không |  |
| `requested_by` | Identifier | FK | Không | `users.id` |
| `sales_return_id` | Identifier | FK | Có | `sales_returns.id` |
| `status` | String(30) |  | Không | Enum: Pending, Completed, Failed, Cancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `refunds` qua `cancelled_by`.
- `users` (0..1) → (0..N) `refunds` qua `completed_by`.
- `users` (0..1) → (0..N) `refunds` qua `deleted_by`.
- `orders` (0..1) → (0..N) `refunds` qua `order_id`.
- `payments` (0..1) → (0..N) `refunds` qua `original_payment_id`.
- `users` (1) → (0..N) `refunds` qua `requested_by`.
- `sales_returns` (0..1) → (0..N) `refunds` qua `sales_return_id`.
- `stores` (1) → (0..N) `refunds` qua `store_id`.

**Khóa duy nhất**

- `(store_id, refund_number)`.

**Ràng buộc trên bản ghi**

- `ck_refunds_amount`: `amount > 0`
- `ck_refunds_order_payment`: `order_id IS NULL OR original_payment_id IS NOT NULL`
- `ck_refunds_refund_method`: `refund_method IN ('CASH', 'BANK_TRANSFER', 'OTHER_EXTERNAL')`
- `ck_refunds_source`: `num_nonnulls(sales_return_id, order_id) = 1`
- `ck_refunds_status`: `status IN ('PENDING', 'COMPLETED', 'FAILED', 'CANCELLED')`
### `sales_return_items` — SalesReturnItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `condition_status` | String(30) |  | Không | Enum: PendingInspection, Resellable, Damaged, Expired, Unusable |
| `conversion_to_base_snapshot` | Integer64 |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `debt_adjustment_transaction_id` | Identifier | FK | Có | `debt_transactions.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `delivery_item_id` | Identifier | FK | Có | `delivery_items.id` |
| `delivery_item_lot_allocation_id` | Identifier | FK | Có | `delivery_item_lot_allocations.id` |
| `inspection_note` | String(1000) |  | Có |  |
| `inventory_disposition` | String(30) |  | Không | Enum: None, Restock, WriteOff |
| `inventory_lot_id` | Identifier | FK | Không | `inventory_lots.id` |
| `order_item_id` | Identifier | FK | Không | `order_items.id` |
| `original_cogs_unit_cost` | Decimal(20,6) |  | Có |  |
| `original_stock_movement_item_id` | Identifier | FK | Có | `stock_movement_items.id` |
| `reason_code` | String(50) |  | Không |  |
| `return_inventory_cost_value` | Decimal(20,6) |  | Có |  |
| `return_stock_movement_id` | Identifier | FK | Có | `stock_movements.id` |
| `return_value` | Decimal(18,2) |  | Không |  |
| `returned_base_quantity` | Integer64 |  | Không |  |
| `sales_return_id` | Identifier | FK | Không | `sales_returns.id` |
| `selling_unit_price_snapshot` | Decimal(18,2) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `debt_transactions` (0..1) → (0..N) `sales_return_items` qua `debt_adjustment_transaction_id`.
- `users` (0..1) → (0..N) `sales_return_items` qua `deleted_by`.
- `delivery_items` (0..1) → (0..N) `sales_return_items` qua `delivery_item_id`.
- `delivery_item_lot_allocations` (0..1) → (0..N) `sales_return_items` qua `delivery_item_lot_allocation_id`.
- `inventory_lots` (1) → (0..N) `sales_return_items` qua `inventory_lot_id`.
- `order_items` (1) → (0..N) `sales_return_items` qua `order_item_id`.
- `stock_movement_items` (0..1) → (0..N) `sales_return_items` qua `original_stock_movement_item_id`.
- `stock_movements` (0..1) → (0..N) `sales_return_items` qua `return_stock_movement_id`.
- `sales_returns` (1) → (0..N) `sales_return_items` qua `sales_return_id`.

**Ràng buộc trên bản ghi**

- `ck_sales_return_items_condition_status`: `condition_status IN ('PENDING_INSPECTION', 'RESELLABLE', 'DAMAGED', 'EXPIRED', 'UNUSABLE')`
- `ck_sales_return_items_conversion_to_base_snapshot`: `conversion_to_base_snapshot > 0`
- `ck_sales_return_items_inventory_disposition`: `inventory_disposition IN ('NONE', 'RESTOCK', 'WRITE_OFF')`
- `ck_sales_return_items_original_cogs_unit_cost`: `original_cogs_unit_cost IS NULL OR original_cogs_unit_cost >= 0`
- `ck_sales_return_items_return_inventory_cost_value`: `return_inventory_cost_value IS NULL OR return_inventory_cost_value >= 0`
- `ck_sales_return_items_return_value`: `return_value >= 0`
- `ck_sales_return_items_returned_base_quantity`: `returned_base_quantity > 0`
- `ck_sales_return_items_selling_unit_price_snapshot`: `selling_unit_price_snapshot >= 0`
- `ck_sales_return_items_single_fulfillment_source`: `(delivery_item_lot_allocation_id IS NULL) <> (original_stock_movement_item_id IS NULL)`
### `sales_returns` — SalesReturn

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `approved_at` | DateTime |  | Có |  |
| `approved_by` | Identifier | FK | Có | `users.id` |
| `cancel_reason` | String(1000) |  | Có |  |
| `cancelled_at` | DateTime |  | Có |  |
| `cancelled_by` | Identifier | FK | Có | `users.id` |
| `completed_at` | DateTime |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_profile_id` | Identifier | FK | Có | `farmer_profiles.id` |
| `inspected_at` | DateTime |  | Có |  |
| `inspected_by` | Identifier | FK | Có | `users.id` |
| `note` | String(1000) |  | Có |  |
| `order_id` | Identifier | FK | Không | `orders.id` |
| `reason_summary` | String(1000) |  | Có |  |
| `received_at` | DateTime |  | Có |  |
| `received_by` | Identifier | FK | Có | `users.id` |
| `requested_at` | DateTime |  | Không |  |
| `requested_by` | Identifier | FK | Không | `users.id` |
| `return_number` | String(50) |  | Không |  |
| `status` | String(30) |  | Không | Enum: Requested, Approved, Rejected, Received, Inspected, PartiallyResolved, Completed, Cancelled |
| `store_id` | Identifier | FK | Không | `stores.id` |
| `total_debt_adjustment` | Decimal(18,2) |  | Không |  |
| `total_refund_amount` | Decimal(18,2) |  | Không |  |
| `total_return_amount` | Decimal(18,2) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `sales_returns` qua `approved_by`.
- `users` (0..1) → (0..N) `sales_returns` qua `cancelled_by`.
- `users` (0..1) → (0..N) `sales_returns` qua `deleted_by`.
- `farmer_profiles` (0..1) → (0..N) `sales_returns` qua `farmer_profile_id`.
- `users` (0..1) → (0..N) `sales_returns` qua `inspected_by`.
- `orders` (1) → (0..N) `sales_returns` qua `order_id`.
- `users` (0..1) → (0..N) `sales_returns` qua `received_by`.
- `users` (1) → (0..N) `sales_returns` qua `requested_by`.
- `stores` (1) → (0..N) `sales_returns` qua `store_id`.

**Khóa duy nhất**

- `(store_id, return_number)`.

**Ràng buộc trên bản ghi**

- `ck_sales_returns_status`: `status IN ('REQUESTED', 'APPROVED', 'REJECTED', 'RECEIVED', 'INSPECTED', 'PARTIALLY_RESOLVED', 'COMPLETED', 'CANCELLED')`
- `ck_sales_returns_total_debt_adjustment`: `total_debt_adjustment >= 0`
- `ck_sales_returns_total_refund_amount`: `total_refund_amount >= 0`
- `ck_sales_returns_total_return_amount`: `total_return_amount >= 0`

## Chẩn đoán AI và duyệt khuyến nghị

### `agent_reviews` — AgentReview

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `ai_disease_id_snapshot` | Identifier | FK | Có | `diseases.id` |
| `comment` | String(2000) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `decision` | String(30) |  | Không | Enum: Confirmed, Corrected, Inconclusive |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `diagnosis_case_id` | Identifier | FK | Không | `diagnosis_cases.id` |
| `final_disease_id` | Identifier | FK | Có | `diseases.id` |
| `is_current` | Boolean |  | Không |  |
| `primary_ai_inference_id` | Identifier | FK | Có | `ai_inferences.id` |
| `reviewed_at` | DateTime |  | Không |  |
| `reviewer_member_id` | Identifier | FK | Không | `store_members.id` |
| `superseded_at` | DateTime |  | Có |  |
| `superseded_by_review_id` | Identifier | FK | Có | `agent_reviews.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `diseases` (0..1) → (0..N) `agent_reviews` qua `ai_disease_id_snapshot`.
- `users` (0..1) → (0..N) `agent_reviews` qua `deleted_by`.
- `diagnosis_cases` (1) → (0..N) `agent_reviews` qua `diagnosis_case_id`.
- `diseases` (0..1) → (0..N) `agent_reviews` qua `final_disease_id`.
- `ai_inferences` (0..1) → (0..N) `agent_reviews` qua `primary_ai_inference_id`.
- `store_members` (1) → (0..N) `agent_reviews` qua `reviewer_member_id`.
- `agent_reviews` (0..1) → (0..N) `agent_reviews` qua `superseded_by_review_id`.

**Khóa duy nhất**

- `(diagnosis_case_id)` khi `is_current = TRUE AND deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_agent_reviews_decision`: `decision IN ('CONFIRMED', 'CORRECTED', 'INCONCLUSIVE')`
- `ck_agent_reviews_decision_final_disease`: `(decision IN ('CONFIRMED', 'CORRECTED') AND final_disease_id IS NOT NULL) OR (decision = 'INCONCLUSIVE' AND final_disease_id IS NULL)`
### `ai_inferences` — AiInference

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `ai_model_id` | Identifier | FK | Không | `ai_models.id` |
| `ai_policy_config_id` | Identifier | FK | Không | `ai_policy_configs.id` |
| `confidence` | Decimal(7,6) |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `diagnosis_case_id` | Identifier | FK | Không | `diagnosis_cases.id` |
| `diagnosis_image_id` | Identifier | FK | Không | `diagnosis_images.id` |
| `inference_duration_ms` | Integer |  | Có |  |
| `inferred_at` | DateTime |  | Không |  |
| `passed_policy` | Boolean |  | Không |  |
| `predicted_class_label` | String(100) |  | Không |  |
| `predicted_disease_id` | Identifier | FK | Có | `diseases.id` |
| `raw_output` | JSON |  | Có |  |
| `status` | String(20) |  | Không | Enum: Success, Failed |
| `top_predictions` | JSON |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `ai_models` (1) → (0..N) `ai_inferences` qua `ai_model_id`.
- `ai_policy_configs` (1) → (0..N) `ai_inferences` qua `ai_policy_config_id`.
- `users` (0..1) → (0..N) `ai_inferences` qua `deleted_by`.
- `diagnosis_cases` (1) → (0..N) `ai_inferences` qua `diagnosis_case_id`.
- `diagnosis_images` (1) → (0..N) `ai_inferences` qua `diagnosis_image_id`.
- `diseases` (0..1) → (0..N) `ai_inferences` qua `predicted_disease_id`.

**Ràng buộc trên bản ghi**

- `ck_ai_inferences_confidence`: `confidence BETWEEN 0 AND 1`
- `ck_ai_inferences_inference_duration_ms`: `inference_duration_ms IS NULL OR inference_duration_ms >= 0`
- `ck_ai_inferences_status`: `status IN ('SUCCESS', 'FAILED')`
### `ai_models` — AiModel

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `architecture` | String(100) |  | Có |  |
| `class_labels` | JSON |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `deployed_at` | DateTime |  | Có |  |
| `framework` | String(50) |  | Không |  |
| `input_height` | Integer |  | Có |  |
| `input_width` | Integer |  | Có |  |
| `metrics` | JSON |  | Có |  |
| `model_storage_url` | String(1000) |  | Không |  |
| `name` | String(150) |  | Không |  |
| `retired_at` | DateTime |  | Có |  |
| `status` | String(30) |  | Không | Enum: Draft, Active, Retired |
| `updated_at` | DateTime |  | Không |  |
| `version` | String(50) |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `ai_models` qua `created_by`.
- `users` (0..1) → (0..N) `ai_models` qua `deleted_by`.

**Khóa duy nhất**

- `(name, version)`.

**Ràng buộc trên bản ghi**

- `ck_ai_models_status`: `status IN ('DRAFT', 'ACTIVE', 'RETIRED')`
### `ai_policy_configs` — AiPolicyConfig

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `ai_model_id` | Identifier | FK | Không | `ai_models.id` |
| `created_at` | DateTime |  | Không |  |
| `created_by` | Identifier | FK | Không | `users.id` |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `effective_from` | DateTime |  | Không |  |
| `effective_to` | DateTime |  | Có |  |
| `minimum_confidence` | Decimal(5,4) |  | Không |  |
| `minimum_margin` | Decimal(5,4) |  | Có |  |
| `parameters` | JSON |  | Có |  |
| `requires_human_review` | Boolean |  | Không |  |
| `status` | String(20) |  | Không | Enum: Draft, Active, Inactive |
| `top_k` | Integer |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `version` | String(50) |  | Không |  |

**Quan hệ**

- `ai_models` (1) → (0..N) `ai_policy_configs` qua `ai_model_id`.
- `users` (1) → (0..N) `ai_policy_configs` qua `created_by`.
- `users` (0..1) → (0..N) `ai_policy_configs` qua `deleted_by`.

**Khóa duy nhất**

- `(ai_model_id, version)`.

**Ràng buộc trên bản ghi**

- `ck_ai_policy_configs_effective_period`: `effective_to IS NULL OR effective_to > effective_from`
- `ck_ai_policy_configs_minimum_confidence`: `minimum_confidence BETWEEN 0 AND 1`
- `ck_ai_policy_configs_minimum_margin`: `minimum_margin IS NULL OR minimum_margin BETWEEN 0 AND 1`
- `ck_ai_policy_configs_status`: `status IN ('DRAFT', 'ACTIVE', 'INACTIVE')`
- `ck_ai_policy_configs_top_k`: `top_k > 0`
### `diagnosis_cases` — DiagnosisCase

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `case_number` | String(50) | UQ | Không |  |
| `completed_at` | DateTime |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `crop_type` | String(50) |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `farmer_note` | String(1000) |  | Có |  |
| `farmer_profile_id` | Identifier | FK | Không | `farmer_profiles.id` |
| `final_disease_id` | Identifier | FK | Có | `diseases.id` |
| `review_summary` | String(1000) |  | Có |  |
| `status` | String(30) |  | Không | Enum: Submitted, Processing, AiCompleted, UnderReview, Verified, Inconclusive, Failed, Cancelled |
| `submitted_at` | DateTime |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `diagnosis_cases` qua `deleted_by`.
- `farmer_profiles` (1) → (0..N) `diagnosis_cases` qua `farmer_profile_id`.
- `diseases` (0..1) → (0..N) `diagnosis_cases` qua `final_disease_id`.

**Khóa duy nhất**

- `(case_number)`.

**Ràng buộc trên bản ghi**

- `ck_diagnosis_cases_inconclusive_final_disease`: `status <> 'INCONCLUSIVE' OR final_disease_id IS NULL`
- `ck_diagnosis_cases_status`: `status IN ('SUBMITTED', 'PROCESSING', 'AI_COMPLETED', 'UNDER_REVIEW', 'VERIFIED', 'INCONCLUSIVE', 'FAILED', 'CANCELLED')`
- `ck_diagnosis_cases_verified_final_disease`: `status <> 'VERIFIED' OR final_disease_id IS NOT NULL`
### `diagnosis_images` — DiagnosisImage

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `diagnosis_case_id` | Identifier | FK | Không | `diagnosis_cases.id` |
| `file_name` | String(255) |  | Có |  |
| `file_size_bytes` | Integer64 |  | Có |  |
| `height` | Integer |  | Có |  |
| `image_url` | String(1500) |  | Không |  |
| `is_primary` | Boolean |  | Không |  |
| `mime_type` | String(100) |  | Có |  |
| `storage_key` | String(1000) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `uploaded_at` | DateTime |  | Không |  |
| `width` | Integer |  | Có |  |

**Quan hệ**

- `users` (0..1) → (0..N) `diagnosis_images` qua `deleted_by`.
- `diagnosis_cases` (1) → (0..N) `diagnosis_images` qua `diagnosis_case_id`.

**Khóa duy nhất**

- `(diagnosis_case_id)` khi `is_primary = TRUE AND deleted_at IS NULL`.
### `disease_treatments` — DiseaseTreatment

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `active_ingredient_id` | Identifier | FK | Có | `active_ingredients.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `disease_id` | Identifier | FK | Không | `diseases.id` |
| `instructions` | String |  | Không |  |
| `is_active` | Boolean |  | Không |  |
| `precautions` | String |  | Có |  |
| `priority` | Integer |  | Không |  |
| `title` | String(255) |  | Không |  |
| `treatment_type` | String(30) |  | Không | Enum: Cultural, Chemical, Preventive, Other |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `active_ingredients` (0..1) → (0..N) `disease_treatments` qua `active_ingredient_id`.
- `users` (0..1) → (0..N) `disease_treatments` qua `deleted_by`.
- `diseases` (1) → (0..N) `disease_treatments` qua `disease_id`.

**Ràng buộc trên bản ghi**

- `ck_disease_treatments_treatment_type`: `treatment_type IN ('CULTURAL', 'CHEMICAL', 'PREVENTIVE', 'OTHER')`
### `diseases` — Disease

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `causes` | String |  | Có |  |
| `code` | String(50) | UQ | Không |  |
| `created_at` | DateTime |  | Không |  |
| `crop_type` | String(50) |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `description` | String |  | Có |  |
| `is_active` | Boolean |  | Không |  |
| `is_healthy_class` | Boolean |  | Không |  |
| `name` | String(200) |  | Không |  |
| `prevention` | String |  | Có |  |
| `scientific_name` | String(255) |  | Có |  |
| `symptoms` | String |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (0..1) → (0..N) `diseases` qua `deleted_by`.

**Khóa duy nhất**

- `(code)`.
### `recommendation_items` — RecommendationItem

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `agent_review_id` | Identifier | FK | Không | `agent_reviews.id` |
| `approved_at` | DateTime |  | Không |  |
| `approved_by` | Identifier | FK | Không | `users.id` |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `diagnosis_case_id` | Identifier | FK | Không | `diagnosis_cases.id` |
| `disease_treatment_id` | Identifier | FK | Có | `disease_treatments.id` |
| `is_active` | Boolean |  | Không |  |
| `rank_order` | Integer |  | Không |  |
| `reason` | String(1000) |  | Có |  |
| `recommendation_type` | String(20) |  | Không | Enum: Treatment, Product |
| `store_product_id` | Identifier | FK | Có | `store_products.id` |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `agent_reviews` (1) → (0..N) `recommendation_items` qua `agent_review_id`.
- `users` (1) → (0..N) `recommendation_items` qua `approved_by`.
- `users` (0..1) → (0..N) `recommendation_items` qua `deleted_by`.
- `diagnosis_cases` (1) → (0..N) `recommendation_items` qua `diagnosis_case_id`.
- `disease_treatments` (0..1) → (0..N) `recommendation_items` qua `disease_treatment_id`.
- `store_products` (0..1) → (0..N) `recommendation_items` qua `store_product_id`.

**Ràng buộc trên bản ghi**

- `ck_recommendation_items_recommendation_type`: `recommendation_type IN ('TREATMENT', 'PRODUCT')`
- `ck_recommendation_items_single_target`: `(recommendation_type = 'TREATMENT' AND disease_treatment_id IS NOT NULL AND store_product_id IS NULL) OR (recommendation_type = 'PRODUCT' AND store_product_id IS NOT NULL AND disease_treatment_id IS NULL)`

## Nội dung, thông báo và nhật ký

### `articles` — Article

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `author_id` | Identifier | FK | Không | `users.id` |
| `content` | String |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `published_at` | DateTime |  | Có |  |
| `slug` | String(255) |  | Không |  |
| `status` | String(20) |  | Không | Enum: Draft, Published, Archived |
| `summary` | String(1000) |  | Có |  |
| `thumbnail_url` | String(1000) |  | Có |  |
| `title` | String(255) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `users` (1) → (0..N) `articles` qua `author_id`.
- `users` (0..1) → (0..N) `articles` qua `deleted_by`.

**Khóa duy nhất**

- `(slug)` khi `deleted_at IS NULL`.

**Ràng buộc trên bản ghi**

- `ck_articles_published_at`: `status <> 'PUBLISHED' OR published_at IS NOT NULL`
- `ck_articles_status`: `status IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')`
### `audit_logs` — AuditLog

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `action` | String(100) |  | Không |  |
| `actor_user_id` | Identifier | FK | Có | `users.id` |
| `correlation_id` | String(100) |  | Có |  |
| `entity_id` | Identifier |  | Có |  |
| `entity_type` | String(100) |  | Không |  |
| `ip_address` | String(64) |  | Có |  |
| `new_values` | JSON |  | Có |  |
| `occurred_at` | DateTime |  | Không |  |
| `old_values` | JSON |  | Có |  |
| `reason` | String(1000) |  | Có |  |
| `store_id` | Identifier | FK | Có | `stores.id` |
| `user_agent` | String(1000) |  | Có |  |

**Quan hệ**

- `users` (0..1) → (0..N) `audit_logs` qua `actor_user_id`.
- `stores` (0..1) → (0..N) `audit_logs` qua `store_id`.
### `contact_requests` — ContactRequest

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `assigned_to` | Identifier | FK | Có | `users.id` |
| `contact_email` | String(255) |  | Có |  |
| `contact_name` | String(150) |  | Không |  |
| `contact_phone` | String(20) |  | Có |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `message` | String |  | Không |  |
| `request_number` | String(50) | UQ | Không |  |
| `resolution_note` | String(2000) |  | Có |  |
| `resolved_at` | DateTime |  | Có |  |
| `status` | String(20) |  | Không | Enum: Open, InProgress, Resolved, Closed |
| `subject` | String(255) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK | Có | `users.id` |

**Quan hệ**

- `users` (0..1) → (0..N) `contact_requests` qua `assigned_to`.
- `users` (0..1) → (0..N) `contact_requests` qua `deleted_by`.
- `users` (0..1) → (0..N) `contact_requests` qua `user_id`.

**Khóa duy nhất**

- `(request_number)`.

**Ràng buộc trên bản ghi**

- `ck_contact_requests_status`: `status IN ('OPEN', 'IN_PROGRESS', 'RESOLVED', 'CLOSED')`
### `notification_outbox` — NotificationOutbox

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `attempts` | Integer |  | Không |  |
| `audit_log_id` | Identifier | FK, UQ | Không | `audit_logs.id` |
| `available_at` | DateTime |  | Không |  |
| `created_at` | DateTime |  | Không |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `last_error_code` | String(100) |  | Có |  |
| `processed_at` | DateTime |  | Có |  |
| `updated_at` | DateTime |  | Không |  |

**Quan hệ**

- `audit_logs` (1) → (0..1) `notification_outbox` qua `audit_log_id`.
- `users` (0..1) → (0..N) `notification_outbox` qua `deleted_by`.

**Khóa duy nhất**

- `(audit_log_id)`.

**Ràng buộc trên bản ghi**

- `ck_notification_outbox_attempts`: `attempts >= 0`
### `notifications` — Notification

| Thuộc tính | Kiểu logical | Khóa | NULL | Tham chiếu / ghi chú |
|---|---|---|---|---|
| `id` | Identifier | PK | Không |  |
| `created_at` | DateTime |  | Không |  |
| `data` | JSON |  | Có |  |
| `deduplication_key` | String(200) |  | Có |  |
| `deleted_at` | DateTime |  | Có |  |
| `deleted_by` | Identifier | FK | Có | `users.id` |
| `message` | String(2000) |  | Không |  |
| `notification_type` | String(50) |  | Không |  |
| `read_at` | DateTime |  | Có |  |
| `status` | String(20) |  | Không | Enum: Unread, Read, Archived |
| `title` | String(255) |  | Không |  |
| `updated_at` | DateTime |  | Không |  |
| `user_id` | Identifier | FK | Không | `users.id` |

**Quan hệ**

- `users` (0..1) → (0..N) `notifications` qua `deleted_by`.
- `users` (1) → (0..N) `notifications` qua `user_id`.

**Khóa duy nhất**

- `(user_id, deduplication_key)` khi `deduplication_key IS NOT NULL`.

**Ràng buộc trên bản ghi**

- `ck_notifications_status`: `status IN ('UNREAD', 'READ', 'ARCHIVED')`

## Kiểm chứng và tác động

- Đã dựng EF Core design-time model và đối chiếu số lượng với migration snapshot: 74 bảng, 1.079 thuộc tính, 278 FK.
- Đã kiểm tra mọi FK trỏ tới thực thể/thuộc tính tồn tại và mỗi thực thể có PK.
- Tài liệu được xuất từ mã nguồn local; không đọc dữ liệu nghiệp vụ, không kết nối database live, không sinh hoặc áp dụng migration.
- Chỉ bổ sung tài liệu/sơ đồ. Không thay đổi entity, cấu hình EF, API hoặc database. Không chạy bộ test nghiệp vụ vì không sửa logic ứng dụng.
