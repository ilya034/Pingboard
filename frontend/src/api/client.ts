import axios, { type AxiosError } from 'axios'
import type { ProblemDetails } from './types'

const STORAGE_KEY = 'pingboard.auth'

/** Что лежит в localStorage: токен вместе с email (для шапки) и сроком жизни. */
export interface StoredAuth {
  accessToken: string
  email: string
  expiresAt: string
}

/** Ошибка API, приведённая к виду, удобному для формы и баннера. */
export interface ApiFailure {
  /** null — ответа не было вовсе (сеть, таймаут). */
  status: number | null
  message: string
  /** Разбивка по полям из ProblemDetails.errors: { url: 'URL должен быть абсолютным…' }. */
  fieldErrors: Record<string, string>
  traceId: string | null
  retryAfterSeconds: number | null
}

export function readStoredAuth(): StoredAuth | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null

    const parsed = JSON.parse(raw) as Partial<StoredAuth>
    // Тип проверяем, а не доверяем приведению: `{"accessToken": 123}` — валидный JSON,
    // из которого иначе получился бы заголовок `Bearer 123` и 401 на каждом запросе.
    if (typeof parsed.accessToken !== 'string' || parsed.accessToken.length === 0) return null

    // Просроченный токен не держим: иначе первый же запрос вернёт 401 и экран мигнёт логином.
    if (typeof parsed.expiresAt === 'string' && new Date(parsed.expiresAt).getTime() <= Date.now()) {
      localStorage.removeItem(STORAGE_KEY)
      return null
    }

    return {
      accessToken: parsed.accessToken,
      email: typeof parsed.email === 'string' ? parsed.email : '',
      expiresAt: typeof parsed.expiresAt === 'string' ? parsed.expiresAt : '',
    }
  } catch {
    // Битое значение в localStorage — не повод ронять приложение: считаем, что входа нет.
    return null
  }
}

export function writeStoredAuth(auth: StoredAuth | null): void {
  if (auth) localStorage.setItem(STORAGE_KEY, JSON.stringify(auth))
  else localStorage.removeItem(STORAGE_KEY)
}

// Токен для интерсептора живёт в модуле: React-состояние до него не достаёт, а прокидывать
// токен в каждый вызов — шум. Инициализация из localStorage закрывает случай «страница
// перезагружена», когда до эффектов React запрос уже может уйти.
let currentToken: string | null = readStoredAuth()?.accessToken ?? null
let unauthorizedHandler: (() => void) | null = null

export function setAuthToken(token: string | null): void {
  currentToken = token
}

/** Куда сообщать о 401: подписывается AuthProvider, чтобы сбросить вход и увести на логин. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler
}

export const api = axios.create({
  // Относительный путь работает и в dev (прокси Vite), и в «проде» (nginx).
  baseURL: '/api',
  timeout: 15_000,
  headers: { 'Content-Type': 'application/json' },
})

api.interceptors.request.use((config) => {
  if (currentToken) config.headers.Authorization = `Bearer ${currentToken}`
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ProblemDetails>) => {
    // 401 от /auth/login и /auth/register — это «неверная пара email+пароль», а не истёкшая
    // сессия: сбрасывать вход и уводить на логин здесь нельзя, там пользователь и стоит.
    const url = error.config?.url ?? ''
    const isAuthCall = url.startsWith('/auth/')

    if (error.response?.status === 401 && !isAuthCall) {
      setAuthToken(null)
      writeStoredAuth(null)
      unauthorizedHandler?.()
    }

    return Promise.reject(error)
  },
)

export function toApiFailure(error: unknown): ApiFailure {
  if (!axios.isAxiosError(error)) {
    return {
      status: null,
      message: error instanceof Error ? error.message : 'Неизвестная ошибка.',
      fieldErrors: {},
      traceId: null,
      retryAfterSeconds: null,
    }
  }

  if (!error.response) {
    const timedOut = error.code === 'ECONNABORTED' || error.code === 'ETIMEDOUT'
    return {
      status: null,
      message: timedOut
        ? 'API не ответил за 15 секунд. Проверьте, что процесс Api запущен.'
        : 'API недоступен: запрос не дошёл до сервера.',
      fieldErrors: {},
      traceId: null,
      retryAfterSeconds: null,
    }
  }

  const { status, data, headers } = error.response
  const problem = data as ProblemDetails | undefined

  const fieldErrors: Record<string, string> = {}
  for (const [field, messages] of Object.entries(problem?.errors ?? {})) {
    // Склеиваем все сообщения поля, а не берём первое: сервер умеет вернуть несколько
    // причин сразу, и «показали одну из трёх» заставляет пользователя чинить по одной.
    const text = messages?.filter((message) => message.length > 0).join(' ')
    if (text) fieldErrors[field] = text
  }

  const retryAfter = Number(headers?.['retry-after'])
  const retryAfterSeconds = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null

  let message = problem?.detail ?? problem?.title ?? describeStatus(status)
  if (status === 429 && retryAfterSeconds) message = `${message} Повторите через ${retryAfterSeconds} с.`

  return { status, message, fieldErrors, traceId: problem?.traceId ?? null, retryAfterSeconds }
}

/** Достаёт сообщение по имени поля, не завися от регистра ключа на сервере. */
export function fieldError(failure: ApiFailure | null, field: string): string | undefined {
  if (!failure) return undefined

  const wanted = field.toLowerCase()
  for (const [key, message] of Object.entries(failure.fieldErrors)) {
    if (key.toLowerCase() === wanted) return message
  }

  return undefined
}

function describeStatus(status: number): string {
  switch (status) {
    case 401:
      return 'Сессия истекла, войдите заново.'
    case 403:
      return 'Этот монитор принадлежит другому пользователю.'
    case 404:
      return 'Запись не найдена.'
    case 409:
      return 'Конфликт состояния: объект с такими данными уже существует.'
    case 429:
      return 'Слишком много запросов.'
    case 499:
      return 'Запрос отменён.'
    case 503:
      return 'Сервис недоступен: база данных не отвечает.'
    default:
      return `Запрос завершился ошибкой ${status}.`
  }
}
