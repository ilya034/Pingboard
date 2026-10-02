import { describe, expect, it } from 'vitest'
import {
  checkWindowSpan,
  formatInterval,
  formatLatency,
  formatUptime,
  formatWindow,
  formatWindowSpan,
  latencyStats,
  monitorState,
} from './format'

describe('monitorState', () => {
  it('пауза важнее последней проверки: выключенный монитор не «в сети»', () => {
    expect(monitorState({ enabled: false, lastOk: true })).toBe('paused')
    expect(monitorState({ enabled: false, lastOk: null })).toBe('paused')
  })

  it('без проверок — unknown, иначе up/down', () => {
    expect(monitorState({ enabled: true, lastOk: null })).toBe('unknown')
    expect(monitorState({ enabled: true, lastOk: true })).toBe('up')
    expect(monitorState({ enabled: true, lastOk: false })).toBe('down')
  })
})

describe('formatLatency', () => {
  it('пустое значение — прочерк, ноль остаётся нулём', () => {
    expect(formatLatency(null)).toBe('—')
    expect(formatLatency(undefined)).toBe('—')
    expect(formatLatency(0)).toBe('0 мс')
    expect(formatLatency(123)).toBe('123 мс')
  })
})

describe('formatUptime', () => {
  it('нет данных — словами, а не нулём', () => {
    expect(formatUptime(null)).toBe('нет данных')
    expect(formatUptime(undefined)).toBe('нет данных')
  })

  it('100 % показывается только при точном равенстве', () => {
    expect(formatUptime(1)).toBe('100 %')
    // Ключевой случай: округление «до ближайшего» показало бы тут 100 % при реальных 99,96.
    expect(formatUptime(0.9996)).toBe('99.9 %')
    expect(formatUptime(0.9999)).toBe('99.9 %')
  })

  it('округление строго вниз, чтобы не завышать доступность', () => {
    expect(formatUptime(0.999)).toBe('99.9 %')
    expect(formatUptime(0.9994)).toBe('99.9 %')
    expect(formatUptime(0.5)).toBe('50.0 %')
    expect(formatUptime(0)).toBe('0.0 %')
  })
})

describe('formatInterval и formatWindow', () => {
  it('интервал в удобных единицах', () => {
    expect(formatInterval(30)).toBe('30 с')
    expect(formatInterval(60)).toBe('1 мин')
    expect(formatInterval(300)).toBe('5 мин')
    expect(formatInterval(3600)).toBe('1 ч')
    expect(formatInterval(86_400)).toBe('24 ч')
  })

  it('окно называет себя по-русски', () => {
    expect(formatWindow(1)).toBe('последний час')
    expect(formatWindow(24)).toBe('последние 24 ч')
  })
})

describe('formatWindowSpan', () => {
  it('называет фактическое покрытие, а не запрошенное окно', () => {
    // 4 часа покрытия при запрошенных 24 — именно этот случай вводил в заблуждение.
    expect(formatWindowSpan('2026-01-01T00:00:00Z', '2026-01-01T04:00:00Z')).toBe('4 ч')
    expect(formatWindowSpan('2026-01-01T00:00:00Z', '2026-01-01T01:30:00Z')).toBe('1.5 ч')
    expect(formatWindowSpan('2026-01-01T00:00:00Z', '2026-01-01T00:30:00Z')).toBe('30 мин')
  })

  it('вырожденные случаи не дают подписи', () => {
    expect(formatWindowSpan(null, '2026-01-01T00:00:00Z')).toBe('')
    expect(formatWindowSpan('2026-01-01T00:00:00Z', null)).toBe('')
    expect(formatWindowSpan('2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')).toBe('')
    // Перевёрнутое окно тоже не подписываем: это признак битых данных.
    expect(formatWindowSpan('2026-01-01T05:00:00Z', '2026-01-01T00:00:00Z')).toBe('')
  })
})

describe('checkWindowSpan', () => {
  it('находит крайние проверки независимо от порядка', () => {
    const items = [
      { checkedAt: '2026-01-01T03:00:00Z' },
      { checkedAt: '2026-01-01T01:00:00Z' },
      { checkedAt: '2026-01-01T05:00:00Z' },
    ]

    expect(checkWindowSpan(items)).toEqual({
      from: '2026-01-01T01:00:00Z',
      to: '2026-01-01T05:00:00Z',
    })
  })

  it('пустой список и одна проверка', () => {
    expect(checkWindowSpan([])).toBeNull()
    expect(checkWindowSpan([{ checkedAt: '2026-01-01T01:00:00Z' }])).toEqual({
      from: '2026-01-01T01:00:00Z',
      to: '2026-01-01T01:00:00Z',
    })
  })
})

describe('latencyStats', () => {
  it('считает среднее, минимум и максимум без сбоев без ответа', () => {
    const stats = latencyStats([{ latencyMs: 100 }, { latencyMs: 200 }, { latencyMs: null }])

    expect(stats).toEqual({ average: 150, min: 100, max: 200 })
  })

  it('без замеров возвращает null, а не ноль', () => {
    expect(latencyStats([])).toEqual({ average: null, min: null, max: null })
    expect(latencyStats([{ latencyMs: null }])).toEqual({ average: null, min: null, max: null })
  })
})
