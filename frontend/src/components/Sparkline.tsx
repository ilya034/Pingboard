import type { MonitorCheckDto } from '../api/types'

const WIDTH = 720
const HEIGHT = 140
const PAD = 14

/**
 * Спарклайн задержек на своём SVG (~40 строк вместо recharts, PLAN.md §7).
 * Точки идут по индексу проверки, а не по времени: при равном интервале это то же самое,
 * зато пропуски в истории не сжимают график в непонятную кашу.
 *
 * `spanLabel` — фактическое покрытие окна. Api обрезает историю лимитом, поэтому «в окне»
 * на графике и «в окне» в запросе могут быть разными интервалами: подпись обязана называть
 * то, что нарисовано.
 */
export function Sparkline({ items, spanLabel }: { items: MonitorCheckDto[]; spanLabel?: string | undefined }) {
  const points = [...items].sort(
    (left, right) => new Date(left.checkedAt).getTime() - new Date(right.checkedAt).getTime(),
  )

  const latencies = points
    .map((point) => point.latencyMs)
    .filter((value): value is number => value !== null)

  if (latencies.length < 2) {
    return <p className="muted">Мало данных для графика: нужно минимум две проверки с задержкой в окне.</p>
  }

  const max = Math.max(...latencies)
  const step = points.length > 1 ? (WIDTH - 2 * PAD) / (points.length - 1) : 0
  const x = (index: number) => PAD + index * step
  const y = (value: number) => HEIGHT - PAD - (value / (max || 1)) * (HEIGHT - 2 * PAD)

  // Линия рвётся на сбоях: у неуспешной проверки задержки нет, и соединять её с соседями —
  // рисовать проверку, которой не было.
  const segments: string[] = []
  let current: string[] = []

  points.forEach((point, index) => {
    if (point.latencyMs === null) {
      if (current.length > 0) segments.push(current.join(' '))
      current = []
      return
    }

    current.push(`${x(index).toFixed(1)},${y(point.latencyMs).toFixed(1)}`)
  })

  if (current.length > 0) segments.push(current.join(' '))

  const failures = points
    .map((point, index) => ({ point, index }))
    .filter(({ point }) => !point.ok)

  return (
    <figure className="spark">
      <svg
        className="spark-svg"
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        role="img"
        aria-label={
          `Задержка проверок: ${latencies.length} проверок с ответом, сбоев ${failures.length}` +
          (spanLabel ? `, ${spanLabel}` : '')
        }
      >
        {segments
          .filter((segment) => segment.includes(' '))
          .map((segment) => (
            <polyline key={segment} className="spark-line" points={segment} />
          ))}

        {/* Сбой — вертикальная засечка у нижней границы: у неё нет своей задержки. */}
        {failures.map(({ point, index }) => (
          <line
            key={point.id}
            className="spark-fail"
            x1={x(index)}
            x2={x(index)}
            y1={HEIGHT - 1}
            y2={HEIGHT - 12}
          />
        ))}
      </svg>

      <figcaption>
        Задержка, мс · максимум {max} · сбоев {failures.length} · проверок с ответом {latencies.length}
        {spanLabel ? ` · ${spanLabel}` : ''}
      </figcaption>
    </figure>
  )
}
