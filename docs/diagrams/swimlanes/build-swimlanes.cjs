const fs = require('node:fs');
const path = require('node:path');

const root = __dirname;
const sourcePath = path.join(root, 'AGRISAGE_SWIMLANES.md');
const source = fs.readFileSync(sourcePath, 'utf8');
const slugs = [
  'ai-diagnosis', 'counter-sale', 'online-checkout', 'payos-payment',
  'order-confirmation', 'pickup', 'delivery', 'debt-repayment',
  'goods-receiving', 'stocktake', 'inventory-adjustment', 'order-cancellation',
  'sales-return', 'refund', 'debt-dispute', 'inventory-alerts',
];
const blocks = [...source.matchAll(/```mermaid\r?\n([\s\S]*?)```/g)];
if (blocks.length !== slugs.length) throw new Error(`Expected 16 diagrams, got ${blocks.length}`);

function checkStructure(code, name) {
  const nodes = new Set();
  const lanes = new Set();
  const edges = [];
  let lane;
  for (const line of code.split(/\r?\n/)) {
    const subgraph = line.match(/^\s*subgraph\s+(\w+)\[/);
    if (subgraph) {
      if (lane) throw new Error(`${name}: nested/unclosed lane`);
      if (lanes.has(subgraph[1])) throw new Error(`${name}: duplicate lane`);
      lane = subgraph[1];
      lanes.add(lane);
    } else if (line.trim() === 'end') {
      if (!lane) throw new Error(`${name}: unmatched end`);
      lane = undefined;
    } else {
      const node = line.match(/^\s+(\w+)(?:\(|\{|\[)/);
      if (node) {
        if (!lane) throw new Error(`${name}: node outside lane`);
        if (nodes.has(node[1])) throw new Error(`${name}: duplicate node ${node[1]}`);
        nodes.add(node[1]);
      }
    }
    if (line.includes('-->')) {
      const chain = line.replace(/\|[^|]*\|/g, '').trim().split(/\s*-->\s*/);
      for (let i = 1; i < chain.length; i++) edges.push([chain[i - 1], chain[i]]);
    }
  }
  if (lane) throw new Error(`${name}: unclosed lane`);
  for (const [a, b] of edges) {
    if (!nodes.has(a) || !nodes.has(b)) throw new Error(`${name}: undefined edge ${a} -> ${b}`);
  }
  function reachable(start, reverse) {
    const seen = new Set([start]);
    const pending = [start];
    while (pending.length) {
      const current = pending.pop();
      for (const edge of edges) {
        const [a, b] = reverse ? [edge[1], edge[0]] : edge;
        if (a === current && !seen.has(b)) { seen.add(b); pending.push(b); }
      }
    }
    return seen;
  }
  const fromStart = reachable('S', false);
  const toEnd = reachable('E', true);
  for (const node of nodes) {
    if (!fromStart.has(node) || !toEnd.has(node)) throw new Error(`${name}: disconnected/nonterminating node ${node}`);
  }
  return { nodes: nodes.size, lanes: lanes.size, edges: edges.length };
}

fs.mkdirSync(path.join(root, 'native'), { recursive: true });
fs.mkdirSync(path.join(root, 'compatible'), { recursive: true });
let compatible = source;
const summary = [];
blocks.forEach((match, index) => {
  const name = `wf-${String(index + 1).padStart(2, '0')}-${slugs[index]}`;
  const native = match[1].trimEnd() + '\n';
  summary.push({ name, ...checkStructure(native, name) });
  const fallback = native.replace('swimlane-beta TB', 'flowchart LR')
    .replace(/^(\s*subgraph[^\n]+)\n/gm, '$1\n    direction TB\n');
  fs.writeFileSync(path.join(root, 'native', name + '.mmd'), native);
  fs.writeFileSync(path.join(root, 'compatible', name + '.mmd'), fallback);
  compatible = compatible.replace(match[0], '```mermaid\n' + fallback + '```');
});
compatible = compatible.replace('# AgriSage — Mermaid swimlane diagrams', '# AgriSage — Mermaid flowchart fallback');
compatible = compatible.replace(/## Cách dùng[\s\S]*?(?=## Danh sách luồng)/,
  '## Cách dùng\n\nBản này dùng `flowchart LR` và `subgraph` để tương thích các trình dựng chưa hỗ trợ `swimlane-beta`. Mỗi khung biểu thị một bên chịu trách nhiệm; bố cục cột có thể khác ảnh vì flowchart tự sắp xếp theo đường nối.\n\n' +
  'Để có swimlane cột dọc, dùng [bản native](AGRISAGE_SWIMLANES.md). Các file rời ở [compatible](compatible/). Chỉ chép nội dung một khối Mermaid vào mỗi lần dựng.\n\n' +
  '`Agent / AI Reviewer` là Store Member có `can_review_ai`, không phải primary role riêng. Phân quyền động và ownership vẫn áp dụng cho mọi lane. Các sơ đồ là mức nghiệp vụ; nút “one transaction” gom các thay đổi atomic.\n\n');
fs.writeFileSync(path.join(root, 'AGRISAGE_SWIMLANES_FLOWCHART.md'), compatible);
console.log(JSON.stringify({ validation: 'Static lane/node/edge and reachability checks; not a Mermaid parser or visual render', diagrams: summary }, null, 2));
