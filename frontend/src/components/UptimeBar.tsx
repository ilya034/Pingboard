import type { UptimeBucketDto } from '../api/types'

/**
 * Полоса доступности: сегмент на интервал окна (по умолчанию 24 сегмента-часа).
 * Состояние считает сервер (UptimeBucketDto.State), поэтому клиент ничего не агрегирует —
 * полоса остаётся верной даже когда история на дашборде обрезана лимитом.
 */
export function UptimeBar({ buckets }: { buckets: UptimeBucketDto[] }) {
  if (buckets.length === 0) return <span className="muted">нет данных</span>

  return (
    <div className="uptime-bar">
      {buckets.map((bucket) => (
        <span key={bucket.from} className={`seg seg-${bucket.state}`} title={describe(bucket)} />
      ))}
    </div>
  )
}

function describe(bucket: UptimeBucketDto): string {
  const from = new Date(bucket.from).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
  const to = new Date(bucket.to).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })

  if (bucket.total === 0) return `${from}–${to}: проверок не было`
  return `${from}–${to}: проверок ${bucket.total}, сбоев ${bucket.failed}`
}
