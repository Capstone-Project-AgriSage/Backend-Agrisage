// Run with @viz-js/viz and sharp available through Node's module resolution.
const fs = require("node:fs");
const path = require("node:path");
const { instance } = require("@viz-js/viz");
const sharp = require("sharp");

(async () => {
  const viz = await instance();
  const dot = fs.readFileSync(path.join(__dirname, "agrisage-single.dot"), "utf8");
  const lines = dot.split("\n");
  const groups = [];
  const definitions = new Map();
  const edges = lines.filter((line) => /^"\w+" ->/.test(line));
  let currentGroup;
  for (const line of lines) {
    if (line.startsWith("subgraph ")) {
      currentGroup = { declaration: line, names: [] };
      groups.push(currentGroup);
    }
    const match = line.match(/^"(\w+)" \[id=/);
    if (match) {
      currentGroup.names.push(match[1]);
      definitions.set(match[1], line);
    }
  }

  // Lay out relationships inside each domain first. Then put every domain
  // on one shared grid and route the original cross-domain edges around nodes.
  const local = groups.map((group, groupIndex) => {
    const names = new Set(group.names);
    const internal = edges.filter((edge) => {
      const [, parent, child] = edge.match(/^"(\w+)" -> "(\w+)"/);
      return names.has(parent) && names.has(child);
    });
    const input = [
      "digraph {",
      `graph[rankdir=${[3, 6, 9].includes(groupIndex) ? "LR" : "TB"},nodesep=0.35,ranksep=0.65,pad=0.2];`,
      'node[shape=plain,fontname="Arial",fontsize=11];',
      'edge[dir=both,arrowsize=0.65];',
      ...group.names.map((name) => definitions.get(name)),
      ...internal, "}",
    ].join("\n");
    const result = viz.render(input, { format: "json", engine: "dot" });
    if (result.status !== "success") throw new Error(JSON.stringify(result.errors));
    const graph = JSON.parse(result.output);
    const [, , width, height] = graph.bb.split(",").map(Number);
    return { graph, width, height };
  });

  // A cell can contain two vertically stacked domain clusters; all 11 are
  // visible at once. These are presentation coordinates, never extra entities.
  const grid = [ [[0], [1, 10], [9]], [[2], [5, 8], [7]], [[3], [4], [6]] ];
  const gap = 95;
  const stackGap = 55;
  const topPadding = 20;
  const cellSize = (cell) => ({
    width: Math.max(...cell.map((i) => local[i].width)),
    height: cell.reduce((total, i) => total + local[i].height + topPadding, 0)
      + stackGap * (cell.length - 1),
  });
  const columnWidths = [0, 1, 2].map((x) => Math.max(...grid.map((row) => cellSize(row[x]).width)));
  const rowHeights = grid.map((row) => Math.max(...row.map((cell) => cellSize(cell).height)));
  const height = rowHeights.reduce((a, b) => a + b, 0) + gap * 2;
  const positions = new Map();
  const bounds = new Map();
  grid.forEach((row, y) => row.forEach((cell, x) => {
    const left = columnWidths.slice(0, x).reduce((a, b) => a + b, 0) + gap * x;
    let top = height - rowHeights.slice(0, y).reduce((a, b) => a + b, 0) - gap * y;
    for (const i of cell) {
      const layout = local[i];
      const ox = left + (columnWidths[x] - layout.width) / 2;
      const oy = top - topPadding - layout.height;
      for (const node of layout.graph.objects || []) {
        if (!definitions.has(node.name)) continue;
        const [px, py] = node.pos.split(",").map(Number);
        positions.set(node.name, [px + ox, py + oy]);
      }
      bounds.set(i, [ox - 20, oy - 20, ox + layout.width + 20, top]);
      top = oy - stackGap;
    }
  }));

  const fixed = [
    "digraph LDM {",
    'graph[layout=neato,inputscale=72,notranslate=true,overlap=true,splines=ortho,pad=0.4,' +
      'outputorder=edgesfirst,bgcolor="white",fontname="Arial",fontsize=22,labelloc=t,' +
      'label="AgriSage - Logical Data Model | 74 entities | 105 relationships"];',
    'node[shape=plain,fontname="Arial",fontsize=11];',
    'edge[dir=both,arrowsize=0.65,fontname="Arial",fontsize=8];',
  ];
  groups.forEach((group, i) => {
    fixed.push(group.declaration);
    for (const name of group.names) {
      const [x, y] = positions.get(name);
      fixed.push(definitions.get(name).replace("[id=", `[pos="${x},${y}!",pin=true,id=`));
    }
    // Invisible points retain layout spacing without drawing group containers.
    const [left, bottom, right, top] = bounds.get(i);
    fixed.push(`"caption_${i}" [shape=point,width=0,height=0,style=invis,pos="${(left + right) / 2},${top}!",pin=true];`);
    fixed.push(`"padding_${i}" [shape=point,width=0,height=0,style=invis,pos="${left},${bottom}!",pin=true];`);
    fixed.push(`"padding_right_${i}" [shape=point,width=0,height=0,style=invis,pos="${right},${bottom}!",pin=true];`);
    fixed.push("}");
  });
  fixed.push(...edges, "}");
  const fixedDot = fixed.join("\n");
  fs.writeFileSync(path.join(__dirname, "agrisage-single-layout.dot"), fixedDot, "utf8");
  const result = viz.render(fixedDot, { format: "svg", engine: "neato" });
  if (result.status !== "success") {
    throw new Error(JSON.stringify(result.errors));
  }
  const svg = result.output;
  fs.writeFileSync(path.join(__dirname, "agrisage-single.svg"), svg, "utf8");
  fs.writeFileSync(path.join(__dirname, "agrisage-fixed-layout.svg"), svg, "utf8");
  const layoutResult = viz.render(fixedDot, { format: "json", engine: "neato" });
  if (layoutResult.status !== "success") throw new Error(JSON.stringify(layoutResult.errors));
  fs.writeFileSync(path.join(__dirname, "fixed-layout.json"), layoutResult.output, "utf8");
  await sharp(Buffer.from(svg), { limitInputPixels: false })
    .resize({ width: 2600, height: 2200, fit: "inside" })
    .png()
    .toFile(path.join(__dirname, "agrisage-single-preview.png"));
  await sharp(Buffer.from(svg), { limitInputPixels: false })
    .resize({ width: 5000 })
    .png()
    .toFile(path.join(__dirname, "agrisage-single.png"));
  fs.copyFileSync(path.join(__dirname, "agrisage-single.png"), path.join(__dirname, "agrisage-fixed-layout.png"));
  console.log(JSON.stringify({
    rendered: true,
    warnings: result.errors,
    viewBox: svg.match(/viewBox="([^"]+)"/)?.[1],
    entities: (svg.match(/id="entity(?:&#45;|-)/g) || []).length,
    relationships: (svg.match(/class="edge"/g) || []).length,
  }));
})().catch((error) => { console.error(error); process.exitCode = 1; });
