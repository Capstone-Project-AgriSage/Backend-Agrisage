# Danh sách bảng và quan hệ để vẽ tay

Theo bản `agrisage-fixed-layout-simplified.mmd`: **74 bảng, 76 quan hệ**.

Quy ước: `A (1) → (0..N) B` nghĩa là một A có thể có nhiều B; mỗi B thuộc đúng một A. `A (0..1) → (0..N) B` nghĩa là mỗi B có thể không gắn với A, hoặc gắn tối đa một A. `N` là nhiều.

Mỗi quan hệ chỉ liệt kê một lần, theo nhóm của bảng bên trái. Mũi tên chỉ quan hệ dữ liệu, không phải thứ tự xử lý. Đây là bản tổng quan đã ẩn 29 quan hệ; không phải danh sách đầy đủ FK trong database.

## 1. Tài khoản, phân quyền và cửa hàng

**Bảng (11):** `auth_challenges`, `auth_sessions`, `permissions`, `refresh_tokens`, `role_permissions`, `roles`, `store_member_permissions`, `store_members`, `stores`, `user_addresses`, `users`.

**Quan hệ:**

```text
auth_sessions (1) → (0..N) refresh_tokens
permissions (1) → (0..N) role_permissions
permissions (1) → (0..N) store_member_permissions
roles (1) → (0..N) role_permissions
roles (1) → (0..N) users
store_members (1) → (0..N) store_member_permissions
stores (1) → (0..N) store_members
stores (1) → (0..N) store_products
users (0..1) → (0..N) contact_requests
users (1) → (0..N) auth_challenges
users (1) → (0..N) auth_sessions
users (1) → (0..N) notifications
users (1) → (0..N) store_members
users (1) → (0..N) user_addresses
users (1) → (0..1) farmer_profiles
```

## 2. Khách hàng và bảng giá

**Bảng (6):** `customer_group_assignments`, `customer_group_price_lists`, `customer_groups`, `farmer_profiles`, `price_list_items`, `price_lists`.

**Quan hệ:**

```text
customer_groups (1) → (0..N) customer_group_assignments
customer_groups (1) → (0..N) customer_group_price_lists
farmer_profiles (0..1) → (0..N) orders
farmer_profiles (1) → (0..N) carts
farmer_profiles (1) → (0..N) customer_group_assignments
farmer_profiles (1) → (0..N) debt_accounts
farmer_profiles (1) → (0..N) diagnosis_cases
farmer_profiles (1) → (0..N) farmer_credit_profiles
price_lists (1) → (0..N) customer_group_price_lists
price_lists (1) → (0..N) price_list_items
```

## 3. Danh mục sản phẩm

**Bảng (9):** `active_ingredients`, `brands`, `categories`, `product_active_ingredients`, `product_packagings`, `product_reviews`, `products`, `store_products`, `units`.

**Quan hệ:**

```text
active_ingredients (1) → (0..N) product_active_ingredients
brands (0..1) → (0..N) products
categories (1) → (0..N) products
products (1) → (0..N) product_active_ingredients
products (1) → (0..N) product_packagings
products (1) → (0..N) store_products
store_products (1) → (0..N) inventory_lots
store_products (1) → (0..N) order_items
store_products (1) → (0..N) product_reviews
units (1) → (0..N) product_packagings
```

## 4. Nhà cung cấp và nhập hàng

**Bảng (3):** `goods_receipt_items`, `goods_receipts`, `suppliers`.

**Quan hệ:**

```text
goods_receipts (1) → (0..N) goods_receipt_items
suppliers (1) → (0..N) goods_receipts
```

## 5. Tồn kho

**Bảng (8):** `inventory_lot_balances`, `inventory_lots`, `inventory_reservation_items`, `inventory_reservations`, `stock_movement_items`, `stock_movements`, `stocktake_items`, `stocktakes`.

**Quan hệ:**

```text
inventory_lots (1) → (0..N) delivery_item_lot_allocations
inventory_lots (1) → (0..N) stock_movement_items
inventory_lots (1) → (0..1) inventory_lot_balances
inventory_reservations (1) → (0..N) inventory_reservation_items
stock_movement_items (0..1) → (0..N) sales_return_items
stock_movements (1) → (0..N) stock_movement_items
stocktakes (1) → (0..N) stocktake_items
```

## 6. Giỏ hàng, đơn hàng và thanh toán

**Bảng (6):** `cart_items`, `carts`, `order_items`, `orders`, `payment_allocations`, `payments`.

**Quan hệ:**

```text
carts (1) → (0..N) cart_items
order_items (1) → (0..N) delivery_items
orders (0..1) → (0..N) payment_allocations
orders (1) → (0..N) deliveries
orders (1) → (0..N) inventory_reservations
orders (1) → (0..N) order_items
orders (1) → (0..N) sales_returns
payment_allocations (0..1) → (0..N) debt_transactions
payments (0..1) → (0..N) refunds
payments (1) → (0..N) payment_allocations
```

## 7. Giao hàng

**Bảng (6):** `deliveries`, `delivery_attempt_items`, `delivery_attempts`, `delivery_incidents`, `delivery_item_lot_allocations`, `delivery_items`.

**Quan hệ:**

```text
deliveries (1) → (0..N) delivery_attempts
deliveries (1) → (0..N) delivery_incidents
deliveries (1) → (0..N) delivery_items
delivery_attempts (1) → (0..N) delivery_attempt_items
delivery_items (1) → (0..N) delivery_item_lot_allocations
```

## 8. Tín dụng và công nợ

**Bảng (8):** `credit_limit_histories`, `credit_reservations`, `credit_tiers`, `debt_accounts`, `debt_entries`, `debt_entry_actions`, `debt_transactions`, `farmer_credit_profiles`.

**Quan hệ:**

```text
credit_tiers (0..1) → (0..N) farmer_credit_profiles
debt_accounts (1) → (0..N) debt_entries
debt_accounts (1) → (0..N) debt_transactions
debt_entries (0..1) → (0..N) payment_allocations
debt_entries (1) → (0..N) debt_entry_actions
farmer_credit_profiles (1) → (0..N) credit_limit_histories
farmer_credit_profiles (1) → (0..N) credit_reservations
```

## 9. Trả hàng và hoàn tiền

**Bảng (3):** `refunds`, `sales_return_items`, `sales_returns`.

**Quan hệ:**

```text
sales_returns (0..1) → (0..N) refunds
sales_returns (1) → (0..N) sales_return_items
```

## 10. Chẩn đoán AI

**Bảng (9):** `agent_reviews`, `ai_inferences`, `ai_models`, `ai_policy_configs`, `diagnosis_cases`, `diagnosis_images`, `disease_treatments`, `diseases`, `recommendation_items`.

**Quan hệ:**

```text
agent_reviews (1) → (0..N) recommendation_items
ai_models (1) → (0..N) ai_policy_configs
ai_policy_configs (1) → (0..N) ai_inferences
diagnosis_cases (1) → (0..N) agent_reviews
diagnosis_cases (1) → (0..N) diagnosis_images
diagnosis_images (1) → (0..N) ai_inferences
diseases (1) → (0..N) disease_treatments
```

## 11. Nội dung, thông báo và nhật ký

**Bảng (5):** `articles`, `audit_logs`, `contact_requests`, `notification_outbox`, `notifications`.

**Quan hệ:**

```text
audit_logs (1) → (0..1) notification_outbox
```

`articles` chưa có đường nối trong sơ đồ nguồn. Một số quan hệ đi từ nhóm khác tới nhóm này, ví dụ `users → notifications`, chỉ xuất hiện tại nhóm của bảng bên trái.
