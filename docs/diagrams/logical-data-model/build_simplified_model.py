"""Build presentation views; never change the schema or the complete source diagrams."""

import json
import re
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parent
OUT = ROOT / "simplified"
SOURCE = ROOT / "agrisage-logical-model-2026-10-26-detailed.mmd"
DOCUMENT = ROOT / "MERMAID_LOGICAL_DATA_MODEL_2026-10-26.md"
COMBINED = ROOT / "agrisage-logical-model-2026-10-26.mmd"

# A view policy, not a list of foreign keys to remove from the database.
# Each entry names the FK columns whose relationship is drawn for that child.
KEEP = {
    "auth_challenges": "user_id",
    "auth_sessions": "user_id",
    "refresh_tokens": "session_id",
    "role_permissions": "role_id permission_id",
    "store_member_permissions": "store_member_id permission_id",
    "store_members": "store_id user_id",
    "user_addresses": "user_id",
    "users": "role_id",
    "customer_group_assignments": "customer_group_id farmer_profile_id",
    "customer_group_price_lists": "customer_group_id price_list_id",
    "customer_groups": "default_credit_tier_id",
    "farmer_profiles": "user_id",
    "price_list_items": "price_list_id product_packaging_id store_product_id",
    "categories": "parent_id",
    "product_active_ingredients": "active_ingredient_id product_id",
    "product_packagings": "product_id unit_id",
    "product_reviews": "farmer_profile_id store_product_id",
    "products": "brand_id category_id",
    "store_products": "product_id store_id",
    "goods_receipt_items": "goods_receipt_id inventory_lot_id product_packaging_id store_product_id",
    "goods_receipts": "supplier_id",
    "inventory_lot_balances": "inventory_lot_id",
    "inventory_lots": "store_product_id",
    "inventory_reservation_items": "inventory_lot_id inventory_reservation_id order_item_id",
    "inventory_reservations": "order_id",
    "stock_movement_items": "inventory_lot_id stock_movement_id",
    "stocktake_items": "inventory_lot_id stocktake_id",
    "cart_items": "cart_id product_packaging_id store_product_id",
    "carts": "farmer_profile_id",
    "order_items": "order_id product_packaging_id store_product_id",
    "orders": "farmer_profile_id",
    "payment_allocations": "debt_entry_id order_id payment_id",
    "deliveries": "order_id",
    "delivery_attempt_items": "delivery_attempt_id delivery_item_lot_allocation_id",
    "delivery_attempts": "delivery_id",
    "delivery_incidents": "delivery_id",
    "delivery_item_lot_allocations": "delivery_item_id inventory_lot_id",
    "delivery_items": "delivery_id order_item_id",
    "credit_limit_histories": "farmer_credit_profile_id",
    "credit_reservations": "farmer_credit_profile_id order_id",
    "debt_accounts": "farmer_profile_id",
    "debt_entries": "debt_account_id order_id",
    "debt_entry_actions": "debt_entry_id",
    "debt_transactions": "debt_account_id debt_entry_id payment_allocation_id sales_return_id",
    "farmer_credit_profiles": "credit_tier_id farmer_profile_id",
    "refunds": "order_id original_payment_id sales_return_id",
    "sales_return_items": "delivery_item_lot_allocation_id order_item_id original_stock_movement_item_id sales_return_id",
    "sales_returns": "order_id",
    "agent_reviews": "diagnosis_case_id final_disease_id primary_ai_inference_id reviewer_member_id",
    "ai_inferences": "ai_policy_config_id diagnosis_image_id predicted_disease_id",
    "ai_policy_configs": "ai_model_id",
    "diagnosis_cases": "farmer_profile_id",
    "diagnosis_images": "diagnosis_case_id",
    "disease_treatments": "active_ingredient_id disease_id",
    "recommendation_items": "agent_review_id disease_treatment_id store_product_id",
    "contact_requests": "user_id",
    "notification_outbox": "audit_log_id",
    "notifications": "user_id",
}

# Selected business attributes; FK attributes are added from KEEP below.
FIELDS = {
    "active_ingredients": "code name",
    "agent_reviews": "decision is_current",
    "ai_inferences": "confidence passed_policy status",
    "ai_models": "name version status",
    "ai_policy_configs": "version minimum_confidence requires_human_review",
    "articles": "title slug status",
    "audit_logs": "action entity_type entity_id occurred_at",
    "auth_challenges": "purpose channel expires_at",
    "auth_sessions": "expires_at revoked_at",
    "brands": "code name",
    "cart_items": "quantity",
    "carts": "status",
    "categories": "code name",
    "contact_requests": "request_number subject status",
    "credit_limit_histories": "old_credit_limit new_credit_limit changed_at",
    "credit_reservations": "amount_reserved amount_consumed amount_released status",
    "credit_tiers": "code default_credit_limit default_payment_term_days",
    "customer_group_assignments": "effective_from effective_to",
    "customer_group_price_lists": "effective_from effective_to",
    "customer_groups": "code name priority",
    "debt_accounts": "current_balance status",
    "debt_entries": "entry_number original_amount outstanding_amount due_date status",
    "debt_entry_actions": "action_type adjustment_amount reason",
    "debt_transactions": "transaction_type amount_delta balance_after",
    "deliveries": "delivery_number status",
    "delivery_attempt_items": "attempted_base_quantity delivered_base_quantity failed_base_quantity",
    "delivery_attempts": "attempt_number status",
    "delivery_incidents": "incident_type affected_base_quantity status",
    "delivery_item_lot_allocations": "allocated_base_quantity delivered_base_quantity status",
    "delivery_items": "planned_base_quantity delivered_base_quantity status",
    "diagnosis_cases": "case_number crop_type status",
    "diagnosis_images": "image_url is_primary",
    "disease_treatments": "title treatment_type instructions",
    "diseases": "code name is_healthy_class",
    "farmer_credit_profiles": "credit_limit status",
    "farmer_profiles": "date_of_birth gender",
    "goods_receipt_items": "base_quantity base_unit_cost",
    "goods_receipts": "receipt_number received_at total_amount status",
    "inventory_lot_balances": "quantity_on_hand quantity_reserved total_cost_value",
    "inventory_lots": "lot_number expiry_date status",
    "inventory_reservation_items": "base_quantity_reserved base_quantity_consumed base_quantity_released",
    "inventory_reservations": "status reserved_at",
    "notification_outbox": "available_at processed_at attempts",
    "notifications": "notification_type title status",
    "order_items": "base_quantity unit_price line_total_amount",
    "orders": "order_number customer_type fulfillment_type total_amount status",
    "payment_allocations": "allocation_type allocated_amount status",
    "payments": "payment_number payment_method amount status",
    "permissions": "code module is_delegable",
    "price_list_items": "selling_price",
    "price_lists": "code name status",
    "product_active_ingredients": "concentration",
    "product_packagings": "packaging_name conversion_to_base is_base_unit",
    "product_reviews": "rating status",
    "products": "sku name status",
    "recommendation_items": "recommendation_type rank_order is_active",
    "refresh_tokens": "expires_at consumed_at",
    "refunds": "refund_number amount status",
    "role_permissions": "is_granted",
    "roles": "code name",
    "sales_return_items": "returned_base_quantity return_value inventory_disposition",
    "sales_returns": "return_number total_return_amount status",
    "stock_movement_items": "quantity_delta_base unit_cost_snapshot",
    "stock_movements": "movement_number movement_type status",
    "stocktake_items": "system_quantity_snapshot counted_quantity difference_quantity",
    "stocktakes": "stocktake_number status",
    "store_member_permissions": "is_granted",
    "store_members": "employee_code can_review_ai status",
    "store_products": "store_sku is_sellable",
    "stores": "code name",
    "suppliers": "code name phone_number",
    "units": "code name symbol",
    "user_addresses": "address_line province is_default",
    "users": "full_name phone_number status",
}

GROUP_TITLES = [
    "Tài khoản, xác thực và cửa hàng", "Khách hàng và bảng giá",
    "Danh mục sản phẩm", "Nhà cung cấp và nhập hàng", "Tồn kho và đặt giữ hàng",
    "Giỏ hàng, đơn hàng và thanh toán", "Giao hàng và truy vết lô",
    "Tín dụng và công nợ", "Trả hàng và hoàn tiền",
    "Chẩn đoán AI và Human Review", "Nội dung và hệ thống",
]


def run():
    OUT.mkdir(exist_ok=True)
    if not SOURCE.exists():
        SOURCE.write_text(COMBINED.read_text(encoding="utf-8"), encoding="utf-8")
    original_doc = ROOT / "MERMAID_LOGICAL_DATA_MODEL_2026-10-26-detailed.md"
    if not original_doc.exists():
        original_doc.write_text(DOCUMENT.read_text(encoding="utf-8"), encoding="utf-8")
    model = json.loads((ROOT / "model.json").read_text(encoding="utf-8-sig"))
    tables = {t["name"]: t for t in model["tables"]}
    assert set(FIELDS) == set(tables), "Every entity must have a presentation definition."
    # The source metadata predates this approved nullable-column migration.
    for col in tables["store_member_permissions"]["columns"]:
        if col["name"] == "is_granted":
            col["nullable"] = True
    text = SOURCE.read_text(encoding="utf-8")
    source_edges = []
    for line in text.splitlines():
        match = re.fullmatch(r'    (\w+) ([|o}{]+)--([|o}{]+) (\w+) : "(\w+)"', line)
        if match:
            parent, left, right, child, column = match.groups()
            source_edges.append({"parent": parent, "child": child, "column": column,
                                 "line": line, "left": left, "right": right})
    kept, hidden = [], []
    for edge in source_edges:
        (kept if edge["column"] in KEEP.get(edge["child"], "").split() else hidden).append(edge)
    assert len(source_edges) == 161
    assert {(e["child"], e["column"]) for e in kept} == {
        (child, col) for child, cols in KEEP.items() for col in cols.split()
    }, "The keep policy must match existing relationships exactly."
    for edge in kept:
        table = tables[edge["child"]]
        fk, = (f for f in table["foreignKeys"]
               if f["columns"] == [edge["column"]] and f["target"] == edge["parent"])
        unique = fk["unique"] or any(u["columns"] == fk["columns"] and not u["filter"]
                                     for u in table["uniqueKeys"])
        assert edge["left"] == ("||" if fk["required"] else "|o")
        assert edge["right"] == ("o|" if unique else "o{")

    def definition(name, reference=False, overview=False):
        table = tables[name]
        columns = {c["name"]: c for c in table["columns"]}
        fields = ["id"] if reference else list(dict.fromkeys(
            ["id"] + KEEP.get(name, "").split() + FIELDS[name].split()))
        if overview:
            fields = ["id"] + FIELDS[name].split()[:2]
        lines = [f"    {name} {{"]
        for field in fields:
            col = columns[field]
            physical = col["type"]
            logical = ("Identifier" if physical == "uuid" else
                       "Boolean" if physical == "boolean" else
                       "Date" if physical == "date" else
                       "DateTime" if physical.startswith("timestamp") else
                       "Integer" if physical in ("bigint", "integer", "smallint") else
                       "Decimal" if physical.startswith("numeric") else
                       "JSON" if physical in ("json", "jsonb") else "String")
            fk = any(field in f["columns"] for f in table["foreignKeys"])
            unique = any(u["columns"] == [field] and not u["filter"] for u in table["uniqueKeys"])
            keys = [key for condition, key in ((col["primary"], "PK"), (fk, "FK"), (unique, "UK")) if condition]
            line = f"        {logical} {field}" + (" " + ",".join(keys) if keys else "")
            if col["nullable"]:
                line += ' "optional"'
            lines.append(line)
        return "\n".join(lines + ["    }"])

    def diagram(names, edges, owned=None, overview=False):
        lines = ["erDiagram", "    direction TB"]
        for name in sorted(names):
            lines.append(definition(name, reference=owned is not None and name not in owned,
                                    overview=overview))
        lines.extend(e["line"] for e in edges)
        return "\n".join(lines) + "\n"

    combined = diagram(tables, kept)
    COMBINED.write_text(combined, encoding="utf-8")
    (OUT / "all-74-entities.mmd").write_text(combined, encoding="utf-8")
    groups = []
    source_files = sorted(ROOT.glob("[0-9][0-9]-*.mmd"))
    for file, title in zip(source_files, GROUP_TITLES, strict=True):
        original = file.read_text(encoding="utf-8-sig")
        owned = {m.group(1) for m in re.finditer(r'^    (\w+) \{\n(.*?)^    \}', original, re.M | re.S)
                 if len(m.group(2).splitlines()) > 1}
        edges = [e for e in kept if e["child"] in owned]
        names = owned | {e["parent"] for e in edges}
        content = diagram(names, edges, owned)
        (OUT / file.name).write_text(content, encoding="utf-8")
        groups.append({"title": title, "file": file.name, "owned": owned,
                       "edges": edges, "content": content})
    membership = Counter(n for group in groups for n in group["owned"])
    assert set(membership) == set(tables) and set(membership.values()) == {1}
    assert sum(len(g["edges"]) for g in groups) == len(kept)

    overview_names = set("users farmer_profiles stores products store_products inventory_lots suppliers "
                         "goods_receipts goods_receipt_items orders order_items deliveries payments "
                         "payment_allocations debt_accounts debt_entries sales_returns sales_return_items "
                         "refunds diagnosis_cases agent_reviews recommendation_items diseases".split())
    overview_edges = [e for e in kept if e["parent"] in overview_names and e["child"] in overview_names]
    overview = diagram(overview_names, overview_edges, overview=True)
    (OUT / "00-overview.mmd").write_text(overview, encoding="utf-8")

    def reason(edge):
        col = edge["column"]
        if col == "store_id":
            return "Phạm vi Store dùng chung; lược đường nối lặp về Store."
        if "snapshot" in col or col == "source_address_id":
            return "Bối cảnh/snapshot tại thời điểm giao dịch; tra trong bản chi tiết."
        if "reversal" in col or "superseded" in col or col in ("new_credit_tier_id", "old_credit_tier_id"):
            return "Lịch sử, thay thế hoặc đảo giao dịch; tra trong bản chi tiết."
        if col in ("assigned_to_member_id", "attempted_by_member_id", "payer_farmer_profile_id"):
            return "Tác nhân hoặc bên tham gia phụ; lược khỏi góc nhìn cấu trúc chính."
        return "Liên kết đối soát, nguồn hoặc tham chiếu chéo; chỉ ẩn trong góc nhìn trình bày."

    review = ["# Đối chiếu quan hệ đã lược khỏi bản trình bày", "",
              f"Bản trước: {len(source_edges)} đường. Bản gọn: {len(kept)} đường. Lược {len(hidden)} đường hiển thị.", "",
              "Tất cả các FK dưới đây vẫn tồn tại trong mô hình nguồn. Bảng này không đề xuất xóa FK hoặc thuộc tính.", "",
              "| Thực thể con | Thuộc tính FK | Tham chiếu | Lý do ẩn |", "|---|---|---|---|"]
    review.extend(f"| `{e['child']}` | `{e['column']}` | `{e['parent']}` | {reason(e)} |" for e in hidden)
    (OUT / "RELATIONSHIP_REVIEW.md").write_text("\n".join(review) + "\n", encoding="utf-8")
    manifest = {"sourceEdges": len(source_edges), "displayedEdges": len(kept),
                "entityCount": len(tables), "kept": kept,
                "hidden": [dict(e, reason=reason(e)) for e in hidden]}
    (OUT / "relationship-selection.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    doc = f"""# AgriSage — Logical Data Model: bản trình bày gọn

Đã chỉnh theo góp ý giảm quan hệ gây rối. Phạm vi chi tiết vẫn là **74 thực thể**; số đường quan hệ hiển thị giảm từ **{len(source_edges)} xuống {len(kept)}**, lược **{len(hidden)} đường ({len(hidden) / len(source_edges):.0%})**. Không thay đổi database, entity, FK hay migration.

## Cách dùng trong báo cáo

- Dùng **sơ đồ đọc nhanh** để giới thiệu phạm vi: {len(overview_names)} thực thể chọn lọc, {len(overview_edges)} quan hệ. Đây là một góc nhìn của logical model, không phải toàn bộ schema.
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
- `||` = đúng một; `o|` = không hoặc một; `o{{` = không hoặc nhiều. Ví dụ `A ||--o{{ B`: mỗi B bắt buộc thuộc một A; A có thể chưa có B.
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
{overview.rstrip()}
```

## Các sơ đồ nghiệp vụ

| Nhóm | Thực thể sở hữu | Quan hệ được vẽ | Mã Mermaid |
|---|---:|---:|---|
"""
    for group in groups:
        doc += f"| {group['title']} | {len(group['owned'])} | {len(group['edges'])} | [{group['file']}](simplified/{group['file']}) |\n"
    for i, group in enumerate(groups, 1):
        doc += f"\n## {i}. {group['title']}\n\n```mermaid\n{group['content'].rstrip()}\n```\n"
    doc += """
## Đối chiếu và nguồn

- [Danh sách từng quan hệ được lược và lý do](simplified/RELATIONSHIP_REVIEW.md).
- [Bản Mermaid chi tiết trước khi giản lược](agrisage-logical-model-2026-10-26-detailed.mmd).
- [Tài liệu chi tiết trước khi giản lược](MERMAID_LOGICAL_DATA_MODEL_2026-10-26-detailed.md).
- [Thiết kế database](../../reference/DATABASE_DESIGN.md), [mở rộng xác thực](../../reference/AUTH_OPERATIONS_SCHEMA.md), metadata local `model.json` và migration `PermissionOverrideInheritance`.

Đã kiểm tra: giữ đủ 74 thực thể trong 11 nhóm, không nhân đôi thực thể sở hữu, mọi quan hệ được vẽ đều có FK thật trong metadata, bội số và tính optional giữ theo nguồn, các quan hệ đã lược có danh sách đối chiếu. Không truy cập database live.
"""
    DOCUMENT.write_text(doc, encoding="utf-8")
    print(f"Entities: {len(tables)}; relationships: {len(source_edges)} -> {len(kept)}; hidden: {len(hidden)}")
    print(f"Overview: {len(overview_names)} entities, {len(overview_edges)} relationships; groups: {len(groups)}")
    print(f"Document: {DOCUMENT}")


if __name__ == "__main__":
    run()
