import { fieldError, type ApiFailure } from '../api/client'

/**
 * Сообщение об ошибке под полем формы: сначала серверное (оно авторитетнее — сервер знает
 * свои правила), потом локальное, найденное до отправки.
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

  return <span className="field-error">{message}</span>
}
