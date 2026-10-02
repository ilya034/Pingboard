import { fieldError, type ApiFailure } from '../api/client'

/** Стабильный id для `aria-describedby`: поле и его сообщение должны быть связаны. */
export function fieldErrorId(field: string): string {
  return `${field}-error`
}

/**
 * Сообщение об ошибке под полем формы: сначала серверное (оно авторитетнее — сервер знает
 * свои правила), потом локальное, найденное до отправки.
 *
 * `role="alert"` — потому что ошибка появляется уже после отправки и без него скринридер
 * о ней не сообщит.
 */
export function FieldErrorText({
  failure,
  field,
  local,
}: {
  failure: ApiFailure | null
  field: string
  local?: string | undefined
}) {
  const message = fieldError(failure, field) ?? local
  if (!message) return null

  return (
    <span className="field-error" id={fieldErrorId(field)} role="alert">
      {message}
    </span>
  )
}

/** Есть ли ошибка у поля — для `aria-invalid` на самом инпуте. */
export function hasFieldError(failure: ApiFailure | null, field: string, local?: string): boolean {
  return Boolean(fieldError(failure, field) ?? local)
}
