/// <reference types="vite/client" />

/**
 * Типы переменных окружения сборки.
 *
 * Без этого объявления `import.meta.env.VITE_*` имеет тип `any`: в `vite/client` интерфейс
 * `ImportMetaEnv` объявлен как `Record<string, any>`, и опечатка в имени переменной
 * (`VITE_API_DOCS_PAHT`) не ловится ни `tsc`, ни сборкой — просто молча получается
 * `undefined`. Здесь перечислены все переменные, которые читает код.
 */
interface ImportMetaEnv {
  /** Путь к документации API; пустая строка или отсутствие — ссылки нет (см. lib/env.ts). */
  readonly VITE_API_DOCS_PATH?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
