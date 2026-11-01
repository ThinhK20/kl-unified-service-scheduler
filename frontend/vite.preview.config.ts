// Builds the live preview: ONE self-contained dist-preview/index.html (all JS and CSS inlined).
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';

const frontendDir = fileURLToPath(new URL('.', import.meta.url));

export default defineConfig({
  root: fileURLToPath(new URL('./src/preview', import.meta.url)),
  envDir: frontendDir,
  publicDir: false,
  plugins: [react(), viteSingleFile({ removeViteModuleLoader: true })],
  build: {
    outDir: fileURLToPath(new URL('./dist-preview', import.meta.url)),
    emptyOutDir: true,
    sourcemap: false,
    modulePreload: false,
  },
});
