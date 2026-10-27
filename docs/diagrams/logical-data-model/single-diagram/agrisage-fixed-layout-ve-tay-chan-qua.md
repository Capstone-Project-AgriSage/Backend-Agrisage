# Danh sách bảng và quan hệ để vẽ tay

Theo bản `agrisage-fixed-layout-simplified.mmd`: **74 bảng, 76 quan hệ**.

Quy ước chân quạ (Crow's Foot), đọc ký hiệu sát từng đầu bảng:

- `||`: đúng một.
- `o|` hoặc `|o`: không hoặc một (vòng tròn nằm phía đường nối).
- `o{` hoặc `}o`: không hoặc nhiều (chân quạ nằm sát bảng).
- `--`: đường nối.

Ví dụ: `orders ||--o{ order_items` nghĩa là mỗi dòng hàng thuộc đúng một đơn; một đơn có thể có từ không đến nhiều dòng hàng.

Đây là 76 quan hệ của bản tổng quan rút gọn, không phải toàn bộ FK trong database. Mỗi quan hệ chỉ xuất hiện một lần, theo nhóm của bảng bên trái. Khi vẽ dọc, xoay ký hiệu theo đường nối nhưng giữ đúng đầu bảng.

## 1. Tài khoản, phân quyền và cửa hàng

**Bảng (11):** `auth_challenges`, `auth_sessions`, `permissions`, `refresh_tokens`, `role_permissions`, `roles`, `store_member_permissions`, `store_members`, `stores`, `user_addresses`, `users`.

**Quan hệ:**

```text
auth_sessions ||--o{ refresh_tokens
permissions ||--o{ role_permissions
permissions ||--o{ store_member_permissions
roles ||--o{ role_permissions
roles ||--o{ users
store_members ||--o{ store_member_permissions
stores ||--o{ store_members
stores ||--o{ store_products
users |o--o{ contact_requests
users ||--o{ auth_challenges
users ||--o{ auth_sessions
users ||--o{ notifications
users ||--o{ store_members
users ||--o{ user_addresses
users ||--o| farmer_profiles
```

## 2. Khách hàng và bảng giá

**Bảng (6):** `customer_group_assignments`, `customer_group_price_lists`, `customer_groups`, `farmer_profiles`, `price_list_items`, `price_lists`.

**Quan hệ:**

```text
customer_groups ||--o{ customer_group_assignments
customer_groups ||--o{ customer_group_price_lists
farmer_profiles |o--o{ orders
farmer_profiles ||--o{ carts
farmer_profiles ||--o{ customer_group_assignments
farmer_profiles ||--o{ debt_accounts
farmer_profiles ||--o{ diagnosis_cases
farmer_profiles ||--o{ farmer_credit_profiles
price_lists ||--o{ customer_group_price_lists
price_lists ||--o{ price_list_items
```

## 3. Danh mục sản phẩm

**Bảng (9):** `active_ingredients`, `brands`, `categories`, `product_active_ingredients`, `product_packagings`, `product_reviews`, `products`, `store_products`, `units`.

**Quan hệ:**

```text
active_ingredients ||--o{ product_active_ingredients
brands |o--o{ products
categories ||--o{ products
products ||--o{ product_active_ingredients
products ||--o{ product_packagings
products ||--o{ store_products
store_products ||--o{ inventory_lots
store_products ||--o{ order_items
store_products ||--o{ product_reviews
units ||--o{ product_packagings
```

## 4. Nhà cung cấp và nhập hàng

**Bảng (3):** `goods_receipt_items`, `goods_receipts`, `suppliers`.

**Quan hệ:**

```text
goods_receipts ||--o{ goods_receipt_items
suppliers ||--o{ goods_receipts
```

## 5. Tồn kho

**Bảng (8):** `inventory_lot_balances`, `inventory_lots`, `inventory_reservation_items`, `inventory_reservations`, `stock_movement_items`, `stock_movements`, `stocktake_items`, `stocktakes`.

**Quan hệ:**

```text
inventory_lots ||--o{ delivery_item_lot_allocations
inventory_lots ||--o{ stock_movement_items
inventory_lots ||--o| inventory_lot_balances
inventory_reservations ||--o{ inventory_reservation_items
stock_movement_items |o--o{ sales_return_items
stock_movements ||--o{ stock_movement_items
stocktakes ||--o{ stocktake_items
```

## 6. Giỏ hàng, đơn hàng và thanh toán

**Bảng (6):** `cart_items`, `carts`, `order_items`, `orders`, `payment_allocations`, `payments`.

**Quan hệ:**

```text
carts ||--o{ cart_items
order_items ||--o{ delivery_items
orders |o--o{ payment_allocations
orders ||--o{ deliveries
orders ||--o{ inventory_reservations
orders ||--o{ order_items
orders ||--o{ sales_returns
payment_allocations |o--o{ debt_transactions
payments |o--o{ refunds
payments ||--o{ payment_allocations
```

## 7. Giao hàng

**Bảng (6):** `deliveries`, `delivery_attempt_items`, `delivery_attempts`, `delivery_incidents`, `delivery_item_lot_allocations`, `delivery_items`.

**Quan hệ:**

```text
deliveries ||--o{ delivery_attempts
deliveries ||--o{ delivery_incidents
deliveries ||--o{ delivery_items
delivery_attempts ||--o{ delivery_attempt_items
delivery_items ||--o{ delivery_item_lot_allocations
```

## 8. Tín dụng và công nợ

**Bảng (8):** `credit_limit_histories`, `credit_reservations`, `credit_tiers`, `debt_accounts`, `debt_entries`, `debt_entry_actions`, `debt_transactions`, `farmer_credit_profiles`.

**Quan hệ:**

```text
credit_tiers |o--o{ farmer_credit_profiles
debt_accounts ||--o{ debt_entries
debt_accounts ||--o{ debt_transactions
debt_entries |o--o{ payment_allocations
debt_entries ||--o{ debt_entry_actions
farmer_credit_profiles ||--o{ credit_limit_histories
farmer_credit_profiles ||--o{ credit_reservations
```

## 9. Trả hàng và hoàn tiền

**Bảng (3):** `refunds`, `sales_return_items`, `sales_returns`.

**Quan hệ:**

```text
sales_returns |o--o{ refunds
sales_returns ||--o{ sales_return_items
```

## 10. Chẩn đoán AI

**Bảng (9):** `agent_reviews`, `ai_inferences`, `ai_models`, `ai_policy_configs`, `diagnosis_cases`, `diagnosis_images`, `disease_treatments`, `diseases`, `recommendation_items`.

**Quan hệ:**

```text
agent_reviews ||--o{ recommendation_items
ai_models ||--o{ ai_policy_configs
ai_policy_configs ||--o{ ai_inferences
diagnosis_cases ||--o{ agent_reviews
diagnosis_cases ||--o{ diagnosis_images
diagnosis_images ||--o{ ai_inferences
diseases ||--o{ disease_treatments
```

## 11. Nội dung, thông báo và nhật ký

**Bảng (5):** `articles`, `audit_logs`, `contact_requests`, `notification_outbox`, `notifications`.

**Quan hệ:**

```text
audit_logs ||--o| notification_outbox
```

`articles` chưa có đường nối trong sơ đồ nguồn. Một số quan hệ đi từ nhóm khác tới nhóm này, ví dụ `users ||--o{ notifications`, chỉ xuất hiện tại nhóm của bảng bên trái.
