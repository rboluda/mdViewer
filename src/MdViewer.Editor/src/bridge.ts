/**
 * Puente entre el editor (JS) y la aplicación (C#).
 *
 * JS → C#: mensajes JSON con window.chrome.webview.postMessage.
 * C# → JS: llamadas a window.mdEditor.* con ExecuteScriptAsync.
 *
 * Mantener sincronizado con src/MdViewer.App/Editor/EditorBridge.cs.
 */

export type EditorMode = "ir" | "sv" | "wysiwyg";
export type EditorTheme = "light" | "dark";

export type HostMessage =
  | { type: "ready" }
  | { type: "changed" }
  | { type: "openLink"; href: string }
  | { type: "stats"; characters: number }
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
}

/** API que C# invoca. Todos los métodos son síncronos o devuelven valores serializables. */
export interface EditorApi {
  load(markdown: string, options?: LoadOptions): void;
  getMarkdown(): string;
  setTheme(theme: EditorTheme): void;
  setMode(mode: EditorMode): void;
  focus(): void;
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
