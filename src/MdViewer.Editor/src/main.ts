import Vditor from "vditor";
import "vditor/dist/index.css";
import "./style.css";
import { postToHost, type EditorApi, type EditorMode, type EditorTheme, type LoadOptions } from "./bridge";
import * as commands from "./commands";
import * as search from "./search";
import * as modes from "./modes";
import * as outline from "./outline";

// Vditor carga sus módulos (lute, katex, mermaid, highlight.js…) bajo demanda desde `cdn`.
// Apuntamos a la copia local para funcionar sin conexión.
const localCdn = new URL("./vditor", window.location.href).href.replace(/\/$/, "");

/** Botones de la barra de herramientas (opcional, menú Ver). Sus atajos los usan los menús. */
const toolbar = [
  "headings", "bold", "italic", "strike", "link", "|",
  "list", "ordered-list", "check", "outdent", "indent", "|",
  "quote", "line", "code", "inline-code", "insert-before", "insert-after", "|",
  "table", "|", "undo", "redo",
];

let vditor: Vditor | null = null;
let currentTheme: EditorTheme = "blue";
let currentMode: EditorMode = "ir";
let toolbarVisible = false;
let readonly = false;
let suppressChange = false;

const vditorTheme = () => (currentTheme === "dark" ? "dark" : "classic");
const contentTheme = () => (currentTheme === "dark" ? "dark" : "light");
const codeTheme = () => (currentTheme === "dark" ? "github-dark" : "github");

/** Envía estadísticas, índice y título activo (con retardo para no penalizar al escribir). */
let infoTimer = 0;
function scheduleInfo(): void {
  window.clearTimeout(infoTimer);
  infoTimer = window.setTimeout(() => {
    if (!vditor) return;
    try {
      postToHost({ type: "stats", stats: outline.stats(vditor) });
      postToHost({ type: "outline", items: outline.outlineFromMarkdown(vditor.getValue()) });
      postToHost({ type: "activeHeading", index: outline.activeHeadingIndex() });
      decorateAlerts();
    } catch (error) {
      postToHost({ type: "error", message: `info: ${String(error)}` });
    }
  }, 300);
}

/** Da estilo a los avisos tipo GitHub (> [!NOTE], > [!WARNING]…). */
function decorateAlerts(): void {
  const el = commands.editableElement();
  if (!el) return;
  el.querySelectorAll("blockquote").forEach((quote) => {
    const match = /^\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]/i.exec(quote.textContent ?? "");
    quote.classList.remove("mdv-alert", "mdv-alert-note", "mdv-alert-tip", "mdv-alert-important", "mdv-alert-warning", "mdv-alert-caution");
    if (match) quote.classList.add("mdv-alert", `mdv-alert-${match[1].toLowerCase()}`);
  });
}

function create(markdown: string): void {
  // Se destruye y se suelta la instancia anterior ANTES de crear la nueva:
  // Vditor invoca callbacks durante el constructor y no deben tocar una instancia destruida.
  const previous = vditor;
  vditor = null;
  commands.attach(null);
  search.clear();
  previous?.destroy();
  suppressChange = true;

  const split = currentMode === "split";
  let created: Vditor | null = null;
  created = new Vditor("editor", {
    value: markdown,
    mode: currentMode === "ir" ? "ir" : "sv",
    lang: "es_ES",
    cdn: localCdn,
    height: "100%",
    theme: vditorTheme(),
    cache: { enable: false },
    toolbar,
    toolbarConfig: { hide: !toolbarVisible, pin: true },
    counter: { enable: false },
    preview: {
      delay: 300,
      mode: split ? "both" : "editor",
      theme: { current: contentTheme() },
      hljs: { style: codeTheme(), lineNumber: false },
      markdown: { toc: true, mark: true, footnotes: true, autoSpace: false, sup: true, sub: true },
      math: { inlineDigit: false, engine: "KaTeX" },
    },
    link: {
      isOpen: false,
      click: (element) => {
        const href = element.getAttribute("href") ?? element.textContent ?? "";
        if (href) postToHost({ type: "openLink", href });
      },
    },
    input: () => {
      if (!suppressChange) postToHost({ type: "changed" });
      scheduleInfo();
    },
    after: () => {
      vditor = created;
      commands.attach(created);
      suppressChange = false;
      if (readonly) created?.disabled();
      modes.apply();
      scheduleInfo();
      postToHost({ type: "ready" });
    },
  });
}

/** El tema azul usa la base clara de Vditor y se completa con CSS (style.css, [data-theme="blue"]). */
function applyThemeClass(theme: EditorTheme): void {
  document.documentElement.dataset.theme = theme;
}

/**
 * Las rutas relativas del documento (imágenes, enlaces) se resuelven contra la carpeta del archivo.
 * Es seguro cambiar <base> aquí: el bundle ya está cargado y `localCdn` es una URL absoluta.
 */
function applyBaseUrl(baseUrl: string | null | undefined): void {
  let base = document.querySelector("base");
  if (!baseUrl) {
    base?.remove();
    return;
  }
  if (!base) {
    base = document.createElement("base");
    document.head.prepend(base);
  }
  base.href = baseUrl;
}

// ───────────── Imágenes pegadas o arrastradas ─────────────

function sendImages(files: FileList | File[]): boolean {
  const images = Array.from(files).filter((f) => f.type.startsWith("image/"));
  for (const file of images) {
    const reader = new FileReader();
    reader.onload = () => {
      const data = String(reader.result).replace(/^data:[^,]*,/, "");
      postToHost({ type: "image", name: file.name || "imagen.png", data });
    };
    reader.readAsDataURL(file);
  }
  return images.length > 0;
}

document.addEventListener(
  "paste",
  (event) => {
    if (!event.isTrusted || !event.clipboardData) return;
    if (event.clipboardData.files.length && sendImages(event.clipboardData.files)) {
      event.preventDefault();
      event.stopPropagation();
    }
  },
  true,
);

document.addEventListener(
  "drop",
  (event) => {
    if (event.dataTransfer?.files.length && sendImages(event.dataTransfer.files)) {
      event.preventDefault();
      event.stopPropagation();
    }
  },
  true,
);

// ───────────── Exportación ─────────────

function renderExport(id: string): void {
  if (!vditor) return;
  const container = document.createElement("div");
  container.className = "vditor-reset mdv-export";
  container.style.cssText = "position:absolute;left:-10000px;top:0;width:860px";
  document.body.appendChild(container);
  Vditor.preview(container, vditor.getValue(), {
    cdn: localCdn,
    mode: contentTheme(),
    theme: { current: contentTheme(), path: `${localCdn}/dist/css/content-theme` },
    hljs: { style: codeTheme(), lineNumber: false },
    markdown: { toc: true, mark: true, footnotes: true, autoSpace: false, sup: true, sub: true },
    math: { inlineDigit: false, engine: "KaTeX" },
    after: () => {
      // Mermaid y otros renderizadores terminan de forma asíncrona.
      window.setTimeout(() => {
        const html = container.innerHTML;
        container.remove();
        postToHost({ type: "export", id, html });
      }, 800);
    },
  });
}

// ───────────── API para C# ─────────────

const api: EditorApi = {
  load(markdown: string, options?: LoadOptions) {
    if (options?.theme) {
      currentTheme = options.theme;
      applyThemeClass(currentTheme);
    }
    if (options?.mode) currentMode = options.mode;
    if (options?.writing) Object.assign(modes.options, options.writing);
    if (options?.readonly !== undefined) readonly = options.readonly;
    if (options?.toolbar !== undefined) toolbarVisible = options.toolbar;
    document.body.classList.toggle("mdv-readonly", readonly);
    applyBaseUrl(options?.baseUrl);
    create(markdown);
  },

  getMarkdown: () => vditor?.getValue() ?? "",

  setValue(markdown: string) {
    vditor?.setValue(markdown);
    scheduleInfo();
  },

  setTheme(theme: EditorTheme) {
    currentTheme = theme;
    applyThemeClass(theme);
    vditor?.setTheme(vditorTheme(), contentTheme(), codeTheme());
  },

  setMode(mode: EditorMode) {
    if (mode === currentMode || !vditor) {
      postToHost({ type: "ready" });
      return;
    }
    const wasSource = currentMode !== "ir";
    const isSource = mode !== "ir";
    currentMode = mode;
    if (wasSource && isSource) {
      // Entre código fuente y pantalla partida solo cambia la vista previa.
      vditor.setPreviewMode(mode === "split" ? "both" : "editor");
      postToHost({ type: "ready" });
      return;
    }
    // Vditor no cambia de modo en caliente: se recrea conservando el contenido.
    create(vditor.getValue());
  },

  focus: () => vditor?.focus(),
  exec: (command, arg) => commands.exec(command, arg),
  selectionMarkdown: () => commands.selectionMarkdown(),
  selectionText: () => commands.markdownToText(commands.selectionMarkdown()),
  selectionHtml: () => commands.markdownToHtml(commands.selectionMarkdown()),
  markdownToHtml: (md) => commands.markdownToHtml(md),
  linkAtCursor: () => commands.linkAtCursor(),
  imageAtCursor: () => commands.imageAtCursor(),
  codeAtCursor: () => commands.codeAtCursor(),
  paste: (text, html) => commands.paste(text, html),
  insertText: (text) => commands.insertText(text),
  insertMarkdown(md: string) {
    commands.editableElement()?.focus();
    vditor?.insertMD(md);
    postToHost({ type: "changed" });
  },
  find: (query, options) => search.find(query, options),
  findStep: (direction) => search.step(direction),
  findClear: () => search.clear(),
  replaceCurrent: (replacement) => search.replaceCurrent(replacement),
  replaceAll: (replacement) => (vditor ? search.replaceAll(vditor, replacement) : 0),
  scrollToHeading: (index) => outline.scrollToHeading(index),
  setWriting(options) {
    Object.assign(modes.options, options);
    modes.apply();
  },
  setReadonly(value: boolean) {
    readonly = value;
    if (value) vditor?.disabled();
    else vditor?.enable();
    document.body.classList.toggle("mdv-readonly", value);
  },
  setToolbar(visible: boolean) {
    toolbarVisible = visible;
    vditor?.updateToolbarConfig({ hide: !visible });
  },
  renderExport: (id) => renderExport(id),
};

window.mdEditor = api;

commands.trackSelection();
commands.onChanged(() => {
  postToHost({ type: "changed" });
  scheduleInfo();
});
modes.install();
modes.installKeyFilter();

// Título activo del índice al desplazarse.
let scrollTimer = 0;
document.addEventListener(
  "scroll",
  () => {
    window.clearTimeout(scrollTimer);
    scrollTimer = window.setTimeout(() => postToHost({ type: "activeHeading", index: outline.activeHeadingIndex() }), 120);
  },
  true,
);

window.addEventListener("error", (event) => postToHost({ type: "error", message: String(event.message) }));
window.addEventListener("unhandledrejection", (event) => postToHost({ type: "error", message: String(event.reason) }));

// Arranque: documento vacío hasta que el host envíe uno.
api.load("", { theme: "blue" });
