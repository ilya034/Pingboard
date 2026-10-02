import js from '@eslint/js'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

/**
 * ESLint (flat config). До этого линтера у фронтенда не было вовсе — при том, что бэкенд
 * собирается с `WarningsAsErrors`. Именно отсутствием линтера объяснялось то, что в
 * `MonitorDetailPage` `useState` стоял после раннего возврата: `tsc` такое нарушение правил
 * хуков не видит, а `react-hooks/rules-of-hooks` видит.
 *
 * Набор правил — из шаблона Vite `react-ts`, плюс `react-hooks` v7 (в нём правила хуков
 * разложены по отдельным проверкам: `rules-of-hooks`, `exhaustive-deps`, `refs`, `purity`,
 * `set-state-in-effect` и т. д.).
 */
export default tseslint.config(
  { ignores: ['dist', 'coverage', 'node_modules'] },

  js.configs.recommended,
  ...tseslint.configs.recommended,
  reactHooks.configs.flat.recommended,

  {
    files: ['**/*.{ts,tsx}'],
    plugins: { 'react-refresh': reactRefresh },
    rules: {
      // Файл, экспортирующий не только компоненты, ломает fast refresh Vite. Здесь это
      // ожидаемо: `Main.tsx`-подобные модули экспортируют состояния мутаций и константы.
      'react-refresh/only-export-components': 'off',
    },
  },

  // Конфиги сборки и тесты исполняются в Node: `process`, `__dirname` и т. п.
  {
    files: ['vite.config.ts', 'vitest.config.ts', 'src/**/*.test.ts'],
    languageOptions: {
      globals: { process: 'readonly', console: 'readonly' },
    },
  },
)
