import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { toApiFailure } from '../api/client'
import { manualRefetch } from '../api/query'
import type { MonitorDto } from '../api/types'
import { FailureBanner } from '../components/FailureBanner'
import { Sparkline } from '../components/Sparkline'
import { StatusBadge } from '../components/StatusBadge'
import { UptimeBar } from '../components/UptimeBar'
import { useChecks } from '../hooks/useChecks'
import { useMonitor, useUpdateMonitor } from '../hooks/useMonitors'
import {
  checkWindowSpan,
  formatDateTime,
  formatInterval,
  formatLatency,
  formatRelative,
  formatUptime,
  formatWindow,
  formatWindowSpan,
  latencyStats,
} from '../lib/format'

const WINDOWS = [1, 6, 24]
/** Сколько строк истории рисуем: лимит Api — 500, но таблица на 500 строк не читается. */
const TABLE_LIMIT = 100

export function MonitorDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const monitor = useMonitor(id)

  // Все хуки объявлены до ранних возвратов: если объявить `useState` окна ниже, при
  // первом рендере из кэша порядок хуков станет условным — правило хуков React нарушено,
  // а отладка такого падения («Rendered more hooks than during the previous render»)
  // стоит дорого. Состояние окна живёт здесь, отрисовка — в MonitorDetail.
  const [hours, setHours] = useState(24)
  const checks = useChecks(id, hours)

  if (monitor.isPending) return <p className="muted">Загружаем монитор…</p>

  if (monitor.isError) {
    return (
      <div className="stack">
        <Link to="/" className="back-link">
          ← К дашборду
        </Link>
        <FailureBanner
          failure={toApiFailure(monitor.error)}
          onRetry={() => void manualRefetch(() => monitor.refetch())}
        />
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

  return (
    <MonitorDetail
      monitor={monitor.data}
      hours={hours}
      onHoursChange={setHours}
      checks={checks}
    />
  )
}

/**
 * Экран загруженного монитора. Отдельный компонент, а не продолжение `MonitorDetailPage`:
 * часть крючков (`useUpdateMonitor`) нужна только этой ветке, а объявлять их надо
 * безусловно — то есть до ранних возвратов. Вынос сохраняет и то, и другое.
 */
function MonitorDetail({
  monitor,
  hours,
  onHoursChange,
  checks,
}: {
  monitor: MonitorDto
  hours: number
  onHoursChange: (hours: number) => void
  checks: ReturnType<typeof useChecks>
}) {
  const [actionError, setActionError] = useState<string | null>(null)
  const updateMonitor = useUpdateMonitor()

  const items = checks.data?.items ?? []
  const failed = items.filter((item) => !item.ok).length
  const windowUptime = items.length > 0 ? (items.length - failed) / items.length : null
  const stats = latencyStats(items)
  const span = checkWindowSpan(items)
  const spanLabel = span ? `покрыто ${formatWindowSpan(span.from, span.to)}` : null

  const rows = [...items]
    .sort((left, right) => new Date(right.checkedAt).getTime() - new Date(left.checkedAt).getTime())
    .slice(0, TABLE_LIMIT)

  function toggleEnabled() {
    setActionError(null)
    updateMonitor.mutate(
      { id: monitor.id, patch: { enabled: !monitor.enabled } },
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
            {monitor.name} <StatusBadge monitor={monitor} />
          </h1>
          <p className="muted">
            <a href={monitor.url} target="_blank" rel="noreferrer">
              {monitor.url}
            </a>{' '}
            · интервал {formatInterval(monitor.intervalSeconds)} · проверен{' '}
            {formatRelative(monitor.lastCheckedAt)}
          </p>
        </div>

        <button
          type="button"
          className="btn btn-ghost"
          onClick={toggleEnabled}
          disabled={updateMonitor.isPending}
        >
          {updateMonitor.isPending ? 'Сохраняем…' : monitor.enabled ? 'Поставить на паузу' : 'Включить проверки'}
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
          {/* Число серверное — тот же агрегат, что в uptimeBar. Пересчитывать его по
              загруженной истории нельзя: Api обрезает её лимитом, и две карточки на одном
              экране показывали бы разный «uptime за 24 ч». */}
          <span className="stat-value">{formatUptime(monitor.uptime24h)}</span>
          <span className="muted small">считает Api по полной истории</span>
        </div>

        <div className="card stat">
          <span className="stat-label">Uptime за {formatWindow(hours)}</span>
          <span className="stat-value">{formatUptime(windowUptime)}</span>
          {/* Здесь пересчёт по загруженным проверкам уместен, но подпись обязана называть
              то, что реально покрыто данными, а не то, что запрошено окном. */}
          <span className="muted small">
            {items.length} проверок, сбоев {failed}
            {spanLabel ? ` · ${spanLabel}` : ''}
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
          <span className="stat-value">{formatLatency(monitor.lastLatencyMs)}</span>
          <span className="muted small">создан {formatDateTime(monitor.createdAt)}</span>
        </div>
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Полоса доступности (24 ч)</h2>
          <span className="muted small">сегмент — час окна, состояние считает Api</span>
        </div>
        <UptimeBar buckets={monitor.uptimeBar} />
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
                onClick={() => onHoursChange(option)}
              >
                {option} ч
              </button>
            ))}
          </div>
        </div>

        {checks.isPending && <p className="muted">Загружаем историю…</p>}
        {checks.isError && (
          <FailureBanner
            failure={toApiFailure(checks.error)}
            onRetry={() => void manualRefetch(() => checks.refetch())}
          />
        )}
        {checks.isSuccess && <Sparkline items={items} spanLabel={spanLabel ?? undefined} />}
      </section>

      <section className="card">
        <div className="card-head">
          <h2>История проверок</h2>
          <span className="muted small">
            {formatWindow(hours)}
            {spanLabel ? ` · ${spanLabel}` : ''}
            {checks.data?.hasMore
              ? ` · показаны последние ${TABLE_LIMIT} (Api отдал ${items.length}, в окне есть более ранние)`
              : ''}
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
