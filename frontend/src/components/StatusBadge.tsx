import type { MonitorDto } from '../api/types'
import { monitorState, type MonitorState } from '../lib/format'

const LABELS: Record<MonitorState, string> = {
  up: 'В сети',
  down: 'Недоступен',
  paused: 'Пауза',
  unknown: 'Нет данных',
}

export function StatusBadge({ monitor }: { monitor: Pick<MonitorDto, 'enabled' | 'lastOk'> }) {
  const state = monitorState(monitor)

  return (
    <span className={`badge badge-${state}`}>
      <i className="badge-dot" />
      {LABELS[state]}
    </span>
  )
}
