import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createMonitor,
  deleteMonitor,
  fetchMonitor,
  fetchMonitors,
  updateMonitor,
} from '../api/monitors'
import type { CreateMonitorRequest, UpdateMonitorRequest } from '../api/types'

/**
 * «Живость» дашборда на MVP — поллинг: SSE/WebSocket в PLAN.md §7 вынесены в расширения.
 * 10 секунд — компромисс между свежестью статуса и нагрузкой на Api.
 */
const POLL_MS = 10_000

export function useMonitors() {
  return useQuery({
    queryKey: ['monitors'],
    queryFn: fetchMonitors,
    refetchInterval: POLL_MS,
  })
}

export function useMonitor(id: string) {
  return useQuery({
    queryKey: ['monitor', id],
    queryFn: () => fetchMonitor(id),
    refetchInterval: POLL_MS,
    enabled: id.length > 0,
  })
}

export function useCreateMonitor() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateMonitorRequest) => createMonitor(request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitors'] })
    },
  })
}

export function useUpdateMonitor() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateMonitorRequest }) => updateMonitor(id, patch),
    onSuccess: (_monitor, { id }) => {
      void queryClient.invalidateQueries({ queryKey: ['monitors'] })
      void queryClient.invalidateQueries({ queryKey: ['monitor', id] })
      void queryClient.invalidateQueries({ queryKey: ['checks', id] })
    },
  })
}

export function useDeleteMonitor() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => deleteMonitor(id),
    onSuccess: (_result, id) => {
      void queryClient.invalidateQueries({ queryKey: ['monitors'] })
      queryClient.removeQueries({ queryKey: ['monitor', id] })
      queryClient.removeQueries({ queryKey: ['checks', id] })
    },
  })
}
