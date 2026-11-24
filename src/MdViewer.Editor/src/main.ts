import Vditor from "vditor";
import "vditor/dist/index.css";
import "./style.css";
import { postToHost, type EditorApi, type EditorMode, type EditorTheme, type LoadOptions } from "./bridge";
import * as commands from "./commands";
import * as search from "./search";
import * as modes from "./modes";
import * as outline from "./outline";
import * as split from "./split";
import * as codeblocks from "./codeblocks";
import * as changes from "./changes";

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

/**
 * Iconos de la barra de herramientas. Vditor los carga insertando el script "en línea", y la
 * política de seguridad de la página (CSP) bloquea los scripts en línea: se cargan aquí como
 * archivo propio con el mismo id, y Vditor ve que ya están y no lo intenta.
 * Vditor borra ese elemento al destruirse; si los iconos ya están en la página, basta con un
 * marcador que no se ejecuta (así no se duplican al recrear el editor).
 */
function ensureToolbarIcons(): void {
  if (document.getElementById("vditorIconScript")) return;
  const script = document.createElement("script");
  script.id = "vditorIconScript";
  if (document.getElementById("vditor-icon-bold")) {
    script.type = "text/plain";
  } else {
    script.src = `${localCdn}/dist/js/icons/ant.js`;
  }
  document.head.appendChild(script);
}

// ───────────── Pantalla partida: separador arrastrable ─────────────

const SPLIT_KEY = "mdv-split-ratio";

function readSplitRatio(): number {
  try {
    const value = Number(window.localStorage.getItem(SPLIT_KEY));
    return value >= 0.15 && value <= 0.85 ? value : 0.5;
  } catch {
    return 0.5;
  }
}

function saveSplitRatio(ratio: number): void {
  try {
    window.localStorage.setItem(SPLIT_KEY, String(ratio));
  } catch {
    // Sin almacenamiento local: la posición simplemente no se recuerda.
  }
}

// ───────────── Pantalla partida: tamaño de letra independiente en cada mitad ─────────────
//
// Se escala el tamaño de letra (no se usa zoom CSS) para que las medidas de desplazamiento sigan
// en píxeles normales: Vditor sincroniza el desplazamiento del código y de la vista previa por
// proporción de esas medidas, y así la sincronización no se altera.

type Pane = "source" | "preview";
const PANE_KEYS: Record<Pane, string> = { source: "mdv-scale-source", preview: "mdv-scale-preview" };
const paneScale: Record<Pane, number> = { source: 1, preview: 1 };
let activePane: Pane = "source";

function readPaneScales(): void {
  for (const pane of ["source", "preview"] as Pane[]) {
    try {
      const value = Number(window.localStorage.getItem(PANE_KEYS[pane]));
      paneScale[pane] = value >= 0.5 && value <= 3 ? value : 1;
    } catch {
      paneScale[pane] = 1;
    }
  }
  applyPaneScales();
}

function applyPaneScales(): void {
  document.documentElement.style.setProperty("--mdv-scale-source", String(paneScale.source));
  document.documentElement.style.setProperty("--mdv-scale-preview", String(paneScale.preview));
}

/** Cambia el tamaño de una mitad: "in", "out" o "reset". Devuelve el texto para la barra de estado. */
function zoomPane(pane: Pane, action: "in" | "out" | "reset"): string {
  const next = action === "reset" ? 1 : paneScale[pane] + (action === "in" ? 0.1 : -0.1);
  paneScale[pane] = Math.round(Math.min(3, Math.max(0.5, next)) * 100) / 100;
  applyPaneScales();
  split.invalidate();
  try {
    window.localStorage.setItem(PANE_KEYS[pane], String(paneScale[pane]));
  } catch {
    // Sin almacenamiento local: el tamaño no se recuerda entre sesiones.
  }
  return `${pane === "source" ? "Código" : "Resultado"}: ${Math.round(paneScale[pane] * 100)} %`;
}

function paneOf(target: EventTarget | null): Pane | null {
  const element = target instanceof Element ? target : null;
  if (element?.closest(".vditor-preview")) return "preview";
  if (element?.closest(".vditor-sv")) return "source";
  return null;
}

// La mitad "activa" es la última en la que se hizo clic o se escribió.
document.addEventListener("pointerdown", (e) => { activePane = paneOf(e.target) ?? activePane; }, true);
document.addEventListener("focusin", (e) => { activePane = paneOf(e.target) ?? activePane; }, true);

// Ctrl+rueda sobre una mitad cambia solo esa mitad (en lugar del zoom de toda la página).
document.addEventListener(
  "wheel",
  (e) => {
    if (currentMode !== "split" || !e.ctrlKey) return;
    const pane = paneOf(e.target);
    if (!pane) return;
    e.preventDefault();
    activePane = pane;
    postToHost({ type: "status", text: zoomPane(pane, e.deltaY < 0 ? "in" : "out") });
  },
  { capture: true, passive: false },
);

/** Coloca (o quita) el separador entre el código y la vista previa según el modo. */
function updateSplitter(): void {
  const content = document.querySelector<HTMLElement>("#editor .vditor-content");
  const source = content?.querySelector<HTMLElement>(":scope > .vditor-sv");
  const preview = content?.querySelector<HTMLElement>(":scope > .vditor-preview");
  let splitter = content?.querySelector<HTMLElement>(":scope > .mdv-splitter") ?? null;
  if (!content || !source || !preview) return;

  if (currentMode !== "split") {
    split.reset();
    splitter?.remove();
    source.style.flex = "";
    preview.style.flex = "";
    return;
  }

  const apply = (ratio: number) => {
    source.style.flex = `0 0 ${(ratio * 100).toFixed(2)}%`;
    preview.style.flex = "1 1 0";
  };
  apply(readSplitRatio());
  split.attach();
  if (splitter) return;

  splitter = document.createElement("div");
  splitter.className = "mdv-splitter";
  splitter.title = "Arrastra para cambiar el tamaño (doble clic: mitad y mitad)";
  source.after(splitter);
  split.createSyncButton(splitter);

  splitter.addEventListener("pointerdown", (event) => {
    event.preventDefault();
    const handle = event.currentTarget as HTMLElement;
    handle.setPointerCapture(event.pointerId);
    document.body.classList.add("mdv-resizing");
    const move = (e: PointerEvent) => {
      const box = content.getBoundingClientRect();
      const ratio = Math.min(0.85, Math.max(0.15, (e.clientX - box.left) / box.width));
      apply(ratio);
      saveSplitRatio(ratio);
    };
    const up = () => {
      handle.removeEventListener("pointermove", move);
      handle.removeEventListener("pointerup", up);
      document.body.classList.remove("mdv-resizing");
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", up);
  });
  splitter.addEventListener("dblclick", () => {
    apply(0.5);
    saveSplitRatio(0.5);
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
  ensureToolbarIcons();
  suppressChange = true;

  const splitMode = currentMode === "split";
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
      mode: splitMode ? "both" : "editor",
      actions: [], // sin la barra "Desktop / Tablet / Mobile" de la vista previa
      theme: { current: contentTheme() },
      hljs: { style: codeTheme(), lineNumber: false, renderMenu: codeblocks.renderCodeMenu },
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
      updateSplitter();
      split.attach();
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
    changes.clear();
    if (options?.theme) {
      currentTheme = options.theme;
      applyThemeClass(currentTheme);
    }
    if (options?.mode) currentMode = options.mode;
    if (options?.writing) Object.assign(modes.options, options.writing);
    if (options?.readonly !== undefined) readonly = options.readonly;
    if (options?.toolbar !== undefined) toolbarVisible = options.toolbar;
    if (options?.lineNumbers !== undefined) split.setLineNumbers(options.lineNumbers);
    document.body.classList.toggle("mdv-readonly", readonly);
    applyBaseUrl(options?.baseUrl);
    create(markdown);
  },

  getMarkdown: () => vditor?.getValue() ?? "",

  setValue(markdown: string) {
    vditor?.setValue(markdown);
    scheduleInfo();
  },

  reloadValue(markdown: string, previous: string) {
    // Recarga por cambios externos: se guarda el desplazamiento de cada zona que lo tenga
    // (documento, código fuente, vista previa) y se restaura tras sustituir el contenido.
    // Después se resalta unos segundos lo que ha cambiado respecto al texto anterior (el del
    // archivo, no el que devuelve el editor, que puede reformatear tablas, listas…).
    const root = document.getElementById("editor");
    const scrolled = root
      ? [...root.querySelectorAll<HTMLElement>("*")].filter((el) => el.scrollTop > 0).map((el) => ({ el, cls: el.className, top: el.scrollTop }))
      : [];
    vditor?.setValue(markdown);
    scheduleInfo();
    const restore = () => {
      for (const { el, cls, top } of scrolled) {
        const target = el.isConnected ? el : root?.getElementsByClassName(cls)[0];
        if (target instanceof HTMLElement) target.scrollTop = top;
      }
    };
    restore();
    requestAnimationFrame(() => {
      restore();
      const count = changes.show(previous, markdown);
      if (count > 0) postToHost({ type: "status", text: count === 1 ? "Recargado desde disco: 1 línea nueva o modificada (en verde)" : `Recargado desde disco: ${count} líneas nuevas o modificadas (en verde)` });
    });
  },

  setTheme(theme: EditorTheme) {
    currentTheme = theme;
    applyThemeClass(theme);
    vditor?.setTheme(vditorTheme(), contentTheme(), codeTheme());
  },

  setMode(mode: EditorMode) {
    changes.clear();
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
      updateSplitter();
      split.attach();
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
  setLineNumbers: (value) => split.setLineNumbers(value),
  zoomPane: (action, pane) => (currentMode === "split" ? zoomPane(pane ?? activePane, action) : ""),
};

window.mdEditor = api;

readPaneScales();
split.configure({ isSplit: () => currentMode === "split" });
commands.trackSelection();
commands.onChanged(() => {
  postToHost({ type: "changed" });
  scheduleInfo();
});
modes.install();
modes.installKeyFilter();
codeblocks.install();

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
