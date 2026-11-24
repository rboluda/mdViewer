/**
 * Buscar y reemplazar dentro del documento.
 * Las coincidencias se resaltan con la API de resaltado de CSS (no modifica el DOM del editor).
 */
import type Vditor from "vditor";
import { editableElement, insertText } from "./commands";
import { replaceRange, selectAndReveal, sourceArea } from "./source";

export interface FindOptions {
  caseSensitive?: boolean;
  regex?: boolean;
  wholeWord?: boolean;
}

export interface FindResult {
  count: number;
  current: number; // 1-based; 0 si no hay coincidencias
  error?: string;
}

/** Coincidencias en modo código fuente (textarea): posiciones dentro de su texto. */
let sourceMatches: { start: number; end: number }[] = [];
let ranges: Range[] = [];
let index = -1;
let lastQuery = "";
let lastOptions: FindOptions = {};

function buildRegex(query: string, options: FindOptions): RegExp {
  let source = options.regex ? query : query.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  if (options.wholeWord) source = `\\b${source}\\b`;
  return new RegExp(source, options.caseSensitive ? "g" : "gi");
}

export function isVisible(node: Text): boolean {
  const parent = node.parentElement;
  if (!parent) return false;
  if (parent.closest(".vditor-ir__marker, .vditor-ir__preview svg, .katex-mathml")) {
    // Los marcadores ocultos del modo de edición en vivo no cuentan, salvo que estén desplegados.
    const node = parent.closest(".vditor-ir__node");
    if (!node || !node.classList.contains("vditor-ir__node--expand")) return false;
  }
  return parent.getClientRects().length > 0;
}

function collectRanges(regex: RegExp): Range[] {
  const root = editableElement();
  if (!root) return [];
  const result: Range[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const text = n as Text;
    if (!text.data || !isVisible(text)) continue;
    regex.lastIndex = 0;
    for (let m = regex.exec(text.data); m; m = regex.exec(text.data)) {
      if (m[0].length === 0) {
        regex.lastIndex++;
        continue;
      }
      const range = document.createRange();
      range.setStart(text, m.index);
      range.setEnd(text, m.index + m[0].length);
      result.push(range);
    }
  }
  return result;
}

function paint(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const highlights = (CSS as any).highlights as Map<string, unknown> | undefined;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const Highlight = (window as any).Highlight;
  if (!highlights || !Highlight) return;
  highlights.set("mdv-find", new Highlight(...ranges));
  if (index >= 0 && ranges[index]) {
    highlights.set("mdv-find-current", new Highlight(ranges[index]));
    const rect = ranges[index].getBoundingClientRect();
    const target = ranges[index].startContainer.parentElement;
    if (target && (rect.top < 60 || rect.bottom > window.innerHeight - 60)) {
      target.scrollIntoView({ block: "center" });
    }
  } else {
    highlights.delete("mdv-find-current");
  }
}

export function find(query: string, options: FindOptions = {}, fromCursor = true): FindResult {
  lastQuery = query;
  lastOptions = options;
  if (!query) {
    clear();
    return { count: 0, current: 0 };
  }
  let regex: RegExp;
  try {
    regex = buildRegex(query, options);
  } catch (error) {
    clear();
    return { count: 0, current: 0, error: String(error) };
  }
  const area = sourceArea();
  if (area) {
    sourceMatches = [];
    for (const m of area.value.matchAll(regex)) {
      if (m[0].length) sourceMatches.push({ start: m.index ?? 0, end: (m.index ?? 0) + m[0].length });
    }
    const caret = area.selectionStart;
    index = sourceMatches.length ? Math.max(0, sourceMatches.findIndex((s) => s.start >= caret)) : -1;
    if (index >= 0) selectAndReveal(area, sourceMatches[index].start, sourceMatches[index].end);
    return { count: sourceMatches.length, current: index + 1 };
  }
  ranges = collectRanges(regex);
  index = ranges.length ? 0 : -1;
  if (fromCursor && ranges.length) {
    const sel = window.getSelection();
    if (sel && sel.rangeCount) {
      const caret = sel.getRangeAt(0);
      const after = ranges.findIndex((r) => r.compareBoundaryPoints(Range.START_TO_START, caret) >= 0);
      index = after >= 0 ? after : 0;
    }
  }
  paint();
  return { count: ranges.length, current: index + 1 };
}

export function step(direction: 1 | -1): FindResult {
  const area = sourceArea();
  if (area) {
    if (!sourceMatches.length) return find(lastQuery, lastOptions);
    index = (index + direction + sourceMatches.length) % sourceMatches.length;
    selectAndReveal(area, sourceMatches[index].start, sourceMatches[index].end);
    return { count: sourceMatches.length, current: index + 1 };
  }
  if (!ranges.length) return find(lastQuery, lastOptions);
  index = (index + direction + ranges.length) % ranges.length;
  paint();
  return { count: ranges.length, current: index + 1 };
}

export function clear(): void {
  sourceMatches = [];
  ranges = [];
  index = -1;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const highlights = (CSS as any).highlights as Map<string, unknown> | undefined;
  highlights?.delete("mdv-find");
  highlights?.delete("mdv-find-current");
}

/** Sustituye la coincidencia actual. El host vuelve a llamar a find() para recalcular. */
export function replaceCurrent(replacement: string): void {
  const area = sourceArea();
  if (area) {
    const match = sourceMatches[index];
    if (!match) return;
    let text = replacement;
    if (lastOptions.regex) {
      try {
        text = area.value.substring(match.start, match.end).replace(buildRegex(lastQuery, lastOptions), replacement);
      } catch {
        // Expresión no válida: se usa el texto literal.
      }
    }
    replaceRange(area, match.start, match.end, text);
    return;
  }
  if (index < 0 || !ranges[index]) return;
  const range = ranges[index];
  let text = replacement;
  if (lastOptions.regex) {
    try {
      text = range.toString().replace(buildRegex(lastQuery, { ...lastOptions }), replacement);
    } catch {
      // Si la expresión no es válida, se usa el texto literal.
    }
  }
  const sel = window.getSelection();
  sel?.removeAllRanges();
  sel?.addRange(range);
  insertText(text);
}

/** Sustituye todas las coincidencias sobre el texto Markdown. Devuelve cuántas se cambiaron. */
export function replaceAll(vditor: Vditor, replacement: string): number {
  if (!lastQuery) return 0;
  let regex: RegExp;
  try {
    regex = buildRegex(lastQuery, lastOptions);
  } catch {
    return 0;
  }
  const md = vditor.getValue();
  const count = (md.match(regex) ?? []).length;
  if (count > 0) {
    // Con expresión regular se admiten referencias $1…; en texto literal se inserta tal cual.
    vditor.setValue(lastOptions.regex ? md.replace(regex, replacement) : md.replace(regex, () => replacement));
  }
  clear();
  return count;
}
