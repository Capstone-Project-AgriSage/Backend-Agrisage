"""Export real editable entities and routed edges with fixed coordinates."""

import html
import json
import math
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def run():
    layout = json.loads((ROOT / "fixed-layout.json").read_text(encoding="utf-8"))
    source = (ROOT / "agrisage-single.mmd").read_text(encoding="utf-8")
    selected = json.loads((ROOT.parent / "simplified/relationship-selection.json").read_text(encoding="utf-8"))["kept"]
    attributes = {}
    for match in re.finditer(r'^    (\w+) \{\n(.*?)^    \}', source, re.M | re.S):
        name, body = match.groups()
        attributes[name] = [line.strip().split() for line in body.splitlines()]
    assert len(attributes) == 74
    objects = {obj["_gvid"]: obj for obj in layout["objects"]}
    nodes = {obj["name"]: obj for obj in objects.values() if obj.get("name") in attributes}
    relations = {(edge["parent"], edge["child"]): edge for edge in selected}
    assert len(nodes) == 74 and len(relations) == 105
    min_x, min_y, max_x, max_y = map(float, layout["bb"].split(","))
    scale, margin = 1.35, 45

    def point(x, y):
        return (round((x - min_x) * scale + margin, 2),
                round((max_y - y) * scale + margin, 2))

    def box(node):
        x, y = map(float, node["pos"].split(","))
        width, height = float(node["width"]) * 72, float(node["height"]) * 72
        left, top = point(x - width / 2, y + height / 2)
        return left, top, round(width * scale, 2), round(height * scale, 2)

    page_width = math.ceil((max_x - min_x) * scale + margin * 2)
    page_height = math.ceil((max_y - min_y) * scale + margin * 2)
    file = ET.Element("mxfile", host="app.diagrams.net", agent="AgriSage", type="device")
    diagram = ET.SubElement(file, "diagram", id="agrisage-fixed", name="AgriSage Logical Data Model")
    model = ET.SubElement(diagram, "mxGraphModel", dx="1400", dy="900", grid="0", gridSize="10",
                          guides="1", tooltips="1", connect="1", arrows="1", fold="1", page="1",
                          pageScale="1", pageWidth=str(page_width), pageHeight=str(page_height),
                          background="#ffffff", math="0", shadow="0")
    root = ET.SubElement(model, "root")
    ET.SubElement(root, "mxCell", id="0")
    ET.SubElement(root, "mxCell", id="1", parent="0")
    boxes = {}
    for name, node in nodes.items():
        x, y, width, height = box(node)
        boxes[name] = (x, y, width, height)
        rows = [f'<tr><td colspan="2" style="padding:5px 6px;color:#2d78b7;'
                f'font-weight:bold;border-bottom:1px solid #dce4ec">{html.escape(name)}</td></tr>']
        for tokens in attributes[name]:
            logical, field = tokens[:2]
            key = "PK " if "PK" in tokens else ""
            optional = " ?" if '"optional"' in tokens else ""
            color = "color:#2d78b7;font-weight:bold;" if key else "color:#203042;"
            rows.append(f'<tr><td style="padding:4px 6px;{color}">{key}{html.escape(field)}{optional}</td>'
                        f'<td style="padding:4px 6px;color:#667485;font-size:12px">{logical}</td></tr>')
        label = ('<table style="width:100%;height:100%;border-collapse:collapse;'
                 'font-family:Arial;font-size:14px;background:#ffffff">' + "".join(rows) + '</table>')
        cell = ET.SubElement(root, "mxCell", id="entity-" + name, value=label, vertex="1", parent="1",
                             style="shape=rectangle;rounded=0;whiteSpace=wrap;html=1;spacing=0;overflow=fill;"
                                   "fillColor=#ffffff;strokeColor=#2d78b7;strokeWidth=1;shadow=0;"
                                   "fontFamily=Arial;fontSize=14;fontColor=#203042;")
        ET.SubElement(cell, "mxGeometry", x=str(x), y=str(y), width=str(width), height=str(height), **{"as": "geometry"})

    def anchor(node, raw):
        cx, cy = map(float, node["pos"].split(","))
        width, height = float(node["width"]) * 72, float(node["height"]) * 72
        return (min(1, max(0, (raw[0] - cx + width / 2) / width)),
                min(1, max(0, (cy + height / 2 - raw[1]) / height)))

    for index, edge in enumerate(layout["edges"]):
        parent, child = objects[edge["tail"]], objects[edge["head"]]
        rel = relations[(parent["name"], child["name"])]
        start_arrow = "ERmandOne" if rel["left"] == "||" else "ERzeroToOne"
        end_arrow = "ERzeroToMany" if rel["right"] == "o{" else "ERzeroToOne"
        segments = next(op["points"] for op in edge["_draw_"] if op["op"] == "b")
        bends = []
        for raw in segments:
            current = point(*raw)
            if not bends or current != bends[-1]:
                bends.append(current)
        source_match = re.search(r's,([-\d.]+),([-\d.]+)', edge["pos"])
        target_match = re.search(r'e,([-\d.]+),([-\d.]+)', edge["pos"])
        assert source_match and target_match
        exit_x, exit_y = anchor(parent, tuple(map(float, source_match.groups())))
        entry_x, entry_y = anchor(child, tuple(map(float, target_match.groups())))
        style = (f"edgeStyle=none;noEdgeStyle=1;rounded=0;curved=0;html=1;strokeColor=#94a3b8;strokeWidth=1;"
                 f"startArrow={start_arrow};endArrow={end_arrow};startFill=0;endFill=0;startSize=10;endSize=10;"
                 f"exitX={exit_x:.6f};exitY={exit_y:.6f};exitDx=0;exitDy=0;exitPerimeter=0;"
                 f"entryX={entry_x:.6f};entryY={entry_y:.6f};entryDx=0;entryDy=0;entryPerimeter=0;")
        cell = ET.SubElement(root, "mxCell", id=f"relation-{index:03d}", value="", edge="1", parent="1",
                             source="entity-" + parent["name"], target="entity-" + child["name"], style=style)
        geometry = ET.SubElement(cell, "mxGeometry", relative="1", **{"as": "geometry"})
        points = ET.SubElement(geometry, "Array", **{"as": "points"})
        for x, y in bends:
            ET.SubElement(points, "mxPoint", x=str(x), y=str(y))
    assert len(layout["edges"]) == 105
    for left_index, (name, a) in enumerate(boxes.items()):
        for other, b in list(boxes.items())[left_index + 1:]:
            overlap_x = min(a[0] + a[2], b[0] + b[2]) - max(a[0], b[0])
            overlap_y = min(a[1] + a[3], b[1] + b[3]) - max(a[1], b[1])
            assert overlap_x < 0.1 or overlap_y < 0.1, (name, other)
    output = ROOT / "agrisage-fixed-layout.drawio"
    ET.indent(file, space="  ")
    ET.ElementTree(file).write(output, encoding="utf-8", xml_declaration=True)
    # Validate the exact bytes that will be opened by Draw.io. This catches
    # truncated mxCell/mxGeometry tags and illegal unescaped characters before
    # the artifact is handed off.
    parsed = ET.parse(output)
    parsed_root = parsed.getroot()
    parsed_cells = parsed_root.findall("./diagram/mxGraphModel/root/mxCell")
    parsed_vertices = [cell for cell in parsed_cells if cell.get("vertex") == "1"]
    parsed_edges = [cell for cell in parsed_cells if cell.get("edge") == "1"]
    if len(parsed_vertices) != 74 or len(parsed_edges) != 105:
        raise RuntimeError("Generated Draw.io XML failed entity/relationship count validation")
    raw = output.read_text(encoding="utf-8")
    if re.search(r"&(?!amp;|lt;|gt;|quot;|apos;)", raw):
        raise RuntimeError("Generated Draw.io XML contains an unescaped ampersand")
    validated_output = ROOT / "agrisage-fixed-layout-validated.drawio"
    validated_output.write_bytes(output.read_bytes())
    manifest_path = ROOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["fixedLayout"] = {"file": output.name, "pageWidth": page_width,
                               "pageHeight": page_height, "pageCount": 1,
                               "editableEntities": len(nodes), "routedRelationships": len(layout["edges"])}
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Draw.io: 74 editable tables, 105 routed relationships, no overlapping tables, one {page_width} x {page_height} page.")
    print(output)
    print(validated_output)


if __name__ == "__main__":
    run()
