namespace MdViewer.App;

/// <summary>Textos de ayuda que se abren como documentos (Ayuda → Guía de Markdown).</summary>
internal static class HelpContent
{
    public const string MarkdownGuide = """
        # Guía rápida de Markdown

        Markdown es una forma sencilla de dar formato a un texto escribiendo unos pocos símbolos. En mdViewer el resultado aparece al momento; pulsa **Ctrl+ç** (o Ctrl+/) para ver el código fuente.

        ## Títulos

        Empieza la línea con almohadillas: una para el título principal, dos para un apartado, y así hasta seis.

        ```markdown
        # Título principal
        ## Apartado
        ### Subapartado
        ```

        Atajos: **Ctrl+1** a **Ctrl+6**, y **Ctrl+0** para volver a párrafo normal.

        ## Énfasis

        | Escribes | Resultado | Atajo |
        | --- | --- | --- |
        | `**negrita**` | **negrita** | Ctrl+B |
        | `*cursiva*` | *cursiva* | Ctrl+I |
        | `~~tachado~~` | ~~tachado~~ | Alt+Mayús+5 |
        | `==resaltado==` | ==resaltado== | |
        | `` `código` `` | `código` | Ctrl+Mayús+ñ |
        | `<u>subrayado</u>` | <u>subrayado</u> | Ctrl+U |
        | `H~2~O` y `x^2^` | H~2~O y x^2^ | |

        ## Listas

        - Una lista con viñetas empieza por `- `
        - Pulsa Tab para sangrar y Mayús+Tab para volver
            - Así se hace una sublista

        1. Una lista numerada empieza por `1. `
        2. Los números se ajustan solos

        - [x] Las tareas se escriben con `- [ ]`
        - [ ] Haz clic en la casilla para marcarla

        ## Citas y avisos

        > Una cita empieza por `> `.

        > [!TIP]
        > Los avisos empiezan por `> [!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]` o `[!CAUTION]`.

        ## Enlaces e imágenes

        Un enlace se escribe `[texto](https://ejemplo.com)` (Ctrl+K). Ctrl+clic para abrirlo.

        Una imagen se escribe `![descripción](ruta/imagen.png)`. También puedes pegarla con Ctrl+V o arrastrarla: mdViewer la guarda junto al documento.

        ## Tablas

        | Producto | Precio |
        | :--- | ---: |
        | Café | 1,20 € |
        | Té | 1,00 € |

        Los dos puntos de la segunda línea alinean la columna a la izquierda (`:---`), al centro (`:---:`) o a la derecha (`---:`). Inserta una tabla con **Ctrl+T**.

        ## Código

        ```python
        def saludo(nombre):
            return f"Hola, {nombre}"
        ```

        Escribe tres comillas invertidas y el nombre del lenguaje para resaltar la sintaxis (Ctrl+Mayús+K).

        ## Fórmulas

        En línea: $E = mc^2$. En bloque (Ctrl+Mayús+M):

        $$
        \int_0^1 x^2\,dx = \frac{1}{3}
        $$

        ## Diagramas

        ```mermaid
        graph LR
            Idea --> Borrador --> Revisión --> Publicado
        ```

        ## Notas al pie

        Una nota al pie se marca así[^1] y se define al final del documento.

        [^1]: Este es el texto de la nota.

        ## Índice y separadores

        Escribe `[toc]` en una línea para insertar el índice del documento, y `---` para una línea horizontal.
        """;
}
