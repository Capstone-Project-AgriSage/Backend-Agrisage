# Quan hệ ẩn trong bản Mermaid rút gọn

Nguồn: `agrisage-fixed-layout.mmd` (105 quan hệ). Bản rút gọn: `agrisage-fixed-layout-simplified.mmd` (76 quan hệ).

Giữ nguyên 74 bảng, 262 thuộc tính, khóa và cardinality của các quan hệ còn hiển thị. Không thêm quan hệ thay thế hoặc suy diễn quan hệ bắc cầu.

Đây là góc nhìn tổng quan. Các liên kết bên dưới chỉ bị ẩn trên sơ đồ; chúng vẫn tồn tại trong bản đầy đủ và không làm thay đổi mô hình dữ liệu. Những bảng vốn không có quan hệ trong nguồn vẫn được giữ; không có bảng đang kết nối nào trở thành bảng rời do bước rút gọn này.

## Liên kết phân loại, tham chiếu phụ

- `active_ingredients` → `disease_treatments`
- `categories` → `categories`
- `credit_tiers` → `customer_groups`
- `farmer_profiles` → `product_reviews`
- `store_members` → `agent_reviews`
- `store_products` → `recommendation_items`

## Liên kết chéo AI; giữ luồng ca chẩn đoán → ảnh → suy luận và ca → duyệt → khuyến nghị

- `ai_inferences` → `agent_reviews`
- `disease_treatments` → `recommendation_items`
- `diseases` → `agent_reviews`
- `diseases` → `ai_inferences`

## Tham chiếu sản phẩm/đóng gói từ các dòng chi tiết; giữ quan hệ chứng từ → dòng chi tiết

- `product_packagings` → `cart_items`
- `product_packagings` → `goods_receipt_items`
- `product_packagings` → `order_items`
- `product_packagings` → `price_list_items`
- `store_products` → `cart_items`
- `store_products` → `goods_receipt_items`
- `store_products` → `price_list_items`

## Truy vết chi tiết giữa các phân hệ; bản đầy đủ vẫn giữ các liên kết này

- `inventory_lots` → `goods_receipt_items`
- `inventory_lots` → `inventory_reservation_items`
- `inventory_lots` → `stocktake_items`
- `order_items` → `inventory_reservation_items`
- `order_items` → `sales_return_items`
- `delivery_item_lot_allocations` → `sales_return_items`
- `delivery_item_lot_allocations` → `delivery_attempt_items`

## Liên kết chéo tín dụng/công nợ/hoàn tiền; giữ các chuỗi chính và payment_allocations

- `debt_entries` → `debt_transactions`
- `orders` → `debt_entries`
- `orders` → `refunds`
- `orders` → `credit_reservations`
- `sales_returns` → `debt_transactions`
