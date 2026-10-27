# Đối chiếu quan hệ đã lược khỏi bản trình bày

Bản trước: 161 đường. Bản gọn: 105 đường. Lược 56 đường hiển thị.

Tất cả các FK dưới đây vẫn tồn tại trong mô hình nguồn. Bảng này không đề xuất xóa FK hoặc thuộc tính.

| Thực thể con | Thuộc tính FK | Tham chiếu | Lý do ẩn |
|---|---|---|---|
| `agent_reviews` | `superseded_by_review_id` | `agent_reviews` | Lịch sử, thay thế hoặc đảo giao dịch; tra trong bản chi tiết. |
| `ai_inferences` | `ai_model_id` | `ai_models` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `credit_limit_histories` | `new_credit_tier_id` | `credit_tiers` | Lịch sử, thay thế hoặc đảo giao dịch; tra trong bản chi tiết. |
| `credit_limit_histories` | `old_credit_tier_id` | `credit_tiers` | Lịch sử, thay thế hoặc đảo giao dịch; tra trong bản chi tiết. |
| `orders` | `customer_group_id_snapshot` | `customer_groups` | Bối cảnh/snapshot tại thời điểm giao dịch; tra trong bản chi tiết. |
| `debt_transactions` | `debt_entry_action_id` | `debt_entry_actions` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `debt_transactions` | `reversal_of_transaction_id` | `debt_transactions` | Lịch sử, thay thế hoặc đảo giao dịch; tra trong bản chi tiết. |
| `sales_return_items` | `debt_adjustment_transaction_id` | `debt_transactions` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `debt_entries` | `delivery_id` | `deliveries` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `stock_movements` | `delivery_id` | `deliveries` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `debt_entries` | `delivery_attempt_id` | `delivery_attempts` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `delivery_incidents` | `delivery_attempt_id` | `delivery_attempts` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `delivery_incidents` | `delivery_item_lot_allocation_id` | `delivery_item_lot_allocations` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `sales_return_items` | `delivery_item_id` | `delivery_items` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `ai_inferences` | `diagnosis_case_id` | `diagnosis_cases` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `recommendation_items` | `diagnosis_case_id` | `diagnosis_cases` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `agent_reviews` | `ai_disease_id_snapshot` | `diseases` | Bối cảnh/snapshot tại thời điểm giao dịch; tra trong bản chi tiết. |
| `diagnosis_cases` | `final_disease_id` | `diseases` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `payments` | `payer_farmer_profile_id` | `farmer_profiles` | Tác nhân hoặc bên tham gia phụ; lược khỏi góc nhìn cấu trúc chính. |
| `sales_returns` | `farmer_profile_id` | `farmer_profiles` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `stock_movements` | `goods_receipt_id` | `goods_receipts` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `sales_return_items` | `inventory_lot_id` | `inventory_lots` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `delivery_item_lot_allocations` | `inventory_reservation_item_id` | `inventory_reservation_items` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `product_reviews` | `order_item_id` | `order_items` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `carts` | `converted_order_id` | `orders` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `payments` | `order_id` | `orders` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `stock_movements` | `order_id` | `orders` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `orders` | `price_list_id_snapshot` | `price_lists` | Bối cảnh/snapshot tại thời điểm giao dịch; tra trong bản chi tiết. |
| `stock_movements` | `sales_return_id` | `sales_returns` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `debt_entries` | `source_stock_movement_id` | `stock_movements` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `delivery_attempts` | `sale_stock_movement_id` | `stock_movements` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `delivery_incidents` | `related_stock_movement_id` | `stock_movements` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `sales_return_items` | `return_stock_movement_id` | `stock_movements` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `stock_movements` | `reversal_of_movement_id` | `stock_movements` | Lịch sử, thay thế hoặc đảo giao dịch; tra trong bản chi tiết. |
| `stock_movements` | `stocktake_id` | `stocktakes` | Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày. |
| `deliveries` | `assigned_to_member_id` | `store_members` | Tác nhân hoặc bên tham gia phụ; lược khỏi góc nhìn cấu trúc chính. |
| `delivery_attempts` | `attempted_by_member_id` | `store_members` | Tác nhân hoặc bên tham gia phụ; lược khỏi góc nhìn cấu trúc chính. |
| `audit_logs` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `carts` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `credit_reservations` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `credit_tiers` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `customer_groups` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `debt_accounts` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `deliveries` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `farmer_credit_profiles` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `goods_receipts` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `inventory_reservations` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `orders` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `payments` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `price_lists` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `refunds` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `sales_returns` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `stock_movements` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `stocktakes` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `suppliers` | `store_id` | `stores` | Phạm vi Store dùng chung; lược đường nối lặp về Store. |
| `orders` | `source_address_id` | `user_addresses` | Bối cảnh/snapshot tại thời điểm giao dịch; tra trong bản chi tiết. |
