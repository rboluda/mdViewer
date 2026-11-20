/**
 * Utilidades para el modo código fuente. En Vditor 4 ese modo es un <textarea> (no HTML como la
 * edición en vivo), así que la selección, las líneas y el desplazamiento se manejan con su API.
 */

export function sourceArea(): HTMLTextAreaElement | null {
  return document.querySelector<HTMLTextAreaElement>("#editor textarea.vditor-sv");
}

/** Inicio y fin de la línea que contiene la posición indicada. */
export function lineBounds(text: string, position: number): { start: number; end: number } {
  const start = text.lastIndexOf("\n", Math.max(0, position - 1)) + 1;
  const newline = text.indexOf("\n", position);
  return { start: position > 0 && text[position - 1] === "\n" ? position : start, end: newline === -1 ? text.length : newline };
}

/** Sustituye un rango del texto como si se escribiera (respeta deshacer y avisa a Vditor). */
export function replaceRange(area: HTMLTextAreaElement, start: number, end: number, text: string): void {
  area.focus();
  area.setSelectionRange(start, end);
  if (!document.execCommand("insertText", false, text)) {
    area.setRangeText(text, start, end, "end");
    area.dispatchEvent(new Event("input", { bubbles: true }));
  }
}

/** Selecciona un rango y lo lleva a la vista (centrado verticalmente). */
export function selectAndReveal(area: HTMLTextAreaElement, start: number, end: number): void {
  area.focus({ preventScroll: true });
  area.setSelectionRange(start, end);
  scrollToOffset(area, start, 0.35);
}

/**
 * Desplaza el textarea para que el carácter <offset> quede a <fraction> de la altura visible.
 * La posición se mide con una copia invisible del textarea (mismo ancho, fuente y ajuste de línea),
 * porque las líneas largas ocupan varias filas.
 */
export function scrollToOffset(area: HTMLTextAreaElement, offset: number, fraction = 0): void {
  const style = getComputedStyle(area);
  const mirror = document.createElement("div");
  for (const prop of ["fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing", "tabSize", "paddingTop", "paddingLeft", "paddingRight", "borderLeftWidth", "borderRightWidth", "boxSizing"] as const) {
    mirror.style[prop] = style[prop];
  }
  mirror.style.position = "absolute";
  mirror.style.visibility = "hidden";
  mirror.style.whiteSpace = "pre-wrap";
  mirror.style.overflowWrap = "break-word";
  mirror.style.width = `${area.clientWidth}px`;
  mirror.style.left = "-10000px";
  mirror.style.top = "0";
  mirror.textContent = area.value.substring(0, offset);
  const marker = document.createElement("span");
  marker.textContent = "​";
  mirror.appendChild(marker);
  document.body.appendChild(mirror);
  const top = marker.offsetTop;
  mirror.remove();
  area.scrollTop = Math.max(0, top - area.clientHeight * fraction - 8);
}

/**
 * Posición vertical (en píxeles del contenido del textarea) de cada uno de los caracteres indicados,
 * medida de una vez con una única copia invisible del textarea.
 */
export function pixelOffsets(area: HTMLTextAreaElement, offsets: number[]): number[] {
  const style = getComputedStyle(area);
  const mirror = document.createElement("div");
  for (const prop of ["fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing", "tabSize", "paddingTop", "paddingLeft", "paddingRight", "borderLeftWidth", "borderRightWidth", "boxSizing"] as const) {
    mirror.style[prop] = style[prop];
  }
  Object.assign(mirror.style, { position: "absolute", visibility: "hidden", whiteSpace: "pre-wrap", overflowWrap: "break-word", width: `${area.clientWidth}px`, left: "-10000px", top: "0" });
  const markers: HTMLElement[] = [];
  let last = 0;
  for (const offset of offsets) {
    mirror.appendChild(document.createTextNode(area.value.substring(last, offset)));
    const marker = document.createElement("span");
    marker.textContent = "​";
    mirror.appendChild(marker);
    markers.push(marker);
    last = offset;
  }
  document.body.appendChild(mirror);
  const result = markers.map((m) => m.offsetTop);
  mirror.remove();
  return result;
}

/** Posiciones de inicio de los títulos Markdown (ignora los que están dentro de bloques de código). */
export function headingOffsets(text: string): number[] {
  const offsets: number[] = [];
  let fence = false;
  let position = 0;
  for (const line of text.split("\n")) {
    if (/^\s*(```|~~~)/.test(line)) fence = !fence;
    if (!fence && /^#{1,6}\s+\S/.test(line)) offsets.push(position);
    position += line.length + 1;
  }
  return offsets;
}
