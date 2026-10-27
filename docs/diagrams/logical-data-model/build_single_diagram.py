"""One logical ER diagram, with uniform tables and no visible group containers."""

import html
import json
from pathlib import Path

from build_simplified_model import FIELDS

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "single-diagram"
TABLE_FILL = "#ffffff"
TABLE_BORDER = "#2d78b7"
SELECT = {
    "orders": "order_number total_amount status",
    "debt_entries": "original_amount outstanding_amount due_date",
    "goods_receipts": "receipt_number total_amount status",
    "payments": "payment_method amount status",
    "sales_returns": "return_number total_return_amount status",
}


def run():
    OUT.mkdir(exist_ok=True)
    metadata = json.loads((ROOT / "model.json").read_text(encoding="utf-8-sig"))
    viewer = json.loads((ROOT / "viewer-data.json").read_text(encoding="utf-8-sig"))
    selection = json.loads((ROOT / "simplified/relationship-selection.json").read_text(encoding="utf-8"))
    tables = {t["name"]: t for t in metadata["tables"]}
    groups = viewer["groups"]
    membership = {t["name"]: t["group"] for t in viewer["tables"]}
    edges = selection["kept"]
    assert len(tables) == 74 and len(edges) == 105
    assert set(tables) == set(membership)

    def attributes(name):
        table = tables[name]
        columns = {c["name"]: c for c in table["columns"]}
        fields = ["id"] + SELECT.get(name, " ".join(FIELDS[name].split()[:3])).split()
        result = []
        for field in fields:
            col = dict(columns[field])
            physical = col["type"]
            logical = ("Identifier" if physical == "uuid" else
                       "Boolean" if physical == "boolean" else
                       "Date" if physical == "date" else
                       "DateTime" if physical.startswith("timestamp") else
                       "Integer" if physical in ("bigint", "integer", "smallint") else
                       "Decimal" if physical.startswith("numeric") else "String")
            if name == "store_member_permissions" and field == "is_granted":
                col["nullable"] = True
            result.append(dict(col, logical=logical))
        return result

    header = """---
title: AgriSage - Logical Data Model - 74 entities
config:
  layout: elk
  theme: base
  look: classic
  elk:
    mergeEdges: false
  themeVariables:
    fontFamily: Arial
    fontSize: 12px
    background: '#ffffff'
    primaryColor: '#ffffff'
    primaryBorderColor: '#2d78b7'
    primaryTextColor: '#2d78b7'
    textColor: '#203042'
    mainBkg: '#ffffff'
    rowOdd: '#ffffff'
    rowEven: '#ffffff'
    lineColor: '#94a3b8'
  er:
    useMaxWidth: false
---
erDiagram
    direction TB
    %% One canvas, uniform tables, no visible group backgrounds or captions.
    %% Logical view: reference attributes are represented by relationships.
    %% Crow-foot markers preserve actual optionality and cardinality.
"""
    lines = [header.rstrip()]
    seen = set()
    for i, group in enumerate(groups):
        names = [name for name in tables if membership[name] == group["id"]]
        assert len(names) == group["count"]
        lines.append(f"    %% {group['title']}")
        for name in sorted(names):
            assert name not in seen
            seen.add(name)
            lines.append(f"    {name} {{")
            for col in attributes(name):
                key = " PK" if col["primary"] else ""
                optional = ' "optional"' if col["nullable"] else ""
                lines.append(f"        {col['logical']} {col['name']}{key}{optional}")
            lines.append("    }")
        for edge in edges:
            if membership[edge["parent"]] == group["id"] == membership[edge["child"]]:
                lines.append(f"    {edge['parent']} {edge['left']}--{edge['right']} {edge['child']} : \"\"")
    lines.append("    %% Cross-domain relationships, each drawn once between real entities.")
    for edge in edges:
        if membership[edge["parent"]] != membership[edge["child"]]:
            lines.append(f"    {edge['parent']} {edge['left']}--{edge['right']} {edge['child']} : \"\"")
    lines.append(f"    classDef default fill:{TABLE_FILL},stroke:{TABLE_BORDER}")
    assert seen == set(tables)
    mermaid = "\n".join(lines) + "\n"
    (OUT / "agrisage-single.mmd").write_text(mermaid, encoding="utf-8")
    (ROOT / "agrisage-logical-model-2026-10-26.mmd").write_text(mermaid, encoding="utf-8")

    # Paste-friendly logical view: relationships carry the FK meaning, so the
    # compact version keeps only the entity identifier inside each table.
    compact_lines = ["erDiagram", "    direction TB"]
    for name in sorted(tables):
        compact_lines.extend([f"    {name} {{", "        Identifier id PK", "    }"])
    compact_lines.extend(
        f"    {edge['parent']} {edge['left']}--{edge['right']} {edge['child']} : \"\""
        for edge in edges
    )
    compact_lines.append("    classDef default fill:#ffffff,stroke:#2d78b7,color:#203042")
    compact = "\n".join(compact_lines) + "\n"
    (OUT / "agrisage-single-compact.mmd").write_text(compact, encoding="utf-8")

    # Standalone SVG preview is rendered offline from the same entities/edges.
    # The Mermaid source above remains the editable deliverable.
    dot = ["digraph LDM {",
           'graph [rankdir=LR, bgcolor="white", pad="0.5", nodesep="0.45", ranksep="0.9", '
           'splines=polyline, overlap=false, compound=true, newrank=true, '
           'outputorder=edgesfirst, fontname="Arial", fontsize=24, labelloc=t, '
           'label="AgriSage - Logical Data Model\\n74 entities | 105 selected relationships | One diagram"];',
           'node [shape=plain, fontname="Arial", fontsize=11];',
           'edge [fontname="Arial", fontsize=8, dir=both, arrowsize=0.65, color="#9aa8b5", '
           'fontcolor="#475569"];']
    for i, group in enumerate(groups):
        # Invisible clusters retain the useful layout, but draw no group box,
        # background or caption. They are not part of the Mermaid output.
        dot.append(f'subgraph "cluster_{group["id"]}" {{ label=""; '
                   'color="transparent"; bgcolor="transparent"; style="invis"; margin=20;')
        for name in sorted(n for n in tables if membership[n] == group["id"]):
            rows = [f'<TR><TD COLSPAN="2" ALIGN="LEFT" BGCOLOR="{TABLE_FILL}">'
                    f'<FONT COLOR="{TABLE_BORDER}"><B>{name}</B></FONT></TD></TR>']
            for col in attributes(name):
                field = html.escape(col["name"]) + (" ?" if col["nullable"] else "")
                key = "PK " if col["primary"] else ""
                label = f'<FONT COLOR="{TABLE_BORDER}"><B>{key}{field}</B></FONT>' if key else field
                rows.append(f'<TR><TD ALIGN="LEFT">{label}</TD>'
                            f'<TD ALIGN="LEFT"><FONT COLOR="#667485">{col["logical"]}</FONT></TD></TR>')
            dot.append(f'"{name}" [id="entity-{name}", label=<<TABLE BORDER="1" COLOR="{TABLE_BORDER}" '
                       'BGCOLOR="white" CELLBORDER="0" CELLSPACING="0" CELLPADDING="5">'
                       + "".join(rows) + '</TABLE>>];')
        dot.append("}")
    for edge in edges:
        inside = membership[edge["parent"]] == membership[edge["child"]]
        tail = "teetee" if edge["left"] == "||" else "teeodot"
        head = "crowodot" if edge["right"] == "o{" else "teeodot"
        color = "#94a3b8"
        tooltip = html.escape(f"{edge['parent']} -> {edge['child']} ({edge['column']})")
        dot.append(f'"{edge["parent"]}" -> "{edge["child"]}" [arrowtail="{tail}", '
                   f'arrowhead="{head}", color="{color}", penwidth="{1.15 if inside else 0.8}", '
                   f'weight="{5 if inside else 1}", tooltip="{tooltip}"];')
    dot.append("}")
    (OUT / "agrisage-single.dot").write_text("\n".join(dot) + "\n", encoding="utf-8")
    manifest = {"entityCount": len(tables), "relationshipCount": len(edges),
                "layoutGroups": len(groups), "visibleGroupContainers": 0,
                "tableFill": TABLE_FILL, "tableBorder": TABLE_BORDER,
                "internalRelationships": sum(
                    membership[e["parent"]] == membership[e["child"]] for e in edges),
                "source": "../simplified/relationship-selection.json"}
    (OUT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    previous = ROOT / "MERMAID_LOGICAL_DATA_MODEL_2026-10-26-multiple-views.md"
    document = ROOT / "MERMAID_LOGICAL_DATA_MODEL_2026-10-26.md"
    if not previous.exists():
        previous.write_text(document.read_text(encoding="utf-8"), encoding="utf-8")
    doc = """# AgriSage — Một sơ đồ Logical Data Model

**74 thực thể, 105 quan hệ được chọn, tất cả trên một sơ đồ.** Đã bỏ nền, khung và tiêu đề nhóm nghiệp vụ. Tất cả bảng dùng cùng kiểu: nền trắng, viền và tên bảng màu xanh.

- [File Draw.io giữ bố cục cố định, chỉnh sửa trực tiếp](single-diagram/agrisage-fixed-layout.drawio).
- [Bản Draw.io đã kiểm tra XML](single-diagram/agrisage-fixed-layout-validated.drawio).
- [Xem sơ đồ SVG để phóng to](single-diagram/agrisage-fixed-layout.svg).
- [Ảnh PNG độ phân giải cao](single-diagram/agrisage-fixed-layout.png).
- [Mã Mermaid của sơ đồ duy nhất](single-diagram/agrisage-single.mmd).
- [Mã Mermaid rút gọn để dán nhanh](single-diagram/agrisage-single-compact.mmd).

Các bảng vẫn được sắp gần nhau theo nghiệp vụ để hạn chế đường nối dài, nhưng không có khung nền bao quanh nhóm. Chỉ hiện định danh và thuộc tính tiêu biểu, bỏ nhãn tên cột FK dài trên đường nối. Mọi thực thể chỉ xuất hiện một lần.

## Cách đọc

- `PK` là định danh; `optional` là thuộc tính có thể NULL. Thuộc tính tham chiếu FK được thể hiện bằng đường quan hệ, nên không lặp lại toàn bộ chúng trong khung thực thể.
- Bội số crow's foot: `||` = đúng một; `o|` = không hoặc một; `o{` = không hoặc nhiều. Bội số giữ theo mô hình nguồn; không suy 1–1 từ tên Profile/Account.
- Quan hệ lặp về Store, audit, snapshot và truy vết phụ vẫn có trong thiết kế; chỉ lược khỏi góc nhìn trình bày này. [Danh sách 56 quan hệ đã lược](simplified/RELATIONSHIP_REVIEW.md).
- PaymentAllocation vẫn thể hiện cả Order và DebtEntry; chọn đúng một đích theo allocation type. SalesReturnItem vẫn giữ hai nguồn bán/giao với điều kiện đúng một nguồn. StockMovementItem và DebtTransaction tiếp tục thể hiện biến động tồn kho và công nợ. AgentReview giữ vai trò Human Review trước khuyến nghị.
- Để giữ bố cục gọn khi chỉnh sửa trong Draw.io/diagrams.net, mở trực tiếp file `.drawio` phía trên. File lưu tọa độ các bảng, đường nối và kích thước một trang riêng; không cần tự dàn lại sau khi mở.
- Mermaid mô tả cùng thực thể và quan hệ, nhưng trình nhập tự tính vị trí nên không bảo đảm giống bố cục đã xuất. SVG/PNG và file `.drawio` dùng chung bộ tọa độ cố định.

## Mã Mermaid — một khối duy nhất

Mã dùng `erDiagram` và layout ELK, không có subgraph hiển thị nền nhóm. Một kiểu màu chung áp dụng cho mọi bảng.

Bản rút gọn chỉ giữ `id` trong mỗi bảng; FK đã được biểu diễn bằng quan hệ nên không lặp lại trong ô bảng. Dùng bản này khi trình Mermaid/Draw.io giới hạn độ dài nội dung.

```mermaid
""" + mermaid.rstrip() + "\n```\n"
    document.write_text(doc, encoding="utf-8")
    print(f"One ER diagram: {len(tables)} entities; {len(edges)} relationships; {len(groups)} clusters.")
    print(f"Mermaid: {OUT / 'agrisage-single.mmd'}")


if __name__ == "__main__":
    run()
