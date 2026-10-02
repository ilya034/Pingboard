import type { MonitorDto } from '../api/types'

export type MonitorState = 'up' | 'down' | 'paused' | 'unknown'

/**
 * Состояние для бейджа. Пауза важнее последней проверки: выключенный монитор не «в сети»,
 * он просто не проверяется — иначе дашборд показывал бы зелёный по устаревшим данным.
 */
export function monitorState(monitor: Pick<MonitorDto, 'enabled' | 'lastOk'>): MonitorState {
  if (!monitor.enabled) return 'paused'
  if (monitor.lastOk === null) return 'unknown'
  return monitor.lastOk ? 'up' : 'down'
}

export function formatLatency(ms: number | null | undefined): string {
  return ms === null || ms === undefined ? '—' : `${ms} мс`
}

/** uptime24h приходит долей 0..1 (или null, если проверок в окне не было). */
export function formatUptime(ratio: number | null | undefined): string {
  if (ratio === null || ratio === undefined) return 'нет данных'
  return `${(ratio * 100).toFixed(ratio >= 0.9995 ? 0 : 1)} %`
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export function formatRelative(value: string | null | undefined): string {
  if (!value) return 'ещё не проверялся'

  const seconds = Math.round((Date.now() - new Date(value).getTime()) / 1000)
  if (seconds < 0) return formatDateTime(value)
  if (seconds < 60) return `${seconds} с назад`
  if (seconds < 3600) return `${Math.round(seconds / 60)} мин назад`
  if (seconds < 86_400) return `${Math.round(seconds / 3600)} ч назад`
  return `${Math.round(seconds / 86_400)} дн назад`
}

export function formatInterval(seconds: number): string {
  if (seconds % 3600 === 0) return `${seconds / 3600} ч`
  if (seconds % 60 === 0) return `${seconds / 60} мин`
  return `${seconds} с`
}

export function formatWindow(hours: number): string {
  return hours === 1 ? 'последний час' : `последние ${hours} ч`
}

/** Среднее, минимум и максимум задержки по загруженным проверкам (без учёта сбоев без ответа). */
export function latencyStats(items: { latencyMs: number | null }[]): {
  average: number | null
  min: number | null
  max: number | null
} {
  const values = items.map((item) => item.latencyMs).filter((value): value is number => value !== null)
  if (values.length === 0) return { average: null, min: null, max: null }

  const sum = values.reduce((total, value) => total + value, 0)
  return {
    average: Math.round(sum / values.length),
    min: Math.min(...values),
    max: Math.max(...values),
  }
}
