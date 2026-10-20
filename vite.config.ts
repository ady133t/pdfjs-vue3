import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vitejs.dev/config/
export default defineConfig({
  base: process.env.BASE || '/',
  plugins: [vue()],
  ssr: {
    noExternal: ['pdfjs-dist'],
  },
  optimizeDeps: {
    include: ['pdfjs-dist'],
  },
  build: {
    chunkSizeWarningLimit: 1600,
  },
})
