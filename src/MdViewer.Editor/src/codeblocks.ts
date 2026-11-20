/**
 * Botones de los bloques de código: ajuste de línea (nuevo) y copiar.
 *
 * Vditor añade a cada bloque un botón de copiar cuya acción va en un atributo onclick; la política
 * de seguridad de la página (CSP) bloquea ese código en línea, así que aquí se gestiona su clic.
 */

import { postToHost } from "./bridge";

const WRAP_KEY = "mdv-code-wrap";
const WRAP_ICON =
  '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 6h18v2H3V6zm0 10h7v2H3v-2zm0-5h15a3.5 3.5 0 0 1 0 7h-2.17l1.58 1.59L16 21l-4-4 4-4 1.41 1.41L15.83 16H18a1.5 1.5 0 0 0 0-3H3v-2z"/></svg>';

function readWrap(): boolean {
  try {
    return window.localStorage.getItem(WRAP_KEY) === "1";
  } catch {
    return false;
  }
}

let wrap = readWrap();

function wrapLabel(): string {
  return wrap ? "Ajuste de línea: activado (clic para desactivar)" : "Ajuste de línea: desactivado (clic para activar)";
}

function applyWrap(): void {
  document.documentElement.classList.toggle("mdv-code-wrap", wrap);
  document.querySelectorAll<HTMLElement>(".mdv-wrap-button").forEach((button) => {
    button.setAttribute("aria-label", wrapLabel());
    button.classList.toggle("mdv-wrap-on", wrap);
  });
}

function toggleWrap(): void {
  wrap = !wrap;
  try {
    window.localStorage.setItem(WRAP_KEY, wrap ? "1" : "0");
  } catch {
    // Sin almacenamiento local: la preferencia no se recuerda.
  }
  applyWrap();
}

/**
 * Para la opción hljs.renderMenu de Vditor: añade el botón de ajuste de línea junto al de copiar.
 * Afecta a todos los bloques a la vez (y se recuerda), porque Vditor vuelve a dibujar los bloques al editar.
 */
export function renderCodeMenu(_code: HTMLElement, menu: HTMLElement): void {
  const button = document.createElement("span");
  button.className = "mdv-wrap-button vditor-tooltipped vditor-tooltipped__w";
  button.innerHTML = WRAP_ICON;
  button.setAttribute("aria-label", wrapLabel());
  button.classList.toggle("mdv-wrap-on", wrap);
  menu.appendChild(button);
}

export function install(): void {
  applyWrap();
  document.addEventListener(
    "click",
    (event) => {
      const target = event.target instanceof Element ? event.target : null;
      if (!target) return;

      if (target.closest(".mdv-wrap-button")) {
        event.preventDefault();
        event.stopPropagation();
        toggleWrap();
        return;
      }

      // Botón de copiar de Vditor (su onclick en línea está bloqueado por la CSP).
      const copy = target.closest<HTMLElement>(".vditor-copy > span:not(.mdv-wrap-button)");
      if (copy) {
        event.preventDefault();
        event.stopPropagation();
        // Lo copia la aplicación (portapapeles de Windows): el del navegador exige permisos y
        // un gesto del usuario que no siempre se dan dentro de WebView2.
        const text = copy.parentElement?.querySelector("textarea")?.value ?? "";
        postToHost({ type: "copy", text });
        copy.setAttribute("aria-label", "Copiado");
      }
    },
    true,
  );
  document.addEventListener(
    "mouseover",
    (event) => {
      const copy = event.target instanceof Element ? event.target.closest<HTMLElement>(".vditor-copy > span:not(.mdv-wrap-button)") : null;
      copy?.setAttribute("aria-label", "Copiar");
    },
    true,
  );
}
