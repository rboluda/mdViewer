# mdViewer

Editor Markdown **libre y de código abierto** para Windows, con edición en vivo: el Markdown se renderiza mientras escribes, sin panel de vista previa aparte.

> Estado: **esqueleto inicial (0.1)**. Ya abre, edita y guarda archivos `.md` con edición en vivo, modo código fuente, temas claro/oscuro, recuperación de borradores y autoactualización.

## Características actuales

- Edición en vivo estilo "renderizado instantáneo" y modo código fuente (`Ctrl+/`).
- Tablas, listas de tareas, notas al pie, `[toc]`, matemáticas (KaTeX), diagramas Mermaid, resaltado de código.
- Guardado atómico: un corte de luz nunca deja un archivo a medias.
- Respeta la codificación (UTF-8, UTF-8 con BOM, UTF-16, ANSI) y los saltos de línea originales.
- Borradores de recuperación cada 30 s y aviso al reabrir tras un cierre inesperado.
- Detecta cambios hechos al archivo desde otro programa.
- Actualización automática desde GitHub Releases.
- Funciona sin conexión: no se carga nada de internet.
- Tema azul y blanco con los colores del icono (predeterminado), además de claro, oscuro y según el sistema.

El icono se genera desde `icon.png` con `tools/make-icon.ps1`.

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
