/**
 * Comandos de edición que invocan los menús de la aplicación.
 *
 * Siempre que se puede se reutiliza la lógica de Vditor disparando sus propios atajos internos
 * (eventos sintéticos de teclado): así los formatos, títulos, listas y tablas se comportan
 * exactamente igual que al usarlos desde el editor. Lo que Vditor no ofrece se implementa aquí
 * sobre el DOM del modo "IR" o sobre el texto Markdown.
 */
import type Vditor from "vditor";

/** Acceso a la parte interna de Vditor (lute, modo actual, elemento editable). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Inner = any;

let current: Vditor | null = null;

export function attach(vditor: Vditor | null): void {
  current = vditor;
}

function inner(): Inner | null {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return current ? (current as any).vditor : null;
}

export function mode(): "ir" | "sv" | "wysiwyg" {
  return inner()?.currentMode ?? "ir";
}

/** Elemento editable del modo actual. */
export function editableElement(): HTMLElement | null {
  const v = inner();
  if (!v) return null;
  return v[v.currentMode]?.element ?? null;
}

// ───────────── Atajos internos de Vditor ─────────────

interface Hotkey {
  key: string;
  code: string;
  ctrl?: boolean;
  shift?: boolean;
  alt?: boolean;
}

/** Dispara un atajo de Vditor sobre el editor. Los eventos sintéticos no los bloquea el filtro de teclado. */
function hotkey(k: Hotkey): void {
  const el = editableElement();
  if (!el) return;
  restoreSelection();
  el.dispatchEvent(
    new KeyboardEvent("keydown", {
      key: k.key,
      code: k.code,
      ctrlKey: !!k.ctrl,
      shiftKey: !!k.shift,
      altKey: !!k.alt,
      bubbles: true,
      cancelable: true,
    }),
  );
}

const keyCode = (ch: string) => (/^[0-9]$/.test(ch) ? `Digit${ch}` : /^[a-z]$/i.test(ch) ? `Key${ch.toUpperCase()}` : ch);
const ctrl = (ch: string, shift = false): Hotkey => ({ key: ch, code: keyCode(ch), ctrl: true, shift });

/** Atajos de la barra de herramientas y de tabla de Vditor (ver vditor/src/ts/constants y fixBrowserBehavior). */
const vditorHotkeys: Record<string, Hotkey> = {
  bold: ctrl("b"),
  italic: ctrl("i"),
  strike: ctrl("d"),
  link: ctrl("k"),
  "inline-code": ctrl("g"),
  "code-block": ctrl("u"),
  list: ctrl("l"),
  "ordered-list": ctrl("o"),
  check: ctrl("j"),
  outdent: ctrl("I", true),
  indent: ctrl("O", true),
  quote: ctrl(";"),
  hr: ctrl("H", true),
  "insert-before": ctrl("B", true),
  "insert-after": ctrl("E", true),
  undo: ctrl("z"),
  redo: ctrl("y"),
  "table-row-above": ctrl("F", true),
  "table-row-below": ctrl("="),
  "table-col-left": ctrl("G", true),
  "table-col-right": { key: "+", code: "Equal", ctrl: true, shift: true },
  "table-row-delete": ctrl("-"),
  "table-col-delete": { key: "_", code: "Minus", ctrl: true, shift: true },
  "table-align-left": ctrl("L", true),
  "table-align-center": ctrl("C", true),
  "table-align-right": ctrl("R", true),
};

// ───────────── Selección ─────────────

let savedRange: Range | null = null;

/** Recuerda la selección del editor para poder actuar sobre ella tras abrir un menú o diálogo. */
export function trackSelection(): void {
  document.addEventListener("selectionchange", () => {
    const el = editableElement();
    const sel = window.getSelection();
    if (el && sel && sel.rangeCount > 0 && el.contains(sel.getRangeAt(0).startContainer)) {
      savedRange = sel.getRangeAt(0).cloneRange();
    }
  });
}

function restoreSelection(): void {
  const el = editableElement();
  if (!el) return;
  if (mode() === "sv") {
    el.focus();
    return;
  }
  const sel = window.getSelection();
  const inEditor = sel && sel.rangeCount > 0 && el.contains(sel.getRangeAt(0).startContainer);
  if (!inEditor && savedRange && el.contains(savedRange.startContainer)) {
    el.focus();
    sel?.removeAllRanges();
    sel?.addRange(savedRange);
  } else if (!inEditor) {
    el.focus();
  }
}

function currentRange(): Range | null {
  restoreSelection();
  const sel = window.getSelection();
  return sel && sel.rangeCount > 0 ? sel.getRangeAt(0) : null;
}

function selectNodeContents(node: Node): void {
  const range = document.createRange();
  range.selectNodeContents(node);
  const sel = window.getSelection();
  sel?.removeAllRanges();
  sel?.addRange(range);
}

/** Bloque de primer nivel (párrafo, título, lista, tabla…) que contiene el cursor. */
function topBlock(): HTMLElement | null {
  const el = editableElement();
  const range = currentRange();
  if (!el || !range) return null;
  let node: Node | null = range.startContainer;
  while (node && node.parentNode !== el) node = node.parentNode;
  return node instanceof HTMLElement ? node : null;
}

function closest(selector: string): HTMLElement | null {
  const range = currentRange();
  if (!range) return null;
  const start = range.startContainer;
  const element = start instanceof HTMLElement ? start : start.parentElement;
  return element?.closest<HTMLElement>(selector) ?? null;
}

/** Inserta texto en el cursor (o sustituye la selección) como si se escribiera. */
export function insertText(text: string): void {
  restoreSelection();
  if (mode() === "sv") {
    current?.insertValue(text);
    return;
  }
  document.execCommand("insertText", false, text);
}

/**
 * Inserta un bloque Markdown después del bloque actual (o en su lugar si está vacío)
 * y deja el cursor dentro del bloque nuevo.
 */
function insertMarkdownBlock(md: string, caretAtStart = false): void {
  restoreSelection();
  const v = inner();
  const el = editableElement();
  if (mode() === "sv" || !v || !el) {
    current?.insertValue(`\n${md}\n`);
    return;
  }
  const template = document.createElement("template");
  template.innerHTML = v.lute.Md2VditorIRDOM(md) as string;
  const nodes = Array.from(template.content.childNodes).filter((n) => n.nodeType === Node.ELEMENT_NODE);
  if (!nodes.length) return;

  const block = topBlock();
  if (block && !block.textContent?.trim() && block.tagName === "P") {
    block.replaceWith(...nodes);
  } else if (block) {
    block.after(...nodes);
  } else {
    el.append(...nodes);
  }
  const index = Array.from(el.children).indexOf(nodes[0] as Element);
  commitDom(index, caretAtStart);
}

// ───────────── Markdown de la selección ─────────────

/** Markdown de la selección actual (en modo IR se convierte el DOM con lute). */
export function selectionMarkdown(): string {
  const v = inner();
  const range = currentRange();
  if (!v || !range || range.collapsed) return "";
  if (mode() === "sv") return window.getSelection()?.toString() ?? "";
  const fragment = range.cloneContents();
  const plain = window.getSelection()?.toString() ?? "";
  // lute convierte bloques: si la selección está dentro de un párrafo, se envuelve en uno.
  const hasBlock = Array.from(fragment.childNodes).some((n) => n instanceof HTMLElement && n.hasAttribute("data-block"));
  const container = document.createElement("div");
  if (hasBlock) {
    container.appendChild(fragment);
  } else {
    const paragraph = document.createElement("p");
    paragraph.setAttribute("data-block", "0");
    paragraph.appendChild(fragment);
    container.appendChild(paragraph);
  }
  try {
    const md = (v.lute.VditorIRDOM2Md(container.innerHTML) as string).replace(/\n+$/, "");
    return md.trim() ? md : plain;
  } catch {
    return plain;
  }
}

export function markdownToHtml(md: string): string {
  const v = inner();
  return v ? (v.lute.Md2HTML(md) as string) : md;
}

export function markdownToText(md: string): string {
  const div = document.createElement("div");
  div.innerHTML = markdownToHtml(md);
  return (div.innerText || div.textContent || "").replace(/\n{3,}/g, "\n\n").trim();
}

function stripInlineMarkdown(md: string): string {
  return md
    .replace(/<\/?(u|mark|sup|sub|b|i|em|strong|s|del|code)>/gi, "")
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
    .replace(/(\*\*|__|~~|==)(.+?)\1/g, "$2")
    .replace(/(\*|_|~|\^)(.+?)\1/g, "$2")
    .replace(/`([^`]+)`/g, "$1");
}

// ───────────── Títulos ─────────────

function headingButtons(): { action: HTMLElement | null; levels: HTMLElement[] } {
  const v = inner();
  const toolbar: HTMLElement | undefined = v?.toolbar?.elements?.headings;
  const action = toolbar?.querySelector<HTMLElement>('[data-type="headings"]') ?? null;
  const levels = toolbar ? Array.from(toolbar.querySelectorAll<HTMLElement>("button[data-tag]")) : [];
  return { action, levels };
}

/** Nivel del título donde está el cursor (0 = no es un título). */
function currentHeadingLevel(): number {
  if (mode() === "sv") {
    const line = currentSourceLine();
    const match = /^(#{1,6})\s/.exec(line);
    return match ? match[1].length : 0;
  }
  const heading = closest("h1,h2,h3,h4,h5,h6");
  return heading ? Number(heading.tagName.substring(1)) : 0;
}

function setHeading(level: number): void {
  if (mode() === "sv") {
    replaceSourceLinePrefix(/^#{1,6}\s+/, level > 0 ? `${"#".repeat(level)} ` : "");
    return;
  }
  restoreSelection();
  const now = currentHeadingLevel();
  const { action, levels } = headingButtons();
  if (level === 0) {
    // En Vditor, pulsar el botón de títulos estando en un título lo convierte en párrafo.
    if (now > 0 && action) {
      action.classList.add("vditor-menu--current");
      action.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    }
    return;
  }
  levels[level - 1]?.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
}

// ───────────── Modo código fuente: líneas ─────────────

function currentSourceLine(): string {
  const text = current?.getValue() ?? "";
  const pos = current?.getCursorPosition();
  void pos;
  const sel = window.getSelection();
  const el = editableElement();
  if (!el || !sel || sel.rangeCount === 0) return "";
  const pre = document.createRange();
  pre.selectNodeContents(el);
  pre.setEnd(sel.getRangeAt(0).startContainer, sel.getRangeAt(0).startOffset);
  const offset = pre.toString().length;
  const start = text.lastIndexOf("\n", offset - 1) + 1;
  const end = text.indexOf("\n", offset);
  return text.substring(start, end === -1 ? text.length : end);
}

function replaceSourceLinePrefix(prefix: RegExp, replacement: string): void {
  // Selecciona la línea actual y la reescribe con el nuevo prefijo.
  const sel = window.getSelection();
  if (!sel) return;
  sel.modify("move", "backward", "lineboundary");
  sel.modify("extend", "forward", "lineboundary");
  const line = sel.toString();
  insertText(replacement + line.replace(prefix, ""));
}

// ───────────── Mover y borrar bloques ─────────────

/** Reconstruye el documento a partir del DOM modificado y deja el cursor en el bloque indicado. */
function commitDom(focusIndex: number, caretAtStart = false): void {
  const v = inner();
  const el = editableElement();
  if (!v || !el || !current) return;
  const md = v.lute.VditorIRDOM2Md(el.innerHTML) as string;
  current.setValue(md);
  const block = editableElement()?.children[Math.max(0, focusIndex)];
  // En tablas el cursor va a la primera celda del cuerpo; en el resto, al principio o al final del bloque.
  const target = block?.querySelector("tbody td") ?? block?.querySelector("pre code, .vditor-ir__marker--pre code") ?? block;
  if (target) {
    const range = document.createRange();
    range.selectNodeContents(target);
    range.collapse(caretAtStart || target !== block);
    const sel = window.getSelection();
    sel?.removeAllRanges();
    sel?.addRange(range);
    (target as HTMLElement).scrollIntoView({ block: "nearest" });
  }
  notifyChanged();
}

function moveBlock(direction: -1 | 1): void {
  if (mode() !== "ir") return;
  // Dentro de una tabla se mueven filas; fuera, bloques completos.
  const row = closest("tr");
  if (row && row.parentElement?.tagName === "TBODY") {
    const sibling = direction < 0 ? row.previousElementSibling : row.nextElementSibling;
    if (!sibling) return;
    if (direction < 0) sibling.before(row);
    else sibling.after(row);
    const block = row.closest<HTMLElement>("[data-block='0']");
    const index = block ? Array.from(block.parentElement!.children).indexOf(block) : 0;
    commitDom(index);
    return;
  }
  const block = topBlock();
  const el = editableElement();
  if (!block || !el) return;
  const sibling = direction < 0 ? block.previousElementSibling : block.nextElementSibling;
  if (!sibling) return;
  if (direction < 0) sibling.before(block);
  else sibling.after(block);
  commitDom(Array.from(el.children).indexOf(block));
}

function deleteBlock(): void {
  if (mode() !== "ir") {
    selectLine();
    document.execCommand("delete");
    return;
  }
  const block = topBlock();
  const el = editableElement();
  if (!block || !el) return;
  const index = Array.from(el.children).indexOf(block);
  block.remove();
  commitDom(Math.max(0, index - 1));
}

function selectWord(): void {
  restoreSelection();
  const sel = window.getSelection();
  if (!sel) return;
  sel.modify("move", "backward", "word");
  sel.modify("extend", "forward", "word");
  // En Windows la palabra incluye el espacio que la sigue: se excluye.
  for (let i = 0; i < 4 && /\s$/.test(sel.toString()); i++) {
    sel.modify("extend", "backward", "character");
  }
}

/**
 * Selecciona la línea actual. En edición en vivo una "línea" de Markdown es el párrafo, título,
 * elemento de lista o celda (el inicio visual de línea caería en medio de marcadores ocultos).
 */
function selectLine(): void {
  restoreSelection();
  if (mode() !== "ir") {
    const sel = window.getSelection();
    sel?.modify("move", "backward", "lineboundary");
    sel?.modify("extend", "forward", "lineboundary");
    return;
  }
  const target = closest("td, th, li > p, li, h1, h2, h3, h4, h5, h6, p") ?? topBlock();
  if (target) selectNodeContents(target);
}

function selectBlock(): void {
  const block = closest("td,th") ?? topBlock();
  if (block) selectNodeContents(block);
}

// ───────────── Listas de tareas ─────────────

function setTask(state: "toggle" | "done" | "todo"): void {
  const item = closest("li");
  const box = item?.querySelector<HTMLInputElement>(":scope > input[type=checkbox], :scope > p > input[type=checkbox]");
  if (!box) {
    hotkey(vditorHotkeys.check);
    return;
  }
  const want = state === "toggle" ? !box.checked : state === "done";
  if (box.checked !== want) box.click();
}

// ───────────── Enlaces ─────────────

export function linkAtCursor(): string {
  const link = closest('[data-type="a"], a');
  if (!link) return "";
  if (link.tagName === "A") return link.getAttribute("href") ?? "";
  return link.querySelector(".vditor-ir__link")?.textContent ?? "";
}

function removeLink(): void {
  const link = closest('[data-type="a"]');
  if (!link) return;
  const text = Array.from(link.querySelectorAll<HTMLElement>(":scope > span"))
    .filter((s) => !s.classList.contains("vditor-ir__marker") && !s.classList.contains("vditor-ir__link"))
    .map((s) => s.textContent)
    .join("");
  selectNodeContents(link);
  insertText(text || link.textContent || "");
}

/** Origen de la imagen donde está el cursor (o vacío). */
export function imageAtCursor(): string {
  const node = closest('[data-type="img"], img');
  if (!node) return "";
  const img = node.tagName === "IMG" ? (node as HTMLImageElement) : node.querySelector("img");
  return img?.getAttribute("src") ?? "";
}

export function codeAtCursor(): string {
  const block = closest('[data-type="code-block"], pre');
  if (!block) return "";
  const code = block.querySelector("pre code, code");
  return (code?.textContent ?? block.textContent ?? "").replace(/\n$/, "");
}

// ───────────── Notas al pie, referencias, metadatos ─────────────

function nextFootnoteNumber(md: string): number {
  let n = 1;
  for (const m of md.matchAll(/\[\^(\d+)\]/g)) n = Math.max(n, Number(m[1]) + 1);
  return n;
}

function appendToDocument(text: string): void {
  if (!current) return;
  const md = current.getValue().replace(/\s+$/, "");
  current.setValue(`${md}\n\n${text}\n`);
  notifyChanged();
}

function insertFootnote(): void {
  const md = current?.getValue() ?? "";
  const n = nextFootnoteNumber(md);
  insertText(`[^${n}]`);
  appendToDocument(`[^${n}]: `);
}

function insertLinkReference(): void {
  const label = window.getSelection()?.toString() || "texto";
  const md = current?.getValue() ?? "";
  let n = 1;
  while (md.includes(`[ref${n}]`)) n++;
  insertText(`[${label}][ref${n}]`);
  appendToDocument(`[ref${n}]: https://`);
}

function insertFrontMatter(): void {
  if (!current) return;
  const md = current.getValue();
  if (md.startsWith("---\n")) return;
  current.setValue(`---\ntitle: \n---\n\n${md}`);
  notifyChanged();
}

// ───────────── Notificación de cambios ─────────────

let changed: () => void = () => {};

export function onChanged(handler: () => void): void {
  changed = handler;
}

function notifyChanged(): void {
  changed();
}

// ───────────── Despacho de comandos ─────────────

export function exec(command: string, arg?: string): void {
  if (!current) return;

  if (/^heading-[1-6]$/.test(command)) {
    setHeading(Number(command.substring(8)));
    return;
  }

  const hk = vditorHotkeys[command];
  if (hk && !(command === "code-block" && mode() === "sv")) {
    hotkey(hk);
    return;
  }

  switch (command) {
    case "paragraph":
      setHeading(0);
      break;
    case "heading-up": {
      const level = currentHeadingLevel();
      setHeading(level === 0 ? 6 : Math.max(1, level - 1));
      break;
    }
    case "heading-down": {
      const level = currentHeadingLevel();
      if (level > 0) setHeading(level === 6 ? 0 : level + 1);
      break;
    }
    case "underline":
      insertText(`<u>${window.getSelection()?.toString() ?? ""}</u>`);
      break;
    case "comment":
      insertText(`<!-- ${window.getSelection()?.toString() ?? ""} -->`);
      break;
    case "clear-format": {
      const md = selectionMarkdown();
      if (md) insertText(stripInlineMarkdown(md));
      break;
    }
    case "math-block":
      insertMarkdownBlock("$$\n\n$$");
      break;
    case "code-block":
      insertMarkdownBlock("```\n\n```");
      break;
    case "toc":
      insertMarkdownBlock("[toc]");
      break;
    case "front-matter":
      insertFrontMatter();
      break;
    case "footnote":
      insertFootnote();
      break;
    case "link-reference":
      insertLinkReference();
      break;
    case "alert": {
      const kind = (arg ?? "NOTE").toUpperCase();
      insertMarkdownBlock(`> [!${kind}]\n> `);
      break;
    }
    case "table": {
      const [rows, cols] = (arg ?? "3x3").split("x").map((n) => Math.max(1, Number(n) || 3));
      const header = `| ${Array.from({ length: cols }, (_, i) => `Columna ${i + 1}`).join(" | ")} |`;
      const sep = `| ${Array.from({ length: cols }, () => "---").join(" | ")} |`;
      const body = Array.from({ length: Math.max(1, rows - 1) }, () => `| ${Array.from({ length: cols }, () => " ").join(" | ")} |`);
      insertMarkdownBlock([header, sep, ...body].join("\n"));
      break;
    }
    case "table-delete": {
      const table = closest("table");
      const block = table?.closest<HTMLElement>("[data-block='0']");
      const el = editableElement();
      if (block && el) {
        const index = Array.from(el.children).indexOf(block);
        block.remove();
        commitDom(Math.max(0, index - 1));
      }
      break;
    }
    case "task-toggle":
      setTask("toggle");
      break;
    case "task-done":
      setTask("done");
      break;
    case "task-todo":
      setTask("todo");
      break;
    case "move-up":
      moveBlock(-1);
      break;
    case "move-down":
      moveBlock(1);
      break;
    case "select-all": {
      const el = editableElement();
      if (el) selectNodeContents(el);
      break;
    }
    case "select-word":
      selectWord();
      break;
    case "select-line":
      selectLine();
      break;
    case "select-block":
      selectBlock();
      break;
    case "delete":
      restoreSelection();
      document.execCommand("delete");
      break;
    case "delete-word":
      selectWord();
      document.execCommand("delete");
      break;
    case "delete-line":
      selectLine();
      document.execCommand("delete");
      break;
    case "delete-block":
      deleteBlock();
      break;
    case "link-remove":
      removeLink();
      break;
    case "cut":
    case "copy":
      restoreSelection();
      document.execCommand(command);
      break;
    case "refresh-math":
      if (current) {
        current.setValue(current.getValue());
      }
      break;
    default:
      console.warn("Comando desconocido", command);
  }
}

/** Pega datos del portapapeles a través del propio manejador de pegado de Vditor. */
export function paste(text: string, html: string | null): void {
  const el = editableElement();
  if (!el) return;
  restoreSelection();
  const data = new DataTransfer();
  data.setData("text/plain", text);
  if (html) data.setData("text/html", html);
  el.dispatchEvent(new ClipboardEvent("paste", { clipboardData: data, bubbles: true, cancelable: true }));
}
