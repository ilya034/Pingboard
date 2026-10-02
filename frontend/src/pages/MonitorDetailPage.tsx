import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { toApiFailure } from '../api/client'
import { FailureBanner } from '../components/FailureBanner'
import { Sparkline } from '../components/Sparkline'
import { StatusBadge } from '../components/StatusBadge'
import { UptimeBar } from '../components/UptimeBar'
import { useChecks } from '../hooks/useChecks'
import { useMonitor, useUpdateMonitor } from '../hooks/useMonitors'
import {
  formatDateTime,
  formatInterval,
  formatLatency,
  formatRelative,
  formatUptime,
  formatWindow,
  latencyStats,
} from '../lib/format'

const WINDOWS = [1, 6, 24]
/** Сколько строк истории рисуем: лимит Api — 500, но таблица на 500 строк не читается. */
const TABLE_LIMIT = 100

export function MonitorDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const [hours, setHours] = useState(24)
  const [actionError, setActionError] = useState<string | null>(null)

  const monitor = useMonitor(id)
  const checks = useChecks(id, hours)
  const updateMonitor = useUpdateMonitor()

  if (monitor.isPending) return <p className="muted">Загружаем монитор…</p>

  if (monitor.isError) {
    return (
      <div className="stack">
        <Link to="/" className="back-link">
          ← К дашборду
        </Link>
        <FailureBanner failure={toApiFailure(monitor.error)} onRetry={() => void monitor.refetch()} />
      </div>
    )
  }

  if (!monitor.data) {
    return (
      <div className="stack">
        <Link to="/" className="back-link">
          ← К дашборду
        </Link>
        <p className="muted">Монитор не найден.</p>
      </div>
    )
  }

  const data = monitor.data
  const items = checks.data?.items ?? []
  const failed = items.filter((item) => !item.ok).length
  const windowUptime = items.length > 0 ? (items.length - failed) / items.length : null
  const stats = latencyStats(items)
  const rows = [...items]
    .sort((left, right) => new Date(right.checkedAt).getTime() - new Date(left.checkedAt).getTime())
    .slice(0, TABLE_LIMIT)

  function toggleEnabled() {
    setActionError(null)
    updateMonitor.mutate(
      { id: data.id, patch: { enabled: !data.enabled } },
      { onError: (error: unknown) => setActionError(toApiFailure(error).message) },
    )
  }

  return (
    <div className="stack">
      <Link to="/" className="back-link">
        ← К дашборду
      </Link>

      <div className="page-head">
        <div>
          <h1>
            {data.name} <StatusBadge monitor={data} />
          </h1>
          <p className="muted">
            <a href={data.url} target="_blank" rel="noreferrer">
              {data.url}
            </a>{' '}
            · интервал {formatInterval(data.intervalSeconds)} · проверен {formatRelative(data.lastCheckedAt)}
          </p>
        </div>

        <button type="button" className="btn btn-ghost" onClick={toggleEnabled} disabled={updateMonitor.isPending}>
          {data.enabled ? 'Поставить на паузу' : 'Включить проверки'}
        </button>
      </div>

      {actionError && (
        <div className="banner banner-error" role="alert">
          <div>{actionError}</div>
        </div>
      )}

      <section className="cards">
        <div className="card stat">
          <span className="stat-label">Uptime за 24 ч</span>
          <span className="stat-value">{formatUptime(data.uptime24h)}</span>
        </div>
        <div className="card stat">
          <span className="stat-label">Uptime за {formatWindow(hours)}</span>
          <span className="stat-value">{formatUptime(windowUptime)}</span>
          <span className="muted small">
            {items.length} проверок, сбоев {failed}
          </span>
        </div>
        <div className="card stat">
          <span className="stat-label">Задержка (средняя)</span>
          <span className="stat-value">{formatLatency(stats.average)}</span>
          <span className="muted small">
            мин {formatLatency(stats.min)} · макс {formatLatency(stats.max)}
          </span>
        </div>
        <div className="card stat">
          <span className="stat-label">Последняя задержка</span>
          <span className="stat-value">{formatLatency(data.lastLatencyMs)}</span>
          <span className="muted small">создан {formatDateTime(data.createdAt)}</span>
        </div>
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Полоса доступности (24 ч)</h2>
          <span className="muted small">сегмент — час окна, состояние считает Api</span>
        </div>
        <UptimeBar buckets={data.uptimeBar} />
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Задержки</h2>
          <div className="segmented">
            {WINDOWS.map((option) => (
              <button
                key={option}
                type="button"
                className={option === hours ? 'seg-btn seg-btn-active' : 'seg-btn'}
                onClick={() => setHours(option)}
              >
                {option} ч
              </button>
            ))}
          </div>
        </div>

        {checks.isPending && <p className="muted">Загружаем историю…</p>}
        {checks.isError && (
          <FailureBanner failure={toApiFailure(checks.error)} onRetry={() => void checks.refetch()} />
        )}
        {checks.isSuccess && <Sparkline items={items} />}
      </section>

      <section className="card">
        <div className="card-head">
          <h2>История проверок</h2>
          <span className="muted small">
            {formatWindow(hours)}
            {checks.data?.hasMore ? ` · показаны последние ${TABLE_LIMIT} (в окне есть более ранние)` : ''}
          </span>
        </div>

        {checks.isSuccess && rows.length === 0 && (
          <p className="muted">
            Проверок в окне нет: либо воркер ещё не дошёл до монитора, либо он на паузе.
          </p>
        )}

        {rows.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Время</th>
                  <th>Результат</th>
                  <th>Код</th>
                  <th>Задержка</th>
                  <th>Ошибка</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((check) => (
                  <tr key={check.id}>
                    <td className="nowrap">{formatDateTime(check.checkedAt)}</td>
                    <td>
                      <span className={check.ok ? 'text-up' : 'text-down'}>{check.ok ? 'успех' : 'сбой'}</span>
                    </td>
                    <td className="num">{check.statusCode ?? '—'}</td>
                    <td className="num">{formatLatency(check.latencyMs)}</td>
                    <td className="error-cell" title={check.error ?? undefined}>
                      {check.error ?? ''}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
