/**
 * Resaltado temporal de lo que ha cambiado al recargar un archivo modificado por otro programa:
 * las líneas nuevas o cambiadas se marcan en verde unos segundos y después se desvanecen.
 * En el documento se usa la API de resaltado de CSS (no toca el DOM que Vditor convierte en
 * Markdown); en el código fuente, que es un <textarea>, unas bandas superpuestas.
 */
import { isVisible } from "./search";
import { sourceWords } from "./split";
import { pixelOffsets, sourceArea } from "./source";

const STEPS = ["mdv-changed", "mdv-changed-2", "mdv-changed-3", "mdv-changed-4"];
const HOLD_MS = 6000;
const FADE_STEP_MS = 300;
/** Por encima de este tamaño (líneas × líneas) no se calcula el detalle: se marca todo el tramo distinto. */
const MAX_DIFF_CELLS = 4_000_000;

interface LineRange {
  start: number; // primera línea (índice en el texto nuevo)
  end: number; // última línea, incluida
}

let ranges: LineRange[] = [];
let lines: string[] = [];
let step = -1;
let timers: number[] = [];

/** Tramos de líneas del texto nuevo que no estaban en el anterior (añadidas o modificadas). */
export function changedLines(oldText: string, newText: string): LineRange[] {
  const a = oldText.split("\n");
  const b = newText.split("\n");
  let prefix = 0;
  while (prefix < a.length && prefix < b.length && a[prefix] === b[prefix]) prefix++;
  let suffix = 0;
  while (suffix < a.length - prefix && suffix < b.length - prefix && a[a.length - 1 - suffix] === b[b.length - 1 - suffix]) suffix++;
  const midA = a.slice(prefix, a.length - suffix);
  const midB = b.slice(prefix, b.length - suffix);

  const changed = new Array<boolean>(midB.length).fill(true);
  if (midA.length > 0 && midB.length > 0 && midA.length * midB.length <= MAX_DIFF_CELLS) {
    // Subsecuencia común más larga: lo que no forma parte de ella es nuevo.
    const cols = midB.length + 1;
    const lcs = new Uint32Array((midA.length + 1) * cols);
    for (let i = midA.length - 1; i >= 0; i--) {
      for (let j = midB.length - 1; j >= 0; j--) {
        lcs[i * cols + j] = midA[i] === midB[j] ? lcs[(i + 1) * cols + j + 1] + 1 : Math.max(lcs[(i + 1) * cols + j], lcs[i * cols + j + 1]);
      }
    }
    for (let i = 0, j = 0; i < midA.length && j < midB.length; ) {
      if (midA[i] === midB[j]) {
        changed[j] = false;
        i++;
        j++;
      } else if (lcs[(i + 1) * cols + j] >= lcs[i * cols + j + 1]) i++;
      else j++;
    }
  }

  const result: LineRange[] = [];
  changed.forEach((isChanged, k) => {
    const line = prefix + k;
    if (!isChanged || b[line].trim() === "") return;
    const last = result[result.length - 1];
    if (last && last.end >= line - 1) last.end = line;
    else result.push({ start: line, end: line });
  });
  return result;
}

/** Resalta los cambios entre el texto que había y el recargado. Devuelve cuántas líneas han cambiado. */
export function show(oldText: string, newText: string): number {
  clear();
  ranges = changedLines(oldText, newText);
  lines = newText.split("\n");
  if (!ranges.length) return 0;
  step = 0;
  // La vista previa de la pantalla partida se dibuja con retraso: se repite al terminar.
  paint();
  timers.push(window.setTimeout(paint, 350), window.setTimeout(paint, 1000));
  timers.push(window.setTimeout(fade, HOLD_MS));
  return ranges.reduce((n, r) => n + r.end - r.start + 1, 0);
}

export function clear(): void {
  timers.forEach((t) => window.clearTimeout(t));
  timers = [];
  ranges = [];
  step = -1;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const highlights = (CSS as any).highlights as Map<string, unknown> | undefined;
  STEPS.forEach((name) => highlights?.delete(name));
  document.querySelectorAll(".mdv-changed-mark").forEach((el) => el.remove());
}

function fade(): void {
  step++;
  if (step >= STEPS.length) {
    clear();
    return;
  }
  paint();
  timers.push(window.setTimeout(fade, FADE_STEP_MS));
}

function paint(): void {
  if (step < 0) return;
  paintRendered();
  paintSource();
}

// ───────────── Documento (edición en vivo y vista previa) ─────────────

function renderedRoots(): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>("#editor .vditor-reset")].filter((el) => el.tagName !== "TEXTAREA" && el.getClientRects().length > 0);
}

function textIndex(root: HTMLElement): { text: string; nodes: { node: Text; start: number }[] } {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes: { node: Text; start: number }[] = [];
  let text = "";
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const node = n as Text;
    if (!node.data || !isVisible(node)) continue;
    nodes.push({ node, start: text.length });
    text += node.data;
  }
  return { text, nodes };
}

function toRange(nodes: { node: Text; start: number }[], start: number, end: number): Range | null {
  // El inicio va en el nodo donde empieza el texto; el final, en el nodo donde acaba.
  const locate = (offset: number, isStart: boolean) => {
    for (let i = nodes.length - 1; i >= 0; i--) {
      const n = nodes[i];
      if (isStart ? n.start <= offset : n.start < offset) {
        return { node: n.node, offset: Math.min(offset - n.start, n.node.data.length) };
      }
    }
    return null;
  };
  const a = locate(start, true);
  const b = locate(end, false);
  if (!a || !b) return null;
  const range = document.createRange();
  range.setStart(a.node, a.offset);
  range.setEnd(b.node, b.offset);
  return range;
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function paintRendered(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const highlights = (CSS as any).highlights as Map<string, unknown> | undefined;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const Highlight = (window as any).Highlight;
  if (!highlights || !Highlight) return;
  const found: Range[] = [];
  for (const root of renderedRoots()) {
    const { text, nodes } = textIndex(root);
    if (!text) continue;
    for (const range of ranges) {
      for (let line = range.start; line <= range.end; line++) {
        const words = sourceWords(lines[line]);
        if (!words.length) continue;
        const pattern = new RegExp(words.map(escape).join("\\s*"), "g");
        // Si el texto aparece varias veces, se elige la aparición más cercana a la posición
        // proporcional de la línea en el documento.
        const expected = (line / Math.max(1, lines.length - 1)) * text.length;
        let best: { index: number; length: number } | null = null;
        for (let m = pattern.exec(text); m; m = pattern.exec(text)) {
          if (!m[0].length) {
            pattern.lastIndex++;
            continue;
          }
          if (!best || Math.abs(m.index - expected) < Math.abs(best.index - expected)) best = { index: m.index, length: m[0].length };
        }
        const r = best && toRange(nodes, best.index, best.index + best.length);
        if (r) found.push(r);
      }
    }
  }
  STEPS.forEach((name) => highlights.delete(name));
  if (found.length) highlights.set(STEPS[step], new Highlight(...found));
}

// ───────────── Código fuente (textarea) ─────────────

function paintSource(): void {
  document.querySelectorAll(".mdv-changed-mark").forEach((el) => el.remove());
  const area = sourceArea();
  const content = area?.parentElement;
  if (!area || !content || area.offsetParent === null) return;
  if (!area.dataset.mdvChanges) {
    area.dataset.mdvChanges = "1";
    area.addEventListener("scroll", () => step >= 0 && paintSource());
  }
  const starts = [0];
  for (let i = area.value.indexOf("\n"); i !== -1; i = area.value.indexOf("\n", i + 1)) starts.push(i + 1);
  const wanted: number[] = [];
  for (const r of ranges) {
    if (r.start >= starts.length) continue;
    wanted.push(starts[r.start], r.end + 1 < starts.length ? starts[r.end + 1] : area.value.length);
  }
  const tops = pixelOffsets(area, wanted);
  const lineHeight = parseFloat(getComputedStyle(area).lineHeight) || 20;
  for (let k = 0; k < tops.length; k += 2) {
    const top = area.offsetTop + tops[k] - area.scrollTop;
    const isLastLine = wanted[k + 1] === area.value.length;
    const height = tops[k + 1] - tops[k] + (isLastLine ? lineHeight : 0);
    const visibleTop = Math.max(top, area.offsetTop);
    const visibleBottom = Math.min(top + height, area.offsetTop + area.clientHeight);
    if (visibleBottom <= visibleTop) continue;
    const mark = document.createElement("div");
    mark.className = `mdv-changed-mark ${STEPS[step]}`;
    Object.assign(mark.style, { left: `${area.offsetLeft}px`, width: `${area.clientWidth}px`, top: `${visibleTop}px`, height: `${visibleBottom - visibleTop}px` });
    content.appendChild(mark);
  }
}
