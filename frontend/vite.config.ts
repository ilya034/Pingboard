import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev-прокси: браузер ходит на тот же origin (5173), а /api уезжает на Api-процесс.
// В «проде» тот же относительный /api проксирует nginx (deploy/nginx.conf), поэтому
// baseURL клиента — '/api' и о CORS в браузере думать не нужно вовсе.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
})
