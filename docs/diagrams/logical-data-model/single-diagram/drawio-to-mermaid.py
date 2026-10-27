"""Convert the actual Draw.io labels and relationship markers to Mermaid ER."""

import re
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parent


class TableLabel(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.rows = []
        self.row = None
        self.cell = None

    def handle_starttag(self, tag, attrs):
        if tag == "tr":
            self.row = []
        elif tag in ("td", "th"):
            self.cell = []

    def handle_data(self, data):
        if self.cell is not None:
            self.cell.append(data)

    def handle_endtag(self, tag):
        if tag in ("td", "th") and self.cell is not None:
            self.row.append("".join(self.cell).strip())
            self.cell = None
        elif tag == "tr" and self.row is not None:
            self.rows.append(self.row)
            self.row = None


def run():
    source = ROOT / "agrisage-fixed-layout.drawio"
    root = ET.parse(source).getroot()
    cells = root.findall("./diagram/mxGraphModel/root/mxCell")
    vertices = [cell for cell in cells if cell.get("vertex") == "1"]
    edges = [cell for cell in cells if cell.get("edge") == "1"]
    names = {}
    blocks = []
    attribute_count = 0
    for cell in vertices:
        parser = TableLabel()
        parser.feed(cell.get("value", ""))
        parser.close()
        name, = parser.rows[0]
        assert re.fullmatch(r"[A-Za-z_]\w*", name), name
        assert name not in names.values(), name
        names[cell.get("id")] = name
        block = [f"    {name} {{"]
        for row in parser.rows[1:]:
            field, logical_type = row
            optional = field.endswith(" ?")
            if optional:
                field = field[:-2]
            key_match = re.match(r"^(PK|FK|UK|UQ) (\w+)$", field)
            key = ""
            if key_match:
                key, field = key_match.groups()
                key = "UK" if key == "UQ" else key
            assert re.fullmatch(r"[A-Za-z_]\w*", field), field
            assert re.fullmatch(r"[A-Za-z][\w(),\[\]-]*", logical_type), logical_type
            block.append(f"        {logical_type} {field}" + (f" {key}" if key else "")
                         + (' "optional"' if optional else ""))
            attribute_count += 1
        blocks.append("\n".join(block + ["    }"]))

    left_markers = {"ERmandOne": "||", "ERzeroToOne": "|o", "ERone": "||",
                    "ERzeroToMany": "}o", "ERoneToMany": "}|", "ERmany": "}o"}
    right_markers = {"ERmandOne": "||", "ERzeroToOne": "o|", "ERone": "||",
                     "ERzeroToMany": "o{", "ERoneToMany": "|{", "ERmany": "o{"}
    relations = []
    for cell in edges:
        style = dict(item.split("=", 1) for item in cell.get("style", "").split(";") if "=" in item)
        parent, child = names[cell.get("source")], names[cell.get("target")]
        left = left_markers[style["startArrow"]]
        right = right_markers[style["endArrow"]]
        label = cell.get("value", "").replace('"', "'")
        relations.append(f'    {parent} {left}--{right} {child} : "{label}"')

    header = """---
config:
  layout: elk
  theme: base
  look: classic
  themeVariables:
    primaryColor: '#ffffff'
    primaryBorderColor: '#2d78b7'
    primaryTextColor: '#2d78b7'
    textColor: '#203042'
    mainBkg: '#ffffff'
    rowOdd: '#ffffff'
    rowEven: '#ffffff'
    lineColor: '#94a3b8'
---
erDiagram
    direction TB
"""
    output = ROOT / "agrisage-from-drawio.mmd"
    content = (header + "\n".join(blocks) + "\n\n" + "\n".join(relations)
               + "\n\n    classDef default fill:#ffffff,stroke:#2d78b7,color:#203042\n")
    output.write_text(content, encoding="utf-8")
    assert len(re.findall(r"^    \w+ \{$", content, re.M)) == len(vertices)
    assert len(re.findall(r'^    \w+ [|o}{]+--[|o}{]+ \w+ : ', content, re.M)) == len(edges)
    print(f"Converted directly from {source.name}: {len(vertices)} tables, "
          f"{attribute_count} displayed attributes, {len(edges)} relationships.")
    print(output)


if __name__ == "__main__":
    run()
