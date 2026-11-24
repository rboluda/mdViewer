# mdViewer

Editor Markdown **libre y de código abierto** para Windows, con edición en vivo: el Markdown se renderiza mientras escribes, sin panel de vista previa aparte.

## Características

**Escribir**
- Edición en vivo (la sintaxis aparece al situar el cursor), modo código fuente (`Ctrl+ç` o `Ctrl+/`) y pantalla partida con el código y el resultado.
- Menús completos de Párrafo y Formato con atajos: títulos (`Ctrl+1…6`), listas, tareas, citas, avisos tipo GitHub, tablas (insertar, filas, columnas, alineación), bloques de código y de fórmula, notas al pie, enlaces por referencia, índice `[toc]`, metadatos YAML, negrita, cursiva, subrayado, tachado, código, comentarios, borrar formato…
- Mover bloques y filas de tabla (`Alt+↑/↓`), seleccionar palabra, línea o bloque, copiar como Markdown, HTML o texto, pegar sin formato.
- Modo concentración (`F8`), máquina de escribir (`F9`), solo lectura, puntuación tipográfica, cierre automático de paréntesis y corrector ortográfico.
- Tablas, listas de tareas, notas al pie, matemáticas (KaTeX), diagramas Mermaid y resaltado de código.

**Organizar y encontrar**
- Barra lateral (`Ctrl+Mayús+L`) con índice del documento, árbol de archivos o lista de documentos de la carpeta, y búsqueda en toda la carpeta (`Ctrl+Mayús+F`).
- Buscar y reemplazar en el documento con expresiones regulares (`Ctrl+F`, `Ctrl+H`), apertura rápida de archivos (`Ctrl+P`), varias ventanas (`Ctrl+Mayús+N`, `Ctrl+Tab`).
- Imágenes: pegar, arrastrar o insertar (se copian a `./assets` o a la carpeta que elijas), rutas relativas, mover o copiar todas, descargar las de internet.

**Compartir**
- Exportar a PDF, HTML, HTML sin estilos e imagen PNG, e imprimir.
- Con [Pandoc](https://pandoc.org) instalado: exportar a Word, ODT, RTF, EPUB, LaTeX, MediaWiki, reStructuredText, Textile y OPML, e importar documentos.

**Archivos**
- Reabrir con otra codificación, recargar desde disco, mover, guardar todo, propiedades, mostrar en el Explorador, enviar a la papelera, recientes (archivos y carpetas).
- Guardado atómico: un corte de luz nunca deja un archivo a medias.
- Respeta la codificación (UTF-8, UTF-8 con BOM, UTF-16, ANSI) y los saltos de línea originales.
- Borradores de recuperación cada 30 s y aviso al reabrir tras un cierre inesperado.
- Aviso si otro programa modifica o elimina el archivo abierto: se puede cargar la nueva versión (los cambios se resaltan en verde unos segundos) o mantener la del editor.
- Actualización automática desde GitHub Releases.
- Funciona sin conexión: no se carga nada de internet.
- Tema azul y blanco con los colores del icono (predeterminado), además de claro, oscuro y según el sistema.

El icono se genera desde `icon.png` (icono completo con esquinas transparentes) con `tools/make-icon.ps1`.

## Instalar

Descarga el instalador de la [última versión](https://github.com/rboluda/mdViewer/releases/latest):

| Archivo | Para qué |
|---|---|
| `mdViewer-win.msi` | **Recomendado.** Instala en `C:\Program Files\mdViewer` para todos los usuarios del equipo (pide permisos de administrador). |
| `mdViewer-win-Setup.exe` | Instala solo para tu usuario, en `%LOCALAPPDATA%\mdViewer`, sin permisos de administrador. |
| `mdViewer-win-Portable.zip` | Sin instalar: descomprimir y ejecutar. |

Al instalarse, mdViewer aparece en **Abrir con** para los archivos `.md`, `.markdown`, `.mdown`, `.mkd` y `.qmd`. Para que sea el programa predeterminado: clic derecho en un `.md` → **Propiedades** → **Cambiar…** → mdViewer (o en Configuración → Aplicaciones → Aplicaciones predeterminadas). Windows no permite que un programa se ponga como predeterminado por su cuenta.

También se puede registrar o quitar a mano: `mdViewer.exe --register-file-types` / `mdViewer.exe --unregister-file-types`.

## Arquitectura

| Proyecto | Qué hace |
|---|---|
| `src/MdViewer.App` | Aplicación WPF (.NET 10): ventana, menús, archivos, actualizaciones |
| `src/MdViewer.Core` | Lógica sin interfaz: documentos, codificación, guardado atómico, configuración, recuperación, log |
| `src/MdViewer.Editor` | Motor de edición web (TypeScript + Vite + [Vditor](https://github.com/Vanessa219/vditor)) que se ejecuta dentro de WebView2 |
| `tests/MdViewer.Core.Tests` | Tests de xUnit v3 |

C# y el editor web se comunican con un puente de mensajes: `src/MdViewer.App/Editor/EditorBridge.cs` ↔ `src/MdViewer.Editor/src/bridge.ts`.

Datos del usuario:

- Configuración: `%APPDATA%\mdViewer\settings.json`
- Registros, borradores de recuperación y caché de WebView2: `%LOCALAPPDATA%\mdViewer.Data\`
- La aplicación instalada: `C:\Program Files\mdViewer\` (MSI) o `%LOCALAPPDATA%\mdViewer\` (Setup.exe). La gestiona Velopack; no guardar nada ahí.

## Compilar

Requisitos: [.NET SDK 10](https://dotnet.microsoft.com/download), [Node.js LTS](https://nodejs.org) y WebView2 Runtime (viene con Windows 11).

```powershell
dotnet build MdViewer.slnx
dotnet run --project src/MdViewer.App
dotnet test --solution MdViewer.slnx
```

La primera compilación ejecuta `npm ci` y `npm run build` del editor automáticamente. Si cambias el editor web, recompílalo con `npm run build` en `src/MdViewer.Editor` (o `dotnet build -p:BuildEditor=true`).

Para trabajar en el editor web con recarga en caliente en el navegador: `npm run dev` en `src/MdViewer.Editor`.

## Publicar una versión

Las versiones se generan solas con GitHub Actions al subir una etiqueta:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

El flujo [release.yml](.github/workflows/release.yml) compila, pasa los tests, empaqueta con [Velopack](https://velopack.io) y publica en GitHub Releases los instaladores (`mdViewer-win.msi` y `mdViewer-win-Setup.exe`), la versión portable y los paquetes de actualización. Las instalaciones existentes detectan la nueva versión al arrancar, la descargan en segundo plano y la aplican al cerrar o al pulsar "Reiniciar para actualizar". En una instalación en Archivos de programa, Windows pide permiso de administrador al aplicar la actualización.

## Licencia

[MIT](LICENSE)
