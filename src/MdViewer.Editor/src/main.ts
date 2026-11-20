import Vditor from "vditor";
import "vditor/dist/index.css";
import "./style.css";
import { postToHost, type EditorApi, type EditorMode, type EditorTheme, type LoadOptions } from "./bridge";

// Vditor carga sus módulos (lute, katex, mermaid, highlight.js…) bajo demanda desde `cdn`.
// Apuntamos a la copia local para funcionar sin conexión.
const localCdn = new URL("./vditor", window.location.href).href.replace(/\/$/, "");

let vditor: Vditor | null = null;
let currentTheme: EditorTheme = "light";
let currentMode: EditorMode = "ir";
let suppressChange = false;

/** Envía el número de caracteres del Markdown. Nunca lanza: un fallo aquí no debe romper el editor. */
function postStats(instance: Vditor | null): void {
  try {
    const characters = instance ? instance.getValue().replace(/\n$/, "").length : 0;
    postToHost({ type: "stats", characters });
  } catch (error) {
    postToHost({ type: "error", message: `stats: ${String(error)}` });
  }
}

function create(markdown: string): void {
  // Se destruye y se suelta la instancia anterior ANTES de crear la nueva:
  // Vditor invoca callbacks durante el constructor y no deben tocar una instancia destruida.
  const previous = vditor;
  vditor = null;
  previous?.destroy();
  suppressChange = true;

  // `created` solo existe tras el constructor; hasta entonces los callbacks ven null.
  // Vditor llama al contador antes de terminar de inicializarse (getValue fallaría), así que
  // solo contamos tras `after`.
  let created: Vditor | null = null;
  let initialized = false;
  created = new Vditor("editor", {
    value: markdown,
    mode: currentMode,
    lang: "es_ES",
    cdn: localCdn,
    height: "100%",
    theme: currentTheme === "dark" ? "dark" : "classic",
    cache: { enable: false },
    toolbarConfig: { hide: true },
    counter: {
      enable: true,
      type: "markdown",
      // El contador de Vditor incluye el texto de los SVG renderizados (diagramas);
      // contamos sobre el Markdown fuente, que es lo que el usuario escribe.
      after: () => {
        if (initialized) {
          postStats(created);
        }
      },
    },
    preview: {
      delay: 300,
      theme: { current: currentTheme === "dark" ? "dark" : "light" },
      hljs: { style: currentTheme === "dark" ? "github-dark" : "github", lineNumber: false },
      markdown: { toc: true, mark: true, footnotes: true, autoSpace: false, sup: true, sub: true },
      math: { inlineDigit: false, engine: "KaTeX" },
    },
    link: {
      isOpen: false,
      click: (element) => {
        const href = element.getAttribute("href") ?? element.textContent ?? "";
        if (href) {
          postToHost({ type: "openLink", href });
        }
      },
    },
    input: () => {
      if (!suppressChange) {
        postToHost({ type: "changed" });
      }
    },
    after: () => {
      suppressChange = false;
      initialized = true;
      postStats(created);
      postToHost({ type: "ready" });
    },
  });
  vditor = created;
}

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

const api: EditorApi = {
  load(markdown: string, options?: LoadOptions) {
    if (options?.theme) {
      currentTheme = options.theme;
      applyThemeClass(currentTheme);
    }
    if (options?.mode) {
      currentMode = options.mode;
    }
    applyBaseUrl(options?.baseUrl);
    create(markdown);
  },

  getMarkdown() {
    return vditor?.getValue() ?? "";
  },

  setTheme(theme: EditorTheme) {
    currentTheme = theme;
    applyThemeClass(theme);
    vditor?.setTheme(
      theme === "dark" ? "dark" : "classic",
      theme === "dark" ? "dark" : "light",
      theme === "dark" ? "github-dark" : "github",
    );
  },

  setMode(mode: EditorMode) {
    if (mode === currentMode || !vditor) {
      return;
    }
    // Vditor no cambia de modo en caliente: se recrea conservando el contenido.
    const markdown = vditor.getValue();
    currentMode = mode;
    create(markdown);
  },

  focus() {
    vditor?.focus();
  },
};

window.mdEditor = api;

window.addEventListener("error", (event) => postToHost({ type: "error", message: String(event.message) }));
window.addEventListener("unhandledrejection", (event) => postToHost({ type: "error", message: String(event.reason) }));

// Arranque: documento vacío hasta que el host envíe uno.
api.load("", { theme: window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light" });
