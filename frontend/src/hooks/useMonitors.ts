import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  createMonitor,
  deleteMonitor,
  fetchMonitor,
  fetchMonitors,
  updateMonitor,
} from '../api/monitors'
import { patchMonitorInList, restoreMonitorInList } from '../api/query'
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
    // Здесь инвалидация уместна: сервер сам считает uptime, полосу и время создания,
    // и предугадать их на клиенте нечем. Оптимистичная запись была бы выдумкой.
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitors'] })
    },
  })
}

export function useUpdateMonitor() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateMonitorRequest }) => updateMonitor(id, patch),
    // Патч тех же полей, что и в MonitorDto (name/url/intervalSeconds/enabled), поэтому
    // он подходит для оптимистичной записи без приведения типов.
    onMutate: ({ id, patch }) => ({
      previous: patchMonitorInList(queryClient, id, patch),
    }),
    onError: (_error, { id }, context) => {
      restoreMonitorInList(queryClient, id, context?.previous ?? null)
    },
    onSettled: (_data, _error, { id }) => {
      // Фоновая сверка с сервером: оптимистичный патч мог разойтись с тем, что записал Api
      // (например, нормализовал URL). `invalidateQueries` не ждём — UI уже обновлён, а
      // refetchInterval всё равно приведёт данные в порядок.
      void queryClient.invalidateQueries({ queryKey: ['monitors'], exact: true })
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
      queryClient.removeQueries({ queryKey: ['monitor', id] })
      queryClient.removeQueries({ queryKey: ['checks', id] })
      void queryClient.invalidateQueries({ queryKey: ['monitors'], exact: true })
    },
  })
}
