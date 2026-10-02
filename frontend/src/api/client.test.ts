import { describe, expect, it } from 'vitest'
import { toApiFailure, fieldError } from './client'
import { AxiosError, AxiosHeaders } from 'axios'

/** Ошибка с ответом сервера — то, что приходит от Api. */
function httpError(status: number, data?: unknown, headers?: Record<string, string>): AxiosError {
  const config = { url: '/monitors', headers: new AxiosHeaders() }
  return new AxiosError('request failed', 'ERR_BAD_REQUEST', config, null, {
    status,
    statusText: String(status),
    data,
    headers: headers ? new AxiosHeaders(headers) : new AxiosHeaders(),
    config: config as never,
  })
}

/** Ошибка без ответа: сеть недоступна или истёк таймаут. */
function networkError(code = 'ERR_NETWORK'): AxiosError {
  return new AxiosError('network', code, { url: '/monitors', headers: new AxiosHeaders() } as never)
}

describe('toApiFailure', () => {
  it('разбирает ProblemDetails: detail, traceId и ошибки по полям', () => {
    const failure = toApiFailure(
      httpError(400, {
        title: 'Validation failed',
        detail: 'Некорректный запрос.',
        traceId: '0HN1:abc',
        errors: { url: ['URL должен быть абсолютным.', 'Ещё одна причина.'] },
      }),
    )

    expect(failure.status).toBe(400)
    expect(failure.message).toBe('Некорректный запрос.')
    expect(failure.traceId).toBe('0HN1:abc')
    // Все сообщения поля, а не только первое: иначе пользователь чинит причины по одной.
    expect(failure.fieldErrors.url).toBe('URL должен быть абсолютным. Ещё одна причина.')
  })

  it('для сети и таймаута отдаёт status: null и понятный текст', () => {
    const offline = toApiFailure(networkError())
    expect(offline.status).toBeNull()
    expect(offline.message).toContain('не дошёл до сервера')

    const timeout = toApiFailure(networkError('ECONNABORTED'))
    expect(timeout.status).toBeNull()
    expect(timeout.message).toContain('15 секунд')
  })

  it('добавляет Retry-After к тексту 429', () => {
    const failure = toApiFailure(httpError(429, { title: 'Слишком много запросов.' }, { 'retry-after': '7' }))

    expect(failure.retryAfterSeconds).toBe(7)
    expect(failure.message).toContain('Повторите через 7 с.')
  })

  it('подставляет текст по статусу, когда тела с ProblemDetails нет', () => {
    expect(toApiFailure(httpError(503)).message).toContain('база данных не отвечает')
    expect(toApiFailure(httpError(418)).message).toContain('418')
  })

  it('не падает на не-axios ошибке', () => {
    const failure = toApiFailure(new Error('что-то сломалось'))
    expect(failure.status).toBeNull()
    expect(failure.message).toBe('что-то сломалось')
    expect(failure.fieldErrors).toEqual({})
  })
})

describe('fieldError', () => {
  it('находит поле независимо от регистра ключа', () => {
    const failure = toApiFailure(httpError(400, { errors: { IntervalSeconds: ['Слишком часто.'] } }))

    expect(fieldError(failure, 'intervalSeconds')).toBe('Слишком часто.')
    expect(fieldError(failure, 'name')).toBeUndefined()
    expect(fieldError(null, 'name')).toBeUndefined()
  })
})
