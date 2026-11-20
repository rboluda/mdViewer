/**
 * Pantalla partida (código fuente | resultado):
 *  - desplazamiento sincronizado en ambos sentidos, anclado en los títulos, con un botón para activarlo;
 *  - selección reflejada: lo que se selecciona en una mitad se resalta en la otra;
 *  - números de línea en el código fuente.
 *
 * El código fuente de Vditor 4 es un <textarea>; la vista previa es HTML (.vditor-preview).
 */
import { markdownToText } from "./commands";
import { headingOffsets, pixelOffsets, sourceArea } from "./source";

let isSplit: () => boolean = () => false;

export function configure(options: { isSplit: () => boolean }): void {
  isSplit = options.isSplit;
}

const previewElement = () => document.querySelector<HTMLElement>("#editor .vditor-preview");
const contentElement = () => document.querySelector<HTMLElement>("#editor .vditor-content");

function readFlag(key: string, fallback: boolean): boolean {
  try {
    const value = window.localStorage.getItem(key);
    return value === null ? fallback : value === "1";
  } catch {
    return fallback;
  }
}

function writeFlag(key: string, value: boolean): void {
  try {
    window.localStorage.setItem(key, value ? "1" : "0");
  } catch {
    // Sin almacenamiento local: la preferencia no se recuerda.
  }
}

// ───────────── Anclas (posición de cada título en las dos mitades) ─────────────

let anchors: { source: number[]; preview: number[] } | null = null;

export function invalidate(): void {
  anchors = null;
  scheduleGutter();
}

function getAnchors(area: HTMLTextAreaElement, preview: HTMLElement): { source: number[]; preview: number[] } {
  if (anchors) return anchors;
  const sourcePx = pixelOffsets(area, headingOffsets(area.value));
  const box = preview.getBoundingClientRect();
  const headings = Array.from(preview.querySelectorAll<HTMLElement>(".vditor-reset h1, .vditor-reset h2, .vditor-reset h3, .vditor-reset h4, .vditor-reset h5, .vditor-reset h6"));
  const previewPx = headings.map((h) => h.getBoundingClientRect().top - box.top + preview.scrollTop);
  const n = Math.min(sourcePx.length, previewPx.length);
  anchors = { source: sourcePx.slice(0, n), preview: previewPx.slice(0, n) };
  return anchors;
}

/** Traduce una posición vertical de una mitad a la otra por tramos entre títulos. */
function mapPosition(y: number, from: number[], fromTotal: number, to: number[], toTotal: number): number {
  const a = [0, ...from, fromTotal];
  const b = [0, ...to, toTotal];
  let k = 0;
  while (k < a.length - 2 && a[k + 1] <= y) k++;
  const fraction = (y - a[k]) / Math.max(1, a[k + 1] - a[k]);
  return b[k] + fraction * (b[k + 1] - b[k]);
}

// ───────────── Desplazamiento sincronizado ─────────────

const SYNC_KEY = "mdv-sync-scroll";
let syncOn = readFlag(SYNC_KEY, true);
/** Valores puestos por código: el evento de desplazamiento que provocan no debe reenviarse. */
let expectedPreviewTop = -1;
let expectedSourceTop = -1;
/** Posición de la vista previa elegida por el usuario cuando la sincronización está desactivada. */
let freePreviewTop = 0;
/** Posición del resultado que originó la última sincronización inversa (resultado → código). */
let reverseOrigin = -1;

function setPreviewTop(preview: HTMLElement, top: number): void {
  const max = preview.scrollHeight - preview.clientHeight;
  const value = Math.max(0, Math.min(max, Math.round(top)));
  expectedPreviewTop = value;
  preview.scrollTop = value;
}

function setSourceTop(area: HTMLTextAreaElement, top: number): void {
  const max = area.scrollHeight - area.clientHeight;
  const value = Math.max(0, Math.min(max, Math.round(top)));
  expectedSourceTop = value;
  area.scrollTop = value;
}

function onSourceScroll(area: HTMLTextAreaElement, preview: HTMLElement): void {
  updateGutterScroll();
  updateSourceMark();
  if (!isSplit() || preview.style.display === "none") return;
  // Vditor ya ha movido la vista previa (su sincronización por proporción, que no se puede quitar);
  // este manejador corre después y deja el resultado definitivo.
  if (!syncOn) {
    preview.scrollTop = freePreviewTop;
    return;
  }
  if (Math.abs(area.scrollTop - expectedSourceTop) <= 1) {
    // El código se ha movido para seguir al resultado: Vditor acaba de mover también el resultado
    // con su cálculo proporcional; se devuelve a donde lo dejó el usuario para evitar rebotes.
    expectedSourceTop = -1;
    if (reverseOrigin >= 0) setPreviewTop(preview, reverseOrigin);
    reverseOrigin = -1;
    return;
  }
  const sourceMax = area.scrollHeight - area.clientHeight;
  if (area.scrollTop >= sourceMax - 2) {
    setPreviewTop(preview, preview.scrollHeight);
    return;
  }
  const { source, preview: target } = getAnchors(area, preview);
  setPreviewTop(preview, mapPosition(area.scrollTop, source, area.scrollHeight, target, preview.scrollHeight));
}

function onPreviewScroll(area: HTMLTextAreaElement, preview: HTMLElement): void {
  if (Math.abs(preview.scrollTop - expectedPreviewTop) <= 1) {
    expectedPreviewTop = -1;
    return;
  }
  freePreviewTop = preview.scrollTop;
  if (!syncOn || !isSplit()) return;
  const previewMax = preview.scrollHeight - preview.clientHeight;
  if (preview.scrollTop >= previewMax - 2) {
    reverseOrigin = preview.scrollTop;
    setSourceTop(area, area.scrollHeight);
    return;
  }
  const { source, preview: target } = getAnchors(area, preview);
  reverseOrigin = preview.scrollTop;
  setSourceTop(area, mapPosition(preview.scrollTop, target, preview.scrollHeight, source, area.scrollHeight));
}

/** Botón (arriba, en medio de las dos mitades) para activar o desactivar la sincronización. */
export function createSyncButton(splitter: HTMLElement): void {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "mdv-sync-button";
  const render = () => {
    button.classList.toggle("mdv-sync-on", syncOn);
    button.textContent = syncOn ? "⇅" : "⇵";
    button.title = syncOn
      ? "Desplazamiento sincronizado: activado (clic para que cada mitad se mueva libremente)"
      : "Desplazamiento sincronizado: desactivado (clic para que las dos mitades se muevan juntas)";
    button.setAttribute("aria-pressed", String(syncOn));
  };
  render();
  button.addEventListener("pointerdown", (e) => e.stopPropagation());
  button.addEventListener("dblclick", (e) => e.stopPropagation());
  button.addEventListener("click", (e) => {
    e.stopPropagation();
    syncOn = !syncOn;
    writeFlag(SYNC_KEY, syncOn);
    render();
    const area = sourceArea();
    const preview = previewElement();
    if (syncOn && area && preview) onSourceScroll(area, preview);
    if (preview) freePreviewTop = preview.scrollTop;
  });
  splitter.appendChild(button);
}

export function isSyncOn(): boolean {
  return syncOn;
}

// ───────────── Selección reflejada ─────────────

const MIRROR = "mdv-mirror";
let sourceMark: { start: number; end: number } | null = null;

function normalize(text: string): string[] {
  return text.replace(/\s+/g, " ").trim().split(" ").filter(Boolean);
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** Nodos de texto de la vista previa con su posición dentro del texto concatenado. */
function previewTextIndex(preview: HTMLElement): { text: string; nodes: { node: Text; start: number }[] } {
  const root = preview.querySelector(".vditor-reset") ?? preview;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: (n) => (n.parentElement?.closest(".katex-mathml, style, script") ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
  });
  const nodes: { node: Text; start: number }[] = [];
  let text = "";
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    nodes.push({ node: n as Text, start: text.length });
    text += (n as Text).data;
  }
  return { text, nodes };
}

function rangeFromIndex(nodes: { node: Text; start: number }[], start: number, end: number): Range | null {
  const locate = (offset: number) => {
    for (let i = nodes.length - 1; i >= 0; i--) {
      if (nodes[i].start <= offset) return { node: nodes[i].node, offset: Math.min(offset - nodes[i].start, nodes[i].node.data.length) };
    }
    return null;
  };
  const a = locate(start);
  const b = locate(end);
  if (!a || !b) return null;
  const range = document.createRange();
  range.setStart(a.node, a.offset);
  range.setEnd(b.node, b.offset);
  return range;
}

function setHighlight(range: Range | null): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const highlights = (CSS as any).highlights as Map<string, unknown> | undefined;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const Highlight = (window as any).Highlight;
  if (!highlights || !Highlight) return;
  if (range) highlights.set(MIRROR, new Highlight(range));
  else highlights.delete(MIRROR);
}

/** Código → resultado: resalta en la vista previa el texto equivalente al seleccionado en el código. */
function mirrorSourceSelection(area: HTMLTextAreaElement, preview: HTMLElement): void {
  const selected = area.value.substring(area.selectionStart, area.selectionEnd);
  const words = normalize(markdownToText(selected));
  if (!words.length) {
    setHighlight(null);
    return;
  }
  const pattern = new RegExp(words.map(escape).join("\\s*"), "g");
  const { text, nodes } = previewTextIndex(preview);
  // Varias coincidencias posibles: se elige la más cercana a donde debería estar según los títulos.
  const { source, preview: target } = getAnchors(area, preview);
  const selectionPx = pixelOffsets(area, [area.selectionStart])[0];
  const expected = mapPosition(selectionPx, source, area.scrollHeight, target, preview.scrollHeight);
  const box = preview.getBoundingClientRect();
  let best: Range | null = null;
  let bestDistance = Infinity;
  let count = 0;
  for (const m of text.matchAll(pattern)) {
    if (++count > 60) break;
    const range = rangeFromIndex(nodes, m.index ?? 0, (m.index ?? 0) + m[0].length);
    if (!range) continue;
    const top = range.getBoundingClientRect().top - box.top + preview.scrollTop;
    const distance = Math.abs(top - expected);
    if (distance < bestDistance) {
      best = range;
      bestDistance = distance;
    }
  }
  setHighlight(best);
  if (best && syncOn) {
    const rect = best.getBoundingClientRect();
    if (rect.top < box.top || rect.bottom > box.bottom) {
      setPreviewTop(preview, rect.top - box.top + preview.scrollTop - preview.clientHeight / 3);
    }
  }
}

/** Resultado → código: marca en el código las líneas del texto seleccionado en la vista previa. */
function mirrorPreviewSelection(area: HTMLTextAreaElement, preview: HTMLElement, selection: Selection): void {
  const words = normalize(selection.toString());
  if (!words.length) {
    clearSourceMark();
    return;
  }
  // Entre palabras del resultado puede haber marcas de Markdown en el código (**, _, [, ](url)…).
  const between = "(?:[\\s*_~`>#|]|\\[|\\]|\\([^)\\s]*\\)|!|-)*?";
  let pattern: RegExp;
  try {
    pattern = new RegExp(words.map(escape).join(between), "g");
  } catch {
    return;
  }
  // Se prefiere la coincidencia en la misma sección que la selección del resultado.
  const range = selection.getRangeAt(0);
  const box = preview.getBoundingClientRect();
  const selectionTop = range.getBoundingClientRect().top - box.top + preview.scrollTop;
  const { source, preview: target } = getAnchors(area, preview);
  const expectedPx = mapPosition(selectionTop, target, preview.scrollHeight, source, area.scrollHeight);
  const candidates: { start: number; end: number }[] = [];
  for (const m of area.value.matchAll(pattern)) {
    candidates.push({ start: m.index ?? 0, end: (m.index ?? 0) + m[0].length });
    if (candidates.length > 60) break;
  }
  if (!candidates.length) {
    clearSourceMark();
    return;
  }
  const tops = pixelOffsets(area, candidates.map((c) => c.start));
  let bestIndex = 0;
  tops.forEach((top, i) => {
    if (Math.abs(top - expectedPx) < Math.abs(tops[bestIndex] - expectedPx)) bestIndex = i;
  });
  sourceMark = candidates[bestIndex];
  updateSourceMark();
  if (syncOn) {
    const top = tops[bestIndex];
    if (top < area.scrollTop || top > area.scrollTop + area.clientHeight - 40) {
      setSourceTop(area, top - area.clientHeight / 3);
    }
  }
}

function clearSourceMark(): void {
  sourceMark = null;
  document.querySelector(".mdv-source-mark")?.remove();
}

/** Banda translúcida sobre las líneas marcadas del código (un textarea no admite resaltados). */
function updateSourceMark(): void {
  const area = sourceArea();
  const content = contentElement();
  let mark = document.querySelector<HTMLElement>(".mdv-source-mark");
  if (!sourceMark || !area || !content) {
    mark?.remove();
    return;
  }
  const [top, bottom] = pixelOffsets(area, [sourceMark.start, sourceMark.end]);
  const lineHeight = parseFloat(getComputedStyle(area).lineHeight) || 20;
  if (!mark) {
    mark = document.createElement("div");
    mark.className = "mdv-source-mark";
    content.appendChild(mark);
  }
  const visibleTop = area.offsetTop + top - area.scrollTop;
  mark.style.left = `${area.offsetLeft}px`;
  mark.style.width = `${area.clientWidth}px`;
  mark.style.top = `${visibleTop}px`;
  mark.style.height = `${bottom - top + lineHeight}px`;
  // Recorte para que no se salga del área visible del código.
  const clipTop = Math.max(0, area.offsetTop - visibleTop);
  const clipBottom = Math.max(0, visibleTop + bottom - top + lineHeight - (area.offsetTop + area.clientHeight));
  mark.style.clipPath = `inset(${clipTop}px 0 ${clipBottom}px 0)`;
}

function onSelectionChange(): void {
  if (!isSplit()) return;
  const area = sourceArea();
  const preview = previewElement();
  if (!area || !preview) return;
  if (document.activeElement === area) {
    clearSourceMark();
    if (area.selectionEnd > area.selectionStart) mirrorSourceSelection(area, preview);
    else setHighlight(null);
    return;
  }
  const selection = window.getSelection();
  if (selection && selection.rangeCount && !selection.isCollapsed && preview.contains(selection.anchorNode)) {
    setHighlight(null);
    mirrorPreviewSelection(area, preview, selection);
  } else if (selection?.isCollapsed) {
    clearSourceMark();
  }
}

let selectionTimer = 0;
document.addEventListener("selectionchange", () => {
  window.clearTimeout(selectionTimer);
  selectionTimer = window.setTimeout(onSelectionChange, 120);
});

// ───────────── Números de línea ─────────────

let lineNumbersOn = false;
let gutterTimer = 0;

export function setLineNumbers(value: boolean): void {
  lineNumbersOn = value;
  document.body.classList.toggle("mdv-line-numbers", value);
  scheduleGutter();
}

function scheduleGutter(): void {
  window.clearTimeout(gutterTimer);
  gutterTimer = window.setTimeout(renderGutter, 120);
}

/** Dibuja los números de cada línea del código (las líneas largas ocupan varias filas). */
function renderGutter(): void {
  const area = sourceArea();
  const content = contentElement();
  let gutter = document.querySelector<HTMLElement>(".mdv-gutter");
  if (!lineNumbersOn || !area || !content || area.offsetParent === null) {
    gutter?.remove();
    return;
  }
  if (!gutter || gutter.parentElement !== content) {
    gutter?.remove();
    gutter = document.createElement("div");
    gutter.className = "mdv-gutter";
    gutter.innerHTML = '<div class="mdv-gutter-inner"></div>';
    content.appendChild(gutter);
  }
  const starts = [0];
  for (let i = area.value.indexOf("\n"); i !== -1; i = area.value.indexOf("\n", i + 1)) starts.push(i + 1);
  const tops = pixelOffsets(area, starts);
  const style = getComputedStyle(area);
  const inner = gutter.firstElementChild as HTMLElement;
  inner.style.fontSize = style.fontSize;
  inner.style.lineHeight = style.lineHeight;
  inner.style.fontFamily = style.fontFamily;
  inner.innerHTML = tops.map((top, i) => `<span style="top:${top}px">${i + 1}</span>`).join("");
  const width = 44;
  const paddingLeft = parseFloat(style.paddingLeft) || 0;
  gutter.style.width = `${width}px`;
  gutter.style.left = `${area.offsetLeft + paddingLeft - width - 10}px`;
  gutter.style.top = `${area.offsetTop}px`;
  gutter.style.height = `${area.clientHeight}px`;
  updateGutterScroll();
}

function updateGutterScroll(): void {
  const area = sourceArea();
  const inner = document.querySelector<HTMLElement>(".mdv-gutter-inner");
  if (area && inner) inner.style.transform = `translateY(${-area.scrollTop}px)`;
}

// ───────────── Conexión con el editor ─────────────

/** Engancha los eventos al textarea y a la vista previa actuales (una sola vez por elemento). */
export function attach(): void {
  const area = sourceArea();
  const preview = previewElement();
  if (!area) return;
  if (!area.dataset.mdvSplit) {
    area.dataset.mdvSplit = "1";
    // Se registra después del manejador de Vditor, así que su resultado prevalece.
    area.addEventListener("scroll", () => preview && onSourceScroll(area, preview));
    area.addEventListener("input", invalidate);
    new ResizeObserver(invalidate).observe(area);
  }
  if (preview && !preview.dataset.mdvSplit) {
    preview.dataset.mdvSplit = "1";
    preview.addEventListener("scroll", () => onPreviewScroll(area, preview));
    new MutationObserver(() => {
      anchors = null;
    }).observe(preview, { childList: true, subtree: true });
    freePreviewTop = preview.scrollTop;
  }
  invalidate();
}

/** Al salir de la pantalla partida o recrear el editor se quitan los resaltados. */
export function reset(): void {
  setHighlight(null);
  clearSourceMark();
  anchors = null;
  scheduleGutter();
}
