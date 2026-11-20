/**
 * Índice de títulos del documento, título activo según el desplazamiento y estadísticas.
 */
import type Vditor from "vditor";
import { editableElement, markdownToText, mode } from "./commands";

export interface OutlineItem {
  level: number;
  text: string;
}

export interface Stats {
  words: number;
  characters: number;
  charactersNoSpaces: number;
  lines: number;
  paragraphs: number;
  readingMinutes: number;
}

function headingElements(): HTMLElement[] {
  const el = editableElement();
  if (!el) return [];
  return Array.from(el.querySelectorAll<HTMLElement>(":scope > h1, :scope > h2, :scope > h3, :scope > h4, :scope > h5, :scope > h6"));
}

function headingText(h: HTMLElement): string {
  const clone = h.cloneNode(true) as HTMLElement;
  clone.querySelectorAll(".vditor-ir__marker").forEach((m) => m.remove());
  return (clone.textContent ?? "").trim();
}

/** Títulos a partir del Markdown (sirve en todos los modos). */
export function outlineFromMarkdown(md: string): OutlineItem[] {
  const items: OutlineItem[] = [];
  let fence = false;
  for (const line of md.split("\n")) {
    if (/^\s*(```|~~~)/.test(line)) fence = !fence;
    if (fence) continue;
    const m = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
    if (m) items.push({ level: m[1].length, text: m[2].replace(/[*_`~]/g, "") });
  }
  return items;
}

export function scrollToHeading(index: number): void {
  if (mode() === "ir") {
    const target = headingElements()[index];
    if (target) {
      target.scrollIntoView({ block: "start", behavior: "smooth" });
      const range = document.createRange();
      range.selectNodeContents(target);
      range.collapse(false);
      const sel = window.getSelection();
      sel?.removeAllRanges();
      sel?.addRange(range);
    }
    return;
  }
  // En modo código fuente se busca la línea del título.
  const el = editableElement();
  if (!el) return;
  const lines = el.querySelectorAll<HTMLElement>("[data-type='heading-marker'], .vditor-sv__marker--heading");
  lines[index]?.scrollIntoView({ block: "start", behavior: "smooth" });
}

/** Índice del título visible en la parte superior de la ventana. */
export function activeHeadingIndex(): number {
  const headings = headingElements();
  let active = -1;
  for (let i = 0; i < headings.length; i++) {
    if (headings[i].getBoundingClientRect().top <= 120) active = i;
    else break;
  }
  return headings.length ? Math.max(0, active) : -1;
}

export function stats(vditor: Vditor): Stats {
  const md = vditor.getValue();
  const text = markdownToText(md);
  // Palabras: secuencias de letras/números; los ideogramas cuentan uno a uno.
  const words = (text.match(/[\p{L}\p{N}'’-]+/gu) ?? []).length;
  const characters = text.replace(/\n/g, "").length;
  const charactersNoSpaces = text.replace(/\s/g, "").length;
  const lines = md ? md.replace(/\n$/, "").split("\n").length : 0;
  const paragraphs = md.split(/\n\s*\n/).filter((p) => p.trim()).length;
  return { words, characters, charactersNoSpaces, lines, paragraphs, readingMinutes: Math.max(words ? 1 : 0, Math.round(words / 200)) };
}

export { headingText };
