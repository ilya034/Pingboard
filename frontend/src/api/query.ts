import type { QueryClient } from '@tanstack/react-query'
import axios from 'axios'
import type { MonitorDto } from './types'

/** Сколько автоповторов после первой неудачи (в `main.tsx` было `failureCount < 2`). */
const RETRY_LIMIT = 2

/**
 * Счётчик одновременных ручных попыток.
 *
 * В TanStack Query v5 повторы задаёт опция `retry` самого запроса, а у `refetch()` опции
 * `retry` нет вовсе (её убрали из `RefetchOptions`). Кнопка «Повторить» появляется уже
 * после того, как автоповторы отказали, поэтому обычный `refetch()` запускает ровно тот же
 * цикл из трёх попыток заново: нажатие выглядит как «ничего не произошло». Счётчик
 * позволяет `retry` отличить ручную попытку и не повторять её — одну попытку делает
 * пользователь, и он же видит её результат.
 *
 * Именно счётчик, а не флаг: ручных попыток может быть несколько одновременно (на дашборде
 * падают и мониторы, и история проверок). Флаг, сброшенный первой завершившейся попыткой,
 * вернул бы автоповторы для ещё идущей второй.
 */
let manualAttempts = 0

/** Обновление по кнопке: ровно одна сетевая попытка. */
export async function manualRefetch(refetch: () => Promise<unknown>): Promise<void> {
  manualAttempts += 1
  try {
    await refetch()
  } finally {
    manualAttempts -= 1
  }
}

/**
 * Политика повторов для всех запросов (передаётся в `QueryClient` из `main.tsx`).
 *
 * Живёт здесь, а не в `client.ts`, по двум причинам: так рядом с `manualRefetch` видно, что
 * ручная попытка не повторяется, и не появляется цикл модулей `client.ts` ↔ `query.ts`
 * (`client.ts` не импортирует `query.ts` вовсе).
 */
export function retryQuery(failureCount: number, error: unknown): boolean {
  if (manualAttempts > 0) return false

  // Повторяем только сеть/таймаут (ответа не было) и 5xx: 400/401 повторять бессмысленно,
  // а 429 — вредно.
  if (!axios.isAxiosError(error)) return false
  const status = error.response?.status
  return status === undefined || status >= 500 ? failureCount < RETRY_LIMIT : false
}

/**
 * Оптимистично применяет патч к монитору в списке и возвращает прежнее значение для отката.
 *
 * Зачем не «просто дождаться ответа»: у строки дашборда нет своего индикатора, и без
 * оптимистичного обновления клик по «Пауза» до ответа сервера выглядит как «не сработало».
 * Заодно это закрывает гонку с поллингом (`refetchInterval` 10 с): перезапрос, стартовавший
 * до коммита PATCH, мог бы вернуть старое состояние и откатить уже переключённый бейдж.
 *
 * `setQueryData` обновляет **существующий** список и не создаёт новый: если дашборд ещё не
 * загрузился, записывать нечего и функция не делает ничего.
 */
export function patchMonitorInList(
  queryClient: QueryClient,
  id: string,
  patch: Partial<MonitorDto>,
): MonitorDto | null {
  const previous = queryClient.getQueryData<MonitorDto[]>(['monitors'])?.find((item) => item.id === id) ?? null

  queryClient.setQueryData<MonitorDto[]>(['monitors'], (list) =>
    list?.map((monitor) => (monitor.id === id ? { ...monitor, ...patch } : monitor)),
  )

  return previous
}

/** Откат оптимистичного патча: сбой запроса не должен оставлять UI во вранье. */
export function restoreMonitorInList(queryClient: QueryClient, id: string, previous: MonitorDto | null): void {
  if (!previous) return

  queryClient.setQueryData<MonitorDto[]>(['monitors'], (list) =>
    list?.map((monitor) => (monitor.id === id ? previous : monitor)),
  )
}
