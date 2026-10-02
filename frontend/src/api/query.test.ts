import { QueryClient } from '@tanstack/react-query'
import { AxiosError, AxiosHeaders } from 'axios'
import { describe, expect, it } from 'vitest'
import type { MonitorDto } from './types'
import { manualRefetch, patchMonitorInList, restoreMonitorInList, retryQuery } from './query'

function httpError(status: number): AxiosError {
  return new AxiosError('failed', 'ERR', { url: '/monitors', headers: new AxiosHeaders() }, null, {
    status,
    statusText: String(status),
    data: null,
    headers: new AxiosHeaders(),
    config: { url: '/monitors', headers: new AxiosHeaders() } as never,
  })
}

describe('retryQuery', () => {
  it('повторяет сеть, таймаут и 5xx — но не больше двух раз', () => {
    expect(retryQuery(0, new AxiosError('offline', 'ERR_NETWORK'))).toBe(true)
    expect(retryQuery(0, new AxiosError('timeout', 'ECONNABORTED'))).toBe(true)
    expect(retryQuery(0, httpError(500))).toBe(true)
    expect(retryQuery(1, httpError(503))).toBe(true)
    expect(retryQuery(2, httpError(500))).toBe(false)
  })

  it('не повторяет 4xx: 400/401 бессмысленно, 429 — вредно', () => {
    expect(retryQuery(0, httpError(400))).toBe(false)
    expect(retryQuery(0, httpError(401))).toBe(false)
    expect(retryQuery(0, httpError(404))).toBe(false)
    expect(retryQuery(0, httpError(429))).toBe(false)
  })

  it('не повторяет то, что не является ошибкой axios', () => {
    expect(retryQuery(0, new Error('boom'))).toBe(false)
    expect(retryQuery(0, undefined)).toBe(false)
  })

  it('ручная попытка с кнопки «Повторить» не повторяется автоматически', async () => {
    // Пока идёт ручная попытка, политика повторов обязана молчать — иначе нажатие
    // запускает тот же цикл из трёх попыток, что и до него.
    let seenDuringAttempt: boolean | null = null

    await manualRefetch(async () => {
      seenDuringAttempt = retryQuery(0, httpError(500))
    })

    expect(seenDuringAttempt).toBe(false)
    // После завершения флаг снимается: обычные запросы снова повторяются.
    expect(retryQuery(0, httpError(500))).toBe(true)
  })

  it('флаг снимается даже при падении ручной попытки', async () => {
    await expect(manualRefetch(async () => Promise.reject(new Error('boom')))).rejects.toThrow('boom')

    expect(retryQuery(0, httpError(500))).toBe(true)
  })

  it('одновременные ручные попытки не снимают запрет на повторы друг для друга', async () => {
    // На дашборде падают и список мониторов, и история проверок: пока идёт вторая попытка,
    // первая уже завершилась — счётчик (а не флаг) обязан оставить запрет в силе.
    let duringSecond: boolean | null = null
    let releaseFirst: (() => void) | null = null

    const first = manualRefetch(
      () => new Promise<void>((resolve) => { releaseFirst = resolve }),
    )

    const second = manualRefetch(async () => {
      releaseFirst?.()
      await Promise.resolve()
      duringSecond = retryQuery(0, httpError(500))
    })

    await Promise.all([first, second])

    expect(duringSecond).toBe(false)
    expect(retryQuery(0, httpError(500))).toBe(true)
  })
})

function monitor(id: string, enabled: boolean): MonitorDto {
  return {
    id,
    name: id,
    url: `https://${id}.example.com`,
    intervalSeconds: 60,
    enabled,
    lastOk: true,
    lastCheckedAt: '2026-01-01T00:00:00Z',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    uptime24h: 1,
    lastLatencyMs: 10,
    uptimeBar: [],
  }
}

describe('patchMonitorInList', () => {
  it('меняет только нужный монитор и возвращает прежнюю версию для отката', () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData<MonitorDto[]>(['monitors'], [monitor('a', true), monitor('b', true)])

    const previous = patchMonitorInList(queryClient, 'a', { enabled: false })

    expect(previous?.enabled).toBe(true)
    const list = queryClient.getQueryData<MonitorDto[]>(['monitors'])
    expect(list?.find((item) => item.id === 'a')?.enabled).toBe(false)
    expect(list?.find((item) => item.id === 'b')?.enabled).toBe(true)
  })

  it('не создаёт список, если дашборд ещё не загрузился', () => {
    const queryClient = new QueryClient()

    expect(patchMonitorInList(queryClient, 'a', { enabled: false })).toBeNull()
    expect(queryClient.getQueryData(['monitors'])).toBeUndefined()
  })

  it('откат возвращает прежнее состояние', () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData<MonitorDto[]>(['monitors'], [monitor('a', true)])

    const previous = patchMonitorInList(queryClient, 'a', { enabled: false })
    restoreMonitorInList(queryClient, 'a', previous)

    expect(queryClient.getQueryData<MonitorDto[]>(['monitors'])?.[0].enabled).toBe(true)
  })

  it('без прежней версии откат ничего не портит', () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData<MonitorDto[]>(['monitors'], [monitor('a', true)])

    restoreMonitorInList(queryClient, 'a', null)

    expect(queryClient.getQueryData<MonitorDto[]>(['monitors'])?.[0].enabled).toBe(true)
  })
})
