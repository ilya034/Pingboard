// `defineConfig` берём из vitest/config, а не из vite: только он знает ключ `test`
// (в `UserConfigExport` из vite такого ключа нет, и `tsc` на нём падает).
import { defineConfig } from 'vitest/config'
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
  // Конфиг тестов живёт здесь, а не в отдельном vitest.config.ts: так он видит те же
  // алиасы и плагины, что сборка, и не появляется второй источник правды о путях.
  // `npm test` запускает это в режиме одного прохода (без watch) — для CI и pre-commit.
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
