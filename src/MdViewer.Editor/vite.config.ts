import { defineConfig } from "vite";
import { viteStaticCopy } from "vite-plugin-static-copy";

// El editor se sirve desde una carpeta local mapeada en WebView2 (sin red),
// así que todo usa rutas relativas y los recursos de Vditor se copian junto al bundle.
export default defineConfig({
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    chunkSizeWarningLimit: 2048,
  },
  plugins: [
    viteStaticCopy({
      // node_modules/vditor/dist/js/... → dist/vditor/dist/js/... (Vditor espera `${cdn}/dist/...`)
      targets: [{ src: "node_modules/vditor/dist/**/*", dest: "vditor/dist", rename: { stripBase: 3 } }],
    }),
  ],
});
