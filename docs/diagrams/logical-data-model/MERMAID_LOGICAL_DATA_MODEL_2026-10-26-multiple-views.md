# AgriSage — Logical Data Model: bản trình bày gọn

Đã chỉnh theo góp ý giảm quan hệ gây rối. Phạm vi chi tiết vẫn là **74 thực thể**; số đường quan hệ hiển thị giảm từ **161 xuống 105**, lược **56 đường (35%)**. Không thay đổi database, entity, FK hay migration.

## Cách dùng trong báo cáo

- Dùng **sơ đồ đọc nhanh** để giới thiệu phạm vi: 23 thực thể chọn lọc, 29 quan hệ. Đây là một góc nhìn của logical model, không phải toàn bộ schema.
- Dùng **11 sơ đồ nghiệp vụ** bên dưới làm phần logical data model chi tiết. Mỗi thực thể xuất hiện đầy đủ ở đúng một nhóm; các khung chỉ có `id` là tham chiếu sang nhóm khác.
- File [Mermaid 74 thực thể](simplified/all-74-entities.mmd) phục vụ tra cứu tổng thể. Khi trình bày, dùng các nhóm bên dưới để tránh dồn toàn bộ mô hình lên một trang.

## Những điểm đã sửa

| Trước | Bản đã chỉnh |
|---|---|
| Nhiều đường về Store trên hầu hết giao dịch | Giữ Store–StoreMember và Store–StoreProduct; mô tả phạm vi Store dùng chung bằng chú thích |
| Các đường snapshot, địa chỉ gốc, lịch sử và đảo giao dịch cùng xuất hiện | Đưa vào bản đối chiếu; lược khỏi hình chính |
| Nhiều đường truy vết nối chéo qua các phân hệ | Chọn đường thể hiện cấu trúc nghiệp vụ chính; giữ các bảng nối và nguồn quan trọng |
| Quá nhiều thuộc tính kỹ thuật trong từng khung | Chỉ hiện định danh, FK được vẽ và thuộc tính nghiệp vụ tiêu biểu |
| Một hình chứa mọi chi tiết | Một hình đọc nhanh và 11 hình theo nghiệp vụ |

Các quan hệ được ẩn là **hợp lệ trong schema**; không có kết luận rằng chúng dư thừa trong database. Không áp dụng quy tắc máy móc “đã nối gián tiếp thì xóa nối trực tiếp”: các FK trực tiếp có thể mang ý nghĩa kiểm soát, lịch sử hoặc đối soát riêng.

## Quy ước và giới hạn

- `PK`: khóa chính; `FK`: thuộc tính tham chiếu; `UK`: thuộc tính duy nhất không điều kiện. `optional` nghĩa là có thể NULL.
- `||` = đúng một; `o|` = không hoặc một; `o{` = không hoặc nhiều. Ví dụ `A ||--o{ B`: mỗi B bắt buộc thuộc một A; A có thể chưa có B.
- Chỉ thể hiện thuộc tính và quan hệ được chọn cho góc nhìn này. Không có đường nối trên hình không có nghĩa là không có FK trong dữ liệu.
- Các bảng thuộc phạm vi cửa hàng vẫn có `store_id` theo thiết kế gốc. Việc lược đường Store không bỏ ràng buộc cửa hàng và không cho phép hard-code Store ID.
- Không vẽ thêm quan hệ suy diễn hoặc quan hệ N–N trực tiếp khi đã dùng thực thể liên kết để biểu diễn cùng một ý nghĩa.
- Giữ tên thực thể của project; dùng kiểu logic Identifier, String, Integer, Decimal, Boolean, Date và DateTime. Chi tiết độ dài, precision, nullability và FK không được chọn xem trong tài liệu nguồn.
- Logical model gồm thực thể, thuộc tính và quan hệ, độc lập chi tiết triển khai kỹ thuật theo [FPT IS](https://fpt-is.com/goc-nhin-so/data-modeling-la-gi/).

## Ràng buộc cần đọc cùng sơ đồ

- PaymentAllocation giữ cả hai đích Order và DebtEntry: chọn đích theo allocation type, không đồng thời phân bổ cùng một dòng cho cả hai. Payment có thể chưa có allocation, nên đường nối này không mô tả toàn bộ vòng đời thanh toán.
- GoodsReceiptItem có thể chưa gắn InventoryLot khi còn nháp. Tồn vật lý thay đổi qua StockMovement + StockMovementItem; liên kết tới chứng từ nguồn vẫn có trong bản chi tiết dù lược trên hình.
- SalesReturnItem giữ hai nguồn DeliveryItemLotAllocation và StockMovementItem gốc, với điều kiện **đúng một nguồn**. Không gom hai nguồn thành một quan hệ giả. Refund giữ cả Order, Payment và SalesReturn vì hoàn tiền có thể xuất phát từ đơn hủy.
- DebtTransaction vẫn là nhật ký biến động công nợ; chỉ tạo công nợ cho phần chưa thanh toán sau fulfillment thành công. Các FK tới Delivery/Attempt/StockMovement của DebtEntry được lược trên hình, vẫn có trong schema.
- AIInference → AgentReview → RecommendationItem giữ rõ kết quả máy, quyết định Human Review và khuyến nghị. Quyền duyệt thuộc StoreMember; không thêm role AI Reviewer. FK trực tiếp từ inference/recommendation về DiagnosisCase vẫn được lưu dù ẩn trên hình.
- Phân quyền thành viên: `is_granted = true` là Allow, `false` là Deny, `NULL` kế thừa Role.

## Sơ đồ đọc nhanh — góc nhìn chọn lọc

[Mã Mermaid riêng](simplified/00-overview.mmd).

```mermaid
erDiagram
    direction TB
    agent_reviews {
        Identifier id PK
        String decision
        Boolean is_current
    }
    debt_accounts {
        Identifier id PK
        Decimal current_balance
        String status
    }
    debt_entries {
        Identifier id PK
        String entry_number UK
        Decimal original_amount
    }
    deliveries {
        Identifier id PK
        String delivery_number
        String status
    }
    diagnosis_cases {
        Identifier id PK
        String case_number UK
        String crop_type
    }
    diseases {
        Identifier id PK
        String code UK
        String name
    }
    farmer_profiles {
        Identifier id PK
        Date date_of_birth "optional"
        String gender "optional"
    }
    goods_receipt_items {
        Identifier id PK
        Integer base_quantity
        Decimal base_unit_cost
    }
    goods_receipts {
        Identifier id PK
        String receipt_number
        DateTime received_at
    }
    inventory_lots {
        Identifier id PK
        String lot_number "optional"
        Date expiry_date "optional"
    }
    order_items {
        Identifier id PK
        Integer base_quantity
        Decimal unit_price
    }
    orders {
        Identifier id PK
        String order_number
        String customer_type
    }
    payment_allocations {
        Identifier id PK
        String allocation_type
        Decimal allocated_amount
    }
    payments {
        Identifier id PK
        String payment_number
        String payment_method
    }
    products {
        Identifier id PK
        String sku UK
        String name
    }
    recommendation_items {
        Identifier id PK
        String recommendation_type
        Integer rank_order
    }
    refunds {
        Identifier id PK
        String refund_number
        Decimal amount
    }
    sales_return_items {
        Identifier id PK
        Integer returned_base_quantity
        Decimal return_value
    }
    sales_returns {
        Identifier id PK
        String return_number
        Decimal total_return_amount
    }
    store_products {
        Identifier id PK
        String store_sku "optional"
        Boolean is_sellable
    }
    stores {
        Identifier id PK
        String code UK
        String name
    }
    suppliers {
        Identifier id PK
        String code "optional"
        String name
    }
    users {
        Identifier id PK
        String full_name
        String phone_number "optional"
    }
    agent_reviews ||--o{ recommendation_items : "agent_review_id"
    debt_accounts ||--o{ debt_entries : "debt_account_id"
    debt_entries |o--o{ payment_allocations : "debt_entry_id"
    diagnosis_cases ||--o{ agent_reviews : "diagnosis_case_id"
    diseases |o--o{ agent_reviews : "final_disease_id"
    farmer_profiles |o--o{ orders : "farmer_profile_id"
    farmer_profiles ||--o{ debt_accounts : "farmer_profile_id"
    farmer_profiles ||--o{ diagnosis_cases : "farmer_profile_id"
    goods_receipts ||--o{ goods_receipt_items : "goods_receipt_id"
    inventory_lots |o--o{ goods_receipt_items : "inventory_lot_id"
    order_items ||--o{ sales_return_items : "order_item_id"
    orders |o--o{ debt_entries : "order_id"
    orders |o--o{ payment_allocations : "order_id"
    orders |o--o{ refunds : "order_id"
    orders ||--o{ deliveries : "order_id"
    orders ||--o{ order_items : "order_id"
    orders ||--o{ sales_returns : "order_id"
    payments |o--o{ refunds : "original_payment_id"
    payments ||--o{ payment_allocations : "payment_id"
    products ||--o{ store_products : "product_id"
    sales_returns |o--o{ refunds : "sales_return_id"
    sales_returns ||--o{ sales_return_items : "sales_return_id"
    store_products |o--o{ recommendation_items : "store_product_id"
    store_products ||--o{ goods_receipt_items : "store_product_id"
    store_products ||--o{ inventory_lots : "store_product_id"
    store_products ||--o{ order_items : "store_product_id"
    stores ||--o{ store_products : "store_id"
    suppliers ||--o{ goods_receipts : "supplier_id"
    users ||--o| farmer_profiles : "user_id"
```

## Các sơ đồ nghiệp vụ

| Nhóm | Thực thể sở hữu | Quan hệ được vẽ | Mã Mermaid |
|---|---:|---:|---|
| Tài khoản, xác thực và cửa hàng | 11 | 11 | [01-identity-store.mmd](simplified/01-identity-store.mmd) |
| Khách hàng và bảng giá | 6 | 9 | [02-customers-pricing.mmd](simplified/02-customers-pricing.mmd) |
| Danh mục sản phẩm | 9 | 11 | [03-catalog.mmd](simplified/03-catalog.mmd) |
| Nhà cung cấp và nhập hàng | 3 | 5 | [04-receiving.mmd](simplified/04-receiving.mmd) |
| Tồn kho và đặt giữ hàng | 8 | 10 | [05-inventory.mmd](simplified/05-inventory.mmd) |
| Giỏ hàng, đơn hàng và thanh toán | 6 | 11 | [06-sales-payments.mmd](simplified/06-sales-payments.mmd) |
| Giao hàng và truy vết lô | 6 | 9 | [07-delivery.mmd](simplified/07-delivery.mmd) |
| Tín dụng và công nợ | 8 | 13 | [08-credit-debt.mmd](simplified/08-credit-debt.mmd) |
| Trả hàng và hoàn tiền | 3 | 8 | [09-returns.mmd](simplified/09-returns.mmd) |
| Chẩn đoán AI và Human Review | 9 | 15 | [10-ai-diagnosis.mmd](simplified/10-ai-diagnosis.mmd) |
| Nội dung và hệ thống | 5 | 3 | [11-content-system.mmd](simplified/11-content-system.mmd) |

## 1. Tài khoản, xác thực và cửa hàng

```mermaid
erDiagram
    direction TB
    auth_challenges {
        Identifier id PK
        Identifier user_id FK
        String purpose
        String channel
        DateTime expires_at
    }
    auth_sessions {
        Identifier id PK
        Identifier user_id FK
        DateTime expires_at
        DateTime revoked_at "optional"
    }
    permissions {
        Identifier id PK
        String code UK
        String module
        Boolean is_delegable
    }
    refresh_tokens {
        Identifier id PK
        Identifier session_id FK
        DateTime expires_at
        DateTime consumed_at "optional"
    }
    role_permissions {
        Identifier id PK
        Identifier role_id FK
        Identifier permission_id FK
        Boolean is_granted
    }
    roles {
        Identifier id PK
        String code
        String name
    }
    store_member_permissions {
        Identifier id PK
        Identifier store_member_id FK
        Identifier permission_id FK
        Boolean is_granted "optional"
    }
    store_members {
        Identifier id PK
        Identifier store_id FK
        Identifier user_id FK
        String employee_code "optional"
        Boolean can_review_ai
        String status
    }
    stores {
        Identifier id PK
        String code UK
        String name
    }
    user_addresses {
        Identifier id PK
        Identifier user_id FK
        String address_line
        String province
        Boolean is_default
    }
    users {
        Identifier id PK
        Identifier role_id FK
        String full_name
        String phone_number "optional"
        String status
    }
    auth_sessions ||--o{ refresh_tokens : "session_id"
    permissions ||--o{ role_permissions : "permission_id"
    permissions ||--o{ store_member_permissions : "permission_id"
    roles ||--o{ role_permissions : "role_id"
    roles ||--o{ users : "role_id"
    store_members ||--o{ store_member_permissions : "store_member_id"
    stores ||--o{ store_members : "store_id"
    users ||--o{ auth_challenges : "user_id"
    users ||--o{ auth_sessions : "user_id"
    users ||--o{ store_members : "user_id"
    users ||--o{ user_addresses : "user_id"
```

## 2. Khách hàng và bảng giá

```mermaid
erDiagram
    direction TB
    credit_tiers {
        Identifier id PK
    }
    customer_group_assignments {
        Identifier id PK
        Identifier customer_group_id FK
        Identifier farmer_profile_id FK
        DateTime effective_from
        DateTime effective_to "optional"
    }
    customer_group_price_lists {
        Identifier id PK
        Identifier customer_group_id FK
        Identifier price_list_id FK
        DateTime effective_from
        DateTime effective_to "optional"
    }
    customer_groups {
        Identifier id PK
        Identifier default_credit_tier_id FK "optional"
        String code
        String name
        Integer priority
    }
    farmer_profiles {
        Identifier id PK
        Identifier user_id FK,UK
        Date date_of_birth "optional"
        String gender "optional"
    }
    price_list_items {
        Identifier id PK
        Identifier price_list_id FK
        Identifier product_packaging_id FK
        Identifier store_product_id FK
        Decimal selling_price
    }
    price_lists {
        Identifier id PK
        String code
        String name
        String status
    }
    product_packagings {
        Identifier id PK
    }
    store_products {
        Identifier id PK
    }
    users {
        Identifier id PK
    }
    credit_tiers |o--o{ customer_groups : "default_credit_tier_id"
    customer_groups ||--o{ customer_group_assignments : "customer_group_id"
    customer_groups ||--o{ customer_group_price_lists : "customer_group_id"
    farmer_profiles ||--o{ customer_group_assignments : "farmer_profile_id"
    price_lists ||--o{ customer_group_price_lists : "price_list_id"
    price_lists ||--o{ price_list_items : "price_list_id"
    product_packagings ||--o{ price_list_items : "product_packaging_id"
    store_products ||--o{ price_list_items : "store_product_id"
    users ||--o| farmer_profiles : "user_id"
```

## 3. Danh mục sản phẩm

```mermaid
erDiagram
    direction TB
    active_ingredients {
        Identifier id PK
        String code "optional"
        String name
    }
    brands {
        Identifier id PK
        String code "optional"
        String name
    }
    categories {
        Identifier id PK
        Identifier parent_id FK "optional"
        String code
        String name
    }
    farmer_profiles {
        Identifier id PK
    }
    product_active_ingredients {
        Identifier id PK
        Identifier active_ingredient_id FK
        Identifier product_id FK
        String concentration "optional"
    }
    product_packagings {
        Identifier id PK
        Identifier product_id FK
        Identifier unit_id FK
        String packaging_name "optional"
        Integer conversion_to_base
        Boolean is_base_unit
    }
    product_reviews {
        Identifier id PK
        Identifier farmer_profile_id FK
        Identifier store_product_id FK
        Integer rating
        String status
    }
    products {
        Identifier id PK
        Identifier brand_id FK "optional"
        Identifier category_id FK
        String sku UK
        String name
        String status
    }
    store_products {
        Identifier id PK
        Identifier product_id FK
        Identifier store_id FK
        String store_sku "optional"
        Boolean is_sellable
    }
    stores {
        Identifier id PK
    }
    units {
        Identifier id PK
        String code UK
        String name
        String symbol "optional"
    }
    active_ingredients ||--o{ product_active_ingredients : "active_ingredient_id"
    brands |o--o{ products : "brand_id"
    categories |o--o{ categories : "parent_id"
    categories ||--o{ products : "category_id"
    farmer_profiles ||--o{ product_reviews : "farmer_profile_id"
    products ||--o{ product_active_ingredients : "product_id"
    products ||--o{ product_packagings : "product_id"
    products ||--o{ store_products : "product_id"
    store_products ||--o{ product_reviews : "store_product_id"
    stores ||--o{ store_products : "store_id"
    units ||--o{ product_packagings : "unit_id"
```

## 4. Nhà cung cấp và nhập hàng

```mermaid
erDiagram
    direction TB
    goods_receipt_items {
        Identifier id PK
        Identifier goods_receipt_id FK
        Identifier inventory_lot_id FK "optional"
        Identifier product_packaging_id FK
        Identifier store_product_id FK
        Integer base_quantity
        Decimal base_unit_cost
    }
    goods_receipts {
        Identifier id PK
        Identifier supplier_id FK
        String receipt_number
        DateTime received_at
        Decimal total_amount
        String status
    }
    inventory_lots {
        Identifier id PK
    }
    product_packagings {
        Identifier id PK
    }
    store_products {
        Identifier id PK
    }
    suppliers {
        Identifier id PK
        String code "optional"
        String name
        String phone_number "optional"
    }
    goods_receipts ||--o{ goods_receipt_items : "goods_receipt_id"
    inventory_lots |o--o{ goods_receipt_items : "inventory_lot_id"
    product_packagings ||--o{ goods_receipt_items : "product_packaging_id"
    store_products ||--o{ goods_receipt_items : "store_product_id"
    suppliers ||--o{ goods_receipts : "supplier_id"
```

## 5. Tồn kho và đặt giữ hàng

```mermaid
erDiagram
    direction TB
    inventory_lot_balances {
        Identifier id PK
        Identifier inventory_lot_id FK,UK
        Integer quantity_on_hand
        Integer quantity_reserved
        Decimal total_cost_value
    }
    inventory_lots {
        Identifier id PK
        Identifier store_product_id FK
        String lot_number "optional"
        Date expiry_date "optional"
        String status
    }
    inventory_reservation_items {
        Identifier id PK
        Identifier inventory_lot_id FK
        Identifier inventory_reservation_id FK
        Identifier order_item_id FK
        Integer base_quantity_reserved
        Integer base_quantity_consumed
        Integer base_quantity_released
    }
    inventory_reservations {
        Identifier id PK
        Identifier order_id FK
        String status
        DateTime reserved_at
    }
    order_items {
        Identifier id PK
    }
    orders {
        Identifier id PK
    }
    stock_movement_items {
        Identifier id PK
        Identifier inventory_lot_id FK
        Identifier stock_movement_id FK
        Integer quantity_delta_base
        Decimal unit_cost_snapshot
    }
    stock_movements {
        Identifier id PK
        String movement_number
        String movement_type
        String status
    }
    stocktake_items {
        Identifier id PK
        Identifier inventory_lot_id FK
        Identifier stocktake_id FK
        Integer system_quantity_snapshot
        Integer counted_quantity "optional"
        Integer difference_quantity "optional"
    }
    stocktakes {
        Identifier id PK
        String stocktake_number
        String status
    }
    store_products {
        Identifier id PK
    }
    inventory_lots ||--o{ inventory_reservation_items : "inventory_lot_id"
    inventory_lots ||--o{ stock_movement_items : "inventory_lot_id"
    inventory_lots ||--o{ stocktake_items : "inventory_lot_id"
    inventory_lots ||--o| inventory_lot_balances : "inventory_lot_id"
    inventory_reservations ||--o{ inventory_reservation_items : "inventory_reservation_id"
    order_items ||--o{ inventory_reservation_items : "order_item_id"
    orders ||--o{ inventory_reservations : "order_id"
    stock_movements ||--o{ stock_movement_items : "stock_movement_id"
    stocktakes ||--o{ stocktake_items : "stocktake_id"
    store_products ||--o{ inventory_lots : "store_product_id"
```

## 6. Giỏ hàng, đơn hàng và thanh toán

```mermaid
erDiagram
    direction TB
    cart_items {
        Identifier id PK
        Identifier cart_id FK
        Identifier product_packaging_id FK
        Identifier store_product_id FK
        Integer quantity
    }
    carts {
        Identifier id PK
        Identifier farmer_profile_id FK
        String status
    }
    debt_entries {
        Identifier id PK
    }
    farmer_profiles {
        Identifier id PK
    }
    order_items {
        Identifier id PK
        Identifier order_id FK
        Identifier product_packaging_id FK
        Identifier store_product_id FK
        Integer base_quantity
        Decimal unit_price
        Decimal line_total_amount
    }
    orders {
        Identifier id PK
        Identifier farmer_profile_id FK "optional"
        String order_number
        String customer_type
        String fulfillment_type
        Decimal total_amount
        String status
    }
    payment_allocations {
        Identifier id PK
        Identifier debt_entry_id FK "optional"
        Identifier order_id FK "optional"
        Identifier payment_id FK
        String allocation_type
        Decimal allocated_amount
        String status
    }
    payments {
        Identifier id PK
        String payment_number
        String payment_method
        Decimal amount
        String status
    }
    product_packagings {
        Identifier id PK
    }
    store_products {
        Identifier id PK
    }
    carts ||--o{ cart_items : "cart_id"
    debt_entries |o--o{ payment_allocations : "debt_entry_id"
    farmer_profiles |o--o{ orders : "farmer_profile_id"
    farmer_profiles ||--o{ carts : "farmer_profile_id"
    orders |o--o{ payment_allocations : "order_id"
    orders ||--o{ order_items : "order_id"
    payments ||--o{ payment_allocations : "payment_id"
    product_packagings ||--o{ cart_items : "product_packaging_id"
    product_packagings ||--o{ order_items : "product_packaging_id"
    store_products ||--o{ cart_items : "store_product_id"
    store_products ||--o{ order_items : "store_product_id"
```

## 7. Giao hàng và truy vết lô

```mermaid
erDiagram
    direction TB
    deliveries {
        Identifier id PK
        Identifier order_id FK
        String delivery_number
        String status
    }
    delivery_attempt_items {
        Identifier id PK
        Identifier delivery_attempt_id FK
        Identifier delivery_item_lot_allocation_id FK
        Integer attempted_base_quantity
        Integer delivered_base_quantity
        Integer failed_base_quantity
    }
    delivery_attempts {
        Identifier id PK
        Identifier delivery_id FK
        Integer attempt_number
        String status
    }
    delivery_incidents {
        Identifier id PK
        Identifier delivery_id FK
        String incident_type
        Integer affected_base_quantity "optional"
        String status
    }
    delivery_item_lot_allocations {
        Identifier id PK
        Identifier delivery_item_id FK
        Identifier inventory_lot_id FK
        Integer allocated_base_quantity
        Integer delivered_base_quantity
        String status
    }
    delivery_items {
        Identifier id PK
        Identifier delivery_id FK
        Identifier order_item_id FK
        Integer planned_base_quantity
        Integer delivered_base_quantity
        String status
    }
    inventory_lots {
        Identifier id PK
    }
    order_items {
        Identifier id PK
    }
    orders {
        Identifier id PK
    }
    deliveries ||--o{ delivery_attempts : "delivery_id"
    deliveries ||--o{ delivery_incidents : "delivery_id"
    deliveries ||--o{ delivery_items : "delivery_id"
    delivery_attempts ||--o{ delivery_attempt_items : "delivery_attempt_id"
    delivery_item_lot_allocations ||--o{ delivery_attempt_items : "delivery_item_lot_allocation_id"
    delivery_items ||--o{ delivery_item_lot_allocations : "delivery_item_id"
    inventory_lots ||--o{ delivery_item_lot_allocations : "inventory_lot_id"
    order_items ||--o{ delivery_items : "order_item_id"
    orders ||--o{ deliveries : "order_id"
```

## 8. Tín dụng và công nợ

```mermaid
erDiagram
    direction TB
    credit_limit_histories {
        Identifier id PK
        Identifier farmer_credit_profile_id FK
        Decimal old_credit_limit
        Decimal new_credit_limit
        DateTime changed_at
    }
    credit_reservations {
        Identifier id PK
        Identifier farmer_credit_profile_id FK
        Identifier order_id FK
        Decimal amount_reserved
        Decimal amount_consumed
        Decimal amount_released
        String status
    }
    credit_tiers {
        Identifier id PK
        String code
        Decimal default_credit_limit
        Integer default_payment_term_days
    }
    debt_accounts {
        Identifier id PK
        Identifier farmer_profile_id FK
        Decimal current_balance
        String status
    }
    debt_entries {
        Identifier id PK
        Identifier debt_account_id FK
        Identifier order_id FK "optional"
        String entry_number UK
        Decimal original_amount
        Decimal outstanding_amount
        Date due_date
        String status
    }
    debt_entry_actions {
        Identifier id PK
        Identifier debt_entry_id FK
        String action_type
        Decimal adjustment_amount "optional"
        String reason
    }
    debt_transactions {
        Identifier id PK
        Identifier debt_account_id FK
        Identifier debt_entry_id FK "optional"
        Identifier payment_allocation_id FK "optional"
        Identifier sales_return_id FK "optional"
        String transaction_type
        Decimal amount_delta
        Decimal balance_after
    }
    farmer_credit_profiles {
        Identifier id PK
        Identifier credit_tier_id FK "optional"
        Identifier farmer_profile_id FK
        Decimal credit_limit
        String status
    }
    farmer_profiles {
        Identifier id PK
    }
    orders {
        Identifier id PK
    }
    payment_allocations {
        Identifier id PK
    }
    sales_returns {
        Identifier id PK
    }
    credit_tiers |o--o{ farmer_credit_profiles : "credit_tier_id"
    debt_accounts ||--o{ debt_entries : "debt_account_id"
    debt_accounts ||--o{ debt_transactions : "debt_account_id"
    debt_entries |o--o{ debt_transactions : "debt_entry_id"
    debt_entries ||--o{ debt_entry_actions : "debt_entry_id"
    farmer_credit_profiles ||--o{ credit_limit_histories : "farmer_credit_profile_id"
    farmer_credit_profiles ||--o{ credit_reservations : "farmer_credit_profile_id"
    farmer_profiles ||--o{ debt_accounts : "farmer_profile_id"
    farmer_profiles ||--o{ farmer_credit_profiles : "farmer_profile_id"
    orders |o--o{ debt_entries : "order_id"
    orders ||--o{ credit_reservations : "order_id"
    payment_allocations |o--o{ debt_transactions : "payment_allocation_id"
    sales_returns |o--o{ debt_transactions : "sales_return_id"
```

## 9. Trả hàng và hoàn tiền

```mermaid
erDiagram
    direction TB
    delivery_item_lot_allocations {
        Identifier id PK
    }
    order_items {
        Identifier id PK
    }
    orders {
        Identifier id PK
    }
    payments {
        Identifier id PK
    }
    refunds {
        Identifier id PK
        Identifier order_id FK "optional"
        Identifier original_payment_id FK "optional"
        Identifier sales_return_id FK "optional"
        String refund_number
        Decimal amount
        String status
    }
    sales_return_items {
        Identifier id PK
        Identifier delivery_item_lot_allocation_id FK "optional"
        Identifier order_item_id FK
        Identifier original_stock_movement_item_id FK "optional"
        Identifier sales_return_id FK
        Integer returned_base_quantity
        Decimal return_value
        String inventory_disposition
    }
    sales_returns {
        Identifier id PK
        Identifier order_id FK
        String return_number
        Decimal total_return_amount
        String status
    }
    stock_movement_items {
        Identifier id PK
    }
    delivery_item_lot_allocations |o--o{ sales_return_items : "delivery_item_lot_allocation_id"
    order_items ||--o{ sales_return_items : "order_item_id"
    orders |o--o{ refunds : "order_id"
    orders ||--o{ sales_returns : "order_id"
    payments |o--o{ refunds : "original_payment_id"
    sales_returns |o--o{ refunds : "sales_return_id"
    sales_returns ||--o{ sales_return_items : "sales_return_id"
    stock_movement_items |o--o{ sales_return_items : "original_stock_movement_item_id"
```

## 10. Chẩn đoán AI và Human Review

```mermaid
erDiagram
    direction TB
    active_ingredients {
        Identifier id PK
    }
    agent_reviews {
        Identifier id PK
        Identifier diagnosis_case_id FK
        Identifier final_disease_id FK "optional"
        Identifier primary_ai_inference_id FK "optional"
        Identifier reviewer_member_id FK
        String decision
        Boolean is_current
    }
    ai_inferences {
        Identifier id PK
        Identifier ai_policy_config_id FK
        Identifier diagnosis_image_id FK
        Identifier predicted_disease_id FK "optional"
        Decimal confidence
        Boolean passed_policy
        String status
    }
    ai_models {
        Identifier id PK
        String name
        String version
        String status
    }
    ai_policy_configs {
        Identifier id PK
        Identifier ai_model_id FK
        String version
        Decimal minimum_confidence
        Boolean requires_human_review
    }
    diagnosis_cases {
        Identifier id PK
        Identifier farmer_profile_id FK
        String case_number UK
        String crop_type
        String status
    }
    diagnosis_images {
        Identifier id PK
        Identifier diagnosis_case_id FK
        String image_url
        Boolean is_primary
    }
    disease_treatments {
        Identifier id PK
        Identifier active_ingredient_id FK "optional"
        Identifier disease_id FK
        String title
        String treatment_type
        String instructions
    }
    diseases {
        Identifier id PK
        String code UK
        String name
        Boolean is_healthy_class
    }
    farmer_profiles {
        Identifier id PK
    }
    recommendation_items {
        Identifier id PK
        Identifier agent_review_id FK
        Identifier disease_treatment_id FK "optional"
        Identifier store_product_id FK "optional"
        String recommendation_type
        Integer rank_order
        Boolean is_active
    }
    store_members {
        Identifier id PK
    }
    store_products {
        Identifier id PK
    }
    active_ingredients |o--o{ disease_treatments : "active_ingredient_id"
    agent_reviews ||--o{ recommendation_items : "agent_review_id"
    ai_inferences |o--o{ agent_reviews : "primary_ai_inference_id"
    ai_models ||--o{ ai_policy_configs : "ai_model_id"
    ai_policy_configs ||--o{ ai_inferences : "ai_policy_config_id"
    diagnosis_cases ||--o{ agent_reviews : "diagnosis_case_id"
    diagnosis_cases ||--o{ diagnosis_images : "diagnosis_case_id"
    diagnosis_images ||--o{ ai_inferences : "diagnosis_image_id"
    disease_treatments |o--o{ recommendation_items : "disease_treatment_id"
    diseases |o--o{ agent_reviews : "final_disease_id"
    diseases |o--o{ ai_inferences : "predicted_disease_id"
    diseases ||--o{ disease_treatments : "disease_id"
    farmer_profiles ||--o{ diagnosis_cases : "farmer_profile_id"
    store_members ||--o{ agent_reviews : "reviewer_member_id"
    store_products |o--o{ recommendation_items : "store_product_id"
```

## 11. Nội dung và hệ thống

```mermaid
erDiagram
    direction TB
    articles {
        Identifier id PK
        String title
        String slug
        String status
    }
    audit_logs {
        Identifier id PK
        String action
        String entity_type
        Identifier entity_id "optional"
        DateTime occurred_at
    }
    contact_requests {
        Identifier id PK
        Identifier user_id FK "optional"
        String request_number UK
        String subject
        String status
    }
    notification_outbox {
        Identifier id PK
        Identifier audit_log_id FK,UK
        DateTime available_at
        DateTime processed_at "optional"
        Integer attempts
    }
    notifications {
        Identifier id PK
        Identifier user_id FK
        String notification_type
        String title
        String status
    }
    users {
        Identifier id PK
    }
    audit_logs ||--o| notification_outbox : "audit_log_id"
    users |o--o{ contact_requests : "user_id"
    users ||--o{ notifications : "user_id"
```

## Đối chiếu và nguồn

- [Danh sách từng quan hệ được lược và lý do](simplified/RELATIONSHIP_REVIEW.md).
- [Bản Mermaid chi tiết trước khi giản lược](agrisage-logical-model-2026-10-26-detailed.mmd).
- [Tài liệu chi tiết trước khi giản lược](MERMAID_LOGICAL_DATA_MODEL_2026-10-26-detailed.md).
- [Thiết kế database](../../reference/DATABASE_DESIGN.md), [mở rộng xác thực](../../reference/AUTH_OPERATIONS_SCHEMA.md), metadata local `model.json` và migration `PermissionOverrideInheritance`.

Đã kiểm tra: giữ đủ 74 thực thể trong 11 nhóm, không nhân đôi thực thể sở hữu, mọi quan hệ được vẽ đều có FK thật trong metadata, bội số và tính optional giữ theo nguồn, các quan hệ đã lược có danh sách đối chiếu. Không truy cập database live.
