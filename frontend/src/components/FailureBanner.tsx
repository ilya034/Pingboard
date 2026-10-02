import type { ApiFailure } from '../api/client'

/**
 * Баннер ошибки: сообщение из ProblemDetails + traceId. traceId показываем не «для красоты» —
 * по нему запрос ищется в JSON-логах Api (фактор XI), это часть SRE-обвязки проекта.
 */
export function FailureBanner({ failure, onRetry }: { failure: ApiFailure; onRetry?: () => void }) {
  return (
    <div className="banner banner-error" role="alert">
      <div>
        <strong>{failure.status === null ? 'Сеть' : `HTTP ${failure.status}`}</strong> {failure.message}
        {failure.traceId && <div className="trace">traceId: {failure.traceId}</div>}
      </div>

      {onRetry && (
        <button type="button" className="btn btn-ghost btn-small" onClick={onRetry}>
          Повторить
        </button>
      )}
    </div>
  )
}
