// Типы ответов API — зеркало DTO из Pingboard.Application (backend/src/Pingboard.Application/*/Dtos).
// Сериализация минимальных API — camelCase, поэтому поля здесь в нижнем регистре с большой буквы.

export interface AuthTokenDto {
  accessToken: string
  expiresAt: string
}

export interface UptimeBucketDto {
  from: string
  to: string
  total: number
  failed: number
  /** unknown — проверок в сегменте не было; иначе up/down. Считает сервер (UptimeBucketDto.State). */
  state: 'up' | 'down' | 'unknown'
}

export interface MonitorDto {
  id: string
  name: string
  url: string
  intervalSeconds: number
  enabled: boolean
  lastOk: boolean | null
  lastCheckedAt: string | null
  createdAt: string
  updatedAt: string
  uptime24h: number | null
  lastLatencyMs: number | null
  uptimeBar: UptimeBucketDto[]
}

export interface MonitorCheckDto {
  id: number
  checkedAt: string
  ok: boolean
  statusCode: number | null
  latencyMs: number | null
  error: string | null
}

export interface CheckPageDto {
  monitorId: string
  from: string
  to: string
  /** true — в окне есть ещё проверки за пределами items (их обрезал limit). */
  hasMore: boolean
  items: MonitorCheckDto[]
}

/** Тело запроса создания/обновления монитора (PATCH — любое подмножество). */
export interface MonitorFormValues {
  name: string
  url: string
  intervalSeconds: number
  enabled: boolean
}

export type CreateMonitorRequest = MonitorFormValues
export type UpdateMonitorRequest = Partial<MonitorFormValues>

/** RFC 7807: так Api отдаёт все ошибки, включая разбивку по полям в errors. */
export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  instance?: string
  traceId?: string
  errors?: Record<string, string[]>
}
