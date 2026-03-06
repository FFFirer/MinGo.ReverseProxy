import { defineConfig } from 'vite'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    tailwindcss(),
  ],
  server: {
    port: 6139,
  },
  build: {
    outDir: './wwwroot',
    emptyOutDir: false,
    manifest: true,
    rollupOptions: {
      input: './index.css',
      output: {
        entryFileNames: '[name].css',
        assetFileNames: '[name][extname]'
      }
    }
  }
})