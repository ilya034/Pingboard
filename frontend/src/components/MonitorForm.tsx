import { useState, type FormEvent } from 'react'
import type { ApiFailure } from '../api/client'
import type { MonitorDto, MonitorFormValues } from '../api/types'
import { FieldErrorText, fieldErrorId, hasFieldError } from './FieldErrorText'

interface MonitorFormProps {
  /** Есть — форма редактирует существующий монитор; нет — создаёт новый. */
  initial?: MonitorDto
  submitting: boolean
  failure: ApiFailure | null
  onSubmit: (values: MonitorFormValues) => void
  onCancel: () => void
}

/** Те же границы, что и в домене (MonitorRules): клиент не должен отправлять заведомо 400. */
const NAME_MAX = 100
const URL_MAX = 2048
const INTERVAL_MIN = 10
const INTERVAL_MAX = 86_400

const INTERVAL_PRESETS = [30, 60, 300, 600, 1800, 3600]

/**
 * Форма создать/изменить. Локальная валидация повторяет серверную, но не заменяет её:
 * сервер всё равно проверяет всё сам, а здесь это только чтобы не гонять заведомо плохой запрос.
 * Состояние сбрасывается через key у родителя (key = id монитора), а не эффектом.
 */
export function MonitorForm({ initial, submitting, failure, onSubmit, onCancel }: MonitorFormProps) {
  const [name, setName] = useState(initial?.name ?? '')
  const [url, setUrl] = useState(initial?.url ?? '')
  const [interval, setInterval] = useState(String(initial?.intervalSeconds ?? 60))
  const [enabled, setEnabled] = useState(initial?.enabled ?? true)
  const [localErrors, setLocalErrors] = useState<Record<string, string>>({})

  function submit(event: FormEvent) {
    event.preventDefault()

    const errors: Record<string, string> = {}
    const trimmedName = name.trim()
    const trimmedUrl = url.trim()
    const seconds = Number(interval)

    if (!trimmedName) errors.name = 'Имя не может быть пустым.'
    else if (trimmedName.length > NAME_MAX) errors.name = `Имя длиннее ${NAME_MAX} символов.`

    if (!isHttpUrl(trimmedUrl)) errors.url = 'Нужен абсолютный http(s)-адрес, например https://example.com'
    else if (hasUrlCredentials(trimmedUrl)) errors.url = 'Уберите логин и пароль из адреса: они попадут в логи.'

    if (!Number.isInteger(seconds) || seconds < INTERVAL_MIN || seconds > INTERVAL_MAX) {
      errors.intervalSeconds = `Интервал — целое число секунд от ${INTERVAL_MIN} до ${INTERVAL_MAX}.`
    }

    setLocalErrors(errors)
    if (Object.keys(errors).length > 0) return

    onSubmit({ name: trimmedName, url: trimmedUrl, intervalSeconds: seconds, enabled })
  }

  return (
    <form className="card monitor-form" onSubmit={submit}>
      <h2>{initial ? `Монитор «${initial.name}»` : 'Новый монитор'}</h2>

      <div className="field">
        <label htmlFor="monitor-name">Имя</label>
        <input
          id="monitor-name"
          value={name}
          maxLength={NAME_MAX}
          placeholder="Мой блог"
          aria-invalid={hasFieldError(failure, 'name', localErrors.name) || undefined}
          aria-describedby={hasFieldError(failure, 'name', localErrors.name) ? fieldErrorId('name') : undefined}
          onChange={(event) => setName(event.target.value)}
        />
        <FieldErrorText failure={failure} field="name" local={localErrors.name} />
      </div>

      <div className="field">
        <label htmlFor="monitor-url">URL</label>
        <input
          id="monitor-url"
          value={url}
          maxLength={URL_MAX}
          placeholder="https://example.com"
          aria-invalid={hasFieldError(failure, 'url', localErrors.url) || undefined}
          aria-describedby={hasFieldError(failure, 'url', localErrors.url) ? fieldErrorId('url') : undefined}
          onChange={(event) => setUrl(event.target.value)}
        />
        <FieldErrorText failure={failure} field="url" local={localErrors.url} />
        <span className="hint">
          Внутренние адреса (10/8, 192.168/16, localhost) отклоняются: барьер SSRF включён по умолчанию.
          Логин и пароль в адресе тоже отклоняются — они попали бы в логи проверок.
        </span>
      </div>

      <div className="field">
        <label htmlFor="monitor-interval">Интервал, секунд</label>
        <input
          id="monitor-interval"
          type="number"
          min={INTERVAL_MIN}
          max={INTERVAL_MAX}
          value={interval}
          aria-invalid={hasFieldError(failure, 'intervalSeconds', localErrors.intervalSeconds) || undefined}
          aria-describedby={
            hasFieldError(failure, 'intervalSeconds', localErrors.intervalSeconds)
              ? fieldErrorId('intervalSeconds')
              : undefined
          }
          onChange={(event) => setInterval(event.target.value)}
        />
        <FieldErrorText failure={failure} field="intervalSeconds" local={localErrors.intervalSeconds} />
        <div className="presets">
          {INTERVAL_PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              className={`chip ${String(preset) === interval ? 'chip-active' : ''}`}
              onClick={() => setInterval(String(preset))}
            >
              {preset < 60 ? `${preset} с` : preset < 3600 ? `${preset / 60} мин` : `${preset / 3600} ч`}
            </button>
          ))}
        </div>
      </div>

      <label className="checkbox">
        <input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
        Включён (выключенный монитор воркер не проверяет)
      </label>

      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? 'Сохраняем…' : initial ? 'Сохранить' : 'Создать'}
        </button>
        <button type="button" className="btn btn-ghost" onClick={onCancel} disabled={submitting}>
          Отмена
        </button>
      </div>
    </form>
  )
}

function isHttpUrl(value: string): boolean {
  if (value.length === 0 || value.length > URL_MAX) return false

  try {
    const parsed = new URL(value)
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch {
    return false
  }
}

/** `https://user:secret@host` — валидный URL, но такие данные утекают в логи и в текст ошибок. */
function hasUrlCredentials(value: string): boolean {
  try {
    const parsed = new URL(value)
    return parsed.username.length > 0 || parsed.password.length > 0
  } catch {
    return false
  }
}
