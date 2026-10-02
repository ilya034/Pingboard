import { useState } from 'react'
import { Link } from 'react-router-dom'
import { toApiFailure, type ApiFailure } from '../api/client'
import type { MonitorDto, MonitorFormValues } from '../api/types'
import { FailureBanner } from '../components/FailureBanner'
import { MonitorForm } from '../components/MonitorForm'
import { StatusBadge } from '../components/StatusBadge'
import { UptimeBar } from '../components/UptimeBar'
import {
  useCreateMonitor,
  useDeleteMonitor,
  useMonitors,
  useUpdateMonitor,
} from '../hooks/useMonitors'
import { formatInterval, formatLatency, formatRelative, formatUptime, monitorState } from '../lib/format'

/**
 * Дашборд: таблица мониторов с полосой доступности за 24 часа. Данные поллит TanStack Query
 * (refetchInterval 10 с), поэтому «живость» не требует ни WebSocket, ни ручного обновления.
 */
export function DashboardPage() {
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<MonitorDto | null>(null)
  const [failure, setFailure] = useState<ApiFailure | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const monitors = useMonitors()
  const createMonitor = useCreateMonitor()
  const updateMonitor = useUpdateMonitor()
  const deleteMonitor = useDeleteMonitor()

  const list = monitors.data ?? []
  const submitting = createMonitor.isPending || updateMonitor.isPending

  const counts = list.reduce(
    (accumulator, monitor) => {
      accumulator[monitorState(monitor)] += 1
      return accumulator
    },
    { up: 0, down: 0, paused: 0, unknown: 0 },
  )

  function openCreate() {
    setEditing(null)
    setFailure(null)
    setFormOpen(true)
  }

  function openEdit(monitor: MonitorDto) {
    setEditing(monitor)
    setFailure(null)
    setFormOpen(true)
  }

  function closeForm() {
    setFormOpen(false)
    setEditing(null)
    setFailure(null)
  }

  function submit(values: MonitorFormValues) {
    setFailure(null)

    const options = {
      onSuccess: closeForm,
      onError: (error: unknown) => setFailure(toApiFailure(error)),
    }

    if (editing) updateMonitor.mutate({ id: editing.id, patch: values }, options)
    else createMonitor.mutate(values, options)
  }

  function toggleEnabled(monitor: MonitorDto) {
    setActionError(null)
    updateMonitor.mutate(
      { id: monitor.id, patch: { enabled: !monitor.enabled } },
      { onError: (error: unknown) => setActionError(toApiFailure(error).message) },
    )
  }

  function remove(monitor: MonitorDto) {
    setActionError(null)

    const confirmed = window.confirm(`Удалить монитор «${monitor.name}» вместе с историей проверок?`)
    if (!confirmed) return

    deleteMonitor.mutate(monitor.id, {
      onError: (error: unknown) => setActionError(toApiFailure(error).message),
    })
  }

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1>Мониторы</h1>
          <p className="muted">
            Всего {list.length} · в сети {counts.up} · недоступно {counts.down} · на паузе {counts.paused}
            {counts.unknown > 0 ? ` · без данных ${counts.unknown}` : ''}
          </p>
        </div>

        <button type="button" className="btn btn-primary" onClick={openCreate}>
          Добавить монитор
        </button>
      </div>

      {actionError && (
        <div className="banner banner-error" role="alert">
          <div>{actionError}</div>
        </div>
      )}

      {formOpen && (
        <MonitorForm
          // key заставляет форму перечитать initial при переходе «создание ↔ редактирование».
          key={editing?.id ?? 'new'}
          initial={editing ?? undefined}
          submitting={submitting}
          failure={failure}
          onSubmit={submit}
          onCancel={closeForm}
        />
      )}

      <section className="card">
        <div className="card-head">
          <h2>Дашборд</h2>
          <span className="muted small">
            {monitors.dataUpdatedAt > 0
              ? `обновлено ${new Date(monitors.dataUpdatedAt).toLocaleTimeString('ru-RU')}`
              : 'загрузка'}{' '}
            · автоопрос каждые 10 с
          </span>
        </div>

        {monitors.isPending && <p className="muted">Загружаем мониторы…</p>}

        {monitors.isError && (
          <FailureBanner failure={toApiFailure(monitors.error)} onRetry={() => void monitors.refetch()} />
        )}

        {monitors.isSuccess && list.length === 0 && (
          <p className="muted">
            Пока ни одного монитора. Нажмите «Добавить монитор» — воркер начнёт проверки в течение
            своего тика и заполнит историю.
          </p>
        )}

        {list.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Статус</th>
                  <th>Имя</th>
                  <th>URL</th>
                  <th>Uptime 24 ч</th>
                  <th>Полоса (24 ч)</th>
                  <th>Задержка</th>
                  <th>Проверен</th>
                  <th>Действия</th>
                </tr>
              </thead>
              <tbody>
                {list.map((monitor) => (
                  <tr key={monitor.id}>
                    <td>
                      <StatusBadge monitor={monitor} />
                    </td>
                    <td>
                      <Link to={`/monitors/${monitor.id}`} className="link-strong">
                        {monitor.name}
                      </Link>
                      <div className="muted small">каждые {formatInterval(monitor.intervalSeconds)}</div>
                    </td>
                    <td className="url-cell" title={monitor.url}>
                      {monitor.url}
                    </td>
                    <td className="num">{formatUptime(monitor.uptime24h)}</td>
                    <td>
                      <UptimeBar buckets={monitor.uptimeBar} />
                    </td>
                    <td className="num">{formatLatency(monitor.lastLatencyMs)}</td>
                    <td className="nowrap">{formatRelative(monitor.lastCheckedAt)}</td>
                    <td className="nowrap">
                      <div className="row-actions">
                        <Link className="btn btn-ghost btn-small" to={`/monitors/${monitor.id}`}>
                          История
                        </Link>
                        <button type="button" className="btn btn-ghost btn-small" onClick={() => openEdit(monitor)}>
                          Изменить
                        </button>
                        <button type="button" className="btn btn-ghost btn-small" onClick={() => toggleEnabled(monitor)}>
                          {monitor.enabled ? 'Пауза' : 'Включить'}
                        </button>
                        <button type="button" className="btn btn-danger btn-small" onClick={() => remove(monitor)}>
                          Удалить
                        </button>
                      </div>
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
