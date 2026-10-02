import { api } from './client'
import type {
  CheckPageDto,
  CreateMonitorRequest,
  MonitorDto,
  UpdateMonitorRequest,
} from './types'

export async function fetchMonitors(): Promise<MonitorDto[]> {
  const { data } = await api.get<MonitorDto[]>('/monitors')
  return data
}

export async function fetchMonitor(id: string): Promise<MonitorDto> {
  const { data } = await api.get<MonitorDto>(`/monitors/${id}`)
  return data
}

export async function createMonitor(request: CreateMonitorRequest): Promise<MonitorDto> {
  const { data } = await api.post<MonitorDto>('/monitors', request)
  return data
}

export async function updateMonitor(id: string, patch: UpdateMonitorRequest): Promise<MonitorDto> {
  const { data } = await api.patch<MonitorDto>(`/monitors/${id}`, patch)
  return data
}

export async function deleteMonitor(id: string): Promise<void> {
  await api.delete(`/monitors/${id}`)
}

/**
 * История проверок за последние `hours` часов. Окно считается в момент запроса, а не в
 * момент открытия страницы: при поллинге каждые 10 с это сдвигает окно вместе с временем.
 */
export async function fetchChecks(id: string, hours: number, limit = 500): Promise<CheckPageDto> {
  const to = new Date()
  const from = new Date(to.getTime() - hours * 3_600_000)

  const { data } = await api.get<CheckPageDto>(`/monitors/${id}/checks`, {
    params: { from: from.toISOString(), to: to.toISOString(), limit },
  })

  return data
}
