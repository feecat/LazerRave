import { defineConfig } from 'vite';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  build: {
    outDir: fileURLToPath(new URL('../../../out/build/cloud/web/dist', import.meta.url)),
    emptyOutDir: true,
    rolldownOptions: {
      onwarn(warning, warn) {
        if (warning.code === 'MODULE_LEVEL_DIRECTIVE' && warning.message.includes('use client')) return;
        warn(warning);
      },
    },
  },
  server: { proxy: { '/api': 'http://localhost:5080', '/hubs': { target: 'http://localhost:5080', ws: true } } },
});
