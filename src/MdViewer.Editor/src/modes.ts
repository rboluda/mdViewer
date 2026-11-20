/**
 * Modos y ayudas de escritura: concentración, máquina de escribir, puntuación tipográfica,
 * cierre automático de paréntesis, solo lectura, corrector y filtro de atajos físicos.
 */
import { editableElement, mode } from "./commands";

export interface WritingOptions {
  focusMode: boolean;
  typewriterMode: boolean;
  smartPunctuation: boolean;
  autoPair: boolean;
  spellcheck: boolean;
}

export const options: WritingOptions = {
  focusMode: false,
  typewriterMode: false,
  smartPunctuation: false,
  autoPair: true,
  spellcheck: false,
};

export function apply(): void {
  document.body.classList.toggle("mdv-focus", options.focusMode);
  document.body.classList.toggle("mdv-typewriter", options.typewriterMode);
  const el = editableElement();
  if (el) el.spellcheck = options.spellcheck;
  updateActiveBlock();
}

// ───────────── Concentración y máquina de escribir ─────────────

let activeBlock: Element | null = null;

function updateActiveBlock(): void {
  const el = editableElement();
  const sel = window.getSelection();
  let block: Node | null = sel && sel.rangeCount ? sel.getRangeAt(0).startContainer : null;
  while (block && block.parentNode !== el) block = block.parentNode;
  if (activeBlock !== block) {
    activeBlock?.classList.remove("mdv-active");
    activeBlock = block instanceof Element ? block : null;
    activeBlock?.classList.add("mdv-active");
  }
}

function scrollContainer(): HTMLElement | null {
  const el = editableElement();
  if (!el) return null;
  let node: HTMLElement | null = el;
  while (node && node !== document.body) {
    const style = getComputedStyle(node);
    if (/(auto|scroll)/.test(style.overflowY) && node.scrollHeight > node.clientHeight) return node;
    node = node.parentElement;
  }
  return el;
}

function centerCaret(): void {
  const sel = window.getSelection();
  if (!sel || !sel.rangeCount) return;
  const range = sel.getRangeAt(0).cloneRange();
  range.collapse(true);
  let rect = range.getBoundingClientRect();
  if (!rect.height) {
    const parent = range.startContainer instanceof Element ? range.startContainer : range.startContainer.parentElement;
    if (!parent) return;
    rect = parent.getBoundingClientRect();
  }
  const container = scrollContainer();
  if (!container) return;
  const box = container.getBoundingClientRect();
  const delta = rect.top - (box.top + box.height / 2);
  if (Math.abs(delta) > 4) container.scrollBy({ top: delta, behavior: "auto" });
}

export function install(): void {
  document.addEventListener("selectionchange", () => {
    if (options.focusMode) updateActiveBlock();
    if (options.typewriterMode) requestAnimationFrame(centerCaret);
  });
  document.addEventListener("input", () => {
    if (options.focusMode) updateActiveBlock();
    if (options.typewriterMode) requestAnimationFrame(centerCaret);
  });

  // Puntuación tipográfica y cierre automático.
  document.addEventListener(
    "beforeinput",
    (event) => {
      if (event.inputType !== "insertText" || !event.data || mode() === "sv") return;
      const el = editableElement();
      const sel = window.getSelection();
      if (!el || !sel || !sel.rangeCount || !el.contains(sel.anchorNode)) return;
      const anchor = sel.anchorNode instanceof Element ? sel.anchorNode : sel.anchorNode?.parentElement;
      if (anchor?.closest("code, pre, .vditor-ir__marker, .language-math")) return;

      const before = textBeforeCaret(2);
      const data = event.data;

      if (options.smartPunctuation) {
        const opening = before === "" || /[\s([{¡¿—–-]$/.test(before);
        let replacement: string | null = null;
        let deleteBefore = 0;
        if (data === '"') replacement = opening ? "“" : "”";
        else if (data === "'") replacement = opening ? "‘" : "’";
        else if (data === "-" && before.endsWith("-")) {
          replacement = "—";
          deleteBefore = 1;
        } else if (data === "." && before.endsWith("..")) {
          replacement = "…";
          deleteBefore = 2;
        }
        if (replacement) {
          event.preventDefault();
          for (let i = 0; i < deleteBefore; i++) document.execCommand("delete");
          document.execCommand("insertText", false, replacement);
          return;
        }
      }

      if (options.autoPair) {
        const pairs: Record<string, string> = { "(": ")", "[": "]", "{": "}" };
        const close = pairs[data];
        if (close && sel.isCollapsed) {
          event.preventDefault();
          document.execCommand("insertText", false, data + close);
          sel.modify("move", "backward", "character");
        }
      }
    },
    true,
  );
}

function textBeforeCaret(length: number): string {
  const sel = window.getSelection();
  if (!sel || !sel.rangeCount) return "";
  const range = sel.getRangeAt(0);
  if (range.startContainer.nodeType !== Node.TEXT_NODE) return "";
  const text = (range.startContainer as Text).data;
  return text.substring(Math.max(0, range.startOffset - length), range.startOffset);
}

// ───────────── Filtro de atajos físicos ─────────────

/**
 * Los atajos de la aplicación los gestiona la ventana (C#). Vditor tiene sus propios atajos con
 * Ctrl que chocarían con ellos (Ctrl+L, Ctrl+H, Ctrl+D…), así que se bloquean los que el usuario
 * pulsa físicamente. Los comandos de los menús usan eventos sintéticos, que sí pasan.
 */
const allowedCtrlKeys = new Set(["a", "c", "v", "x", "z", "y", "arrowleft", "arrowright", "arrowup", "arrowdown", "home", "end", "backspace", "delete", "enter", "tab"]);

export function installKeyFilter(): void {
  document.addEventListener(
    "keydown",
    (event) => {
      if (!event.isTrusted) return;
      if ((event.ctrlKey || event.metaKey) && !allowedCtrlKeys.has(event.key.toLowerCase())) {
        event.stopPropagation();
        if (!event.shiftKey || /^[a-z0-9]$/i.test(event.key)) event.preventDefault();
      }
    },
    true,
  );
}
