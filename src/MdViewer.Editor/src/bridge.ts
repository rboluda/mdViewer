/**
 * Puente entre el editor (JS) y la aplicación (C#).
 *
 * JS → C#: mensajes JSON con window.chrome.webview.postMessage.
 * C# → JS: llamadas a window.mdEditor.* con ExecuteScriptAsync (deben ser síncronas;
 *          los resultados asíncronos se devuelven con un mensaje).
 *
 * Mantener sincronizado con src/MdViewer.App/Editor/EditorBridge.cs.
 */
import type { FindOptions, FindResult } from "./search";
import type { OutlineItem, Stats } from "./outline";
import type { WritingOptions } from "./modes";

/** "ir": edición en vivo · "sv": código fuente · "split": código fuente con vista previa al lado. */
export type EditorMode = "ir" | "sv" | "split";

/** "blue" es el tema de la marca (azul y blanco) y el predeterminado. */
export type EditorTheme = "blue" | "light" | "dark";

export type HostMessage =
  | { type: "ready" }
  | { type: "changed" }
  | { type: "openLink"; href: string }
  | { type: "stats"; stats: Stats }
  | { type: "outline"; items: OutlineItem[] }
  | { type: "activeHeading"; index: number }
  | { type: "image"; name: string; data: string }
  | { type: "export"; id: string; html: string }
  | { type: "status"; text: string }
  | { type: "copy"; text: string }
  | { type: "error"; message: string };

interface WebViewHost {
  postMessage(message: unknown): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewHost };
    mdEditor: EditorApi;
  }
}

export interface LoadOptions {
  theme?: EditorTheme;
  mode?: EditorMode;
  /** URL base para imágenes y enlaces relativos (host virtual de la carpeta del documento). */
  baseUrl?: string | null;
  writing?: Partial<WritingOptions>;
  readonly?: boolean;
  toolbar?: boolean;
  lineNumbers?: boolean;
}

/** API que C# invoca. */
export interface EditorApi {
  load(markdown: string, options?: LoadOptions): void;
  getMarkdown(): string;
  setValue(markdown: string): void;
  setTheme(theme: EditorTheme): void;
  setMode(mode: EditorMode): void;
  focus(): void;
  exec(command: string, arg?: string): void;
  selectionMarkdown(): string;
  selectionText(): string;
  selectionHtml(): string;
  markdownToHtml(md: string): string;
  linkAtCursor(): string;
  imageAtCursor(): string;
  codeAtCursor(): string;
  paste(text: string, html: string | null): void;
  insertText(text: string): void;
  insertMarkdown(md: string): void;
  find(query: string, options?: FindOptions): FindResult;
  findStep(direction: 1 | -1): FindResult;
  findClear(): void;
  replaceCurrent(replacement: string): void;
  replaceAll(replacement: string): number;
  scrollToHeading(index: number): void;
  setWriting(options: Partial<WritingOptions>): void;
  setReadonly(readonly: boolean): void;
  setToolbar(visible: boolean): void;
  renderExport(id: string): void;
  /** Zoom de la mitad activa en pantalla partida; devuelve "" si no se está en pantalla partida. */
  zoomPane(action: "in" | "out" | "reset", pane?: "source" | "preview"): string;
  setLineNumbers(value: boolean): void;
}

export function postToHost(message: HostMessage): void {
  const host = window.chrome?.webview;
  if (host) {
    host.postMessage(message);
  } else {
    // Fuera de WebView2 (npm run dev en el navegador): solo trazamos.
    console.debug("[host]", message);
  }
}
