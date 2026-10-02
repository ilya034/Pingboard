import type { UptimeBucketDto } from '../api/types'

/**
 * Полоса доступности: сегмент на интервал окна (по умолчанию 24 сегмента-часа).
 * Состояние считает сервер (UptimeBucketDto.State), поэтому клиент ничего не агрегирует —
 * полоса остаётся верной даже когда история на дашборде обрезана лимитом.
 *
 * Полоса — SVG, а не набор `<span>`: состояние каждого сегмента должно читаться не только
 * глазами (цвет) и не только мышью (`title` не доступен с клавиатуры и не читается
 * скринридером). `aria-label` описывает окно целиком, `<title>` у сегмента — деталь.
 */
export function UptimeBar({ buckets }: { buckets: UptimeBucketDto[] }) {
  if (buckets.length === 0) return <span className="muted">нет данных</span>

  const down = buckets.filter((bucket) => bucket.state === 'down').length
  const unknown = buckets.filter((bucket) => bucket.state === 'unknown').length

  return (
    <svg
      className="uptime-bar"
      viewBox={`0 0 ${buckets.length * 10} 22`}
      preserveAspectRatio="none"
      role="img"
      aria-label={`Полоса доступности: сегментов ${buckets.length}, сбоев ${down}, без данных ${unknown}`}
    >
      {buckets.map((bucket, index) => (
        <rect
          key={bucket.from}
          className={`seg seg-${bucket.state}`}
          x={index * 10}
          y={0}
          width={8}
          height={22}
          rx={2}
        >
          <title>{describe(bucket)}</title>
        </rect>
      ))}
    </svg>
  )
}

function describe(bucket: UptimeBucketDto): string {
  const from = new Date(bucket.from).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
  const to = new Date(bucket.to).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })

  if (bucket.total === 0) return `${from}–${to}: проверок не было`
  return `${from}–${to}: проверок ${bucket.total}, сбоев ${bucket.failed}`
}
