import { useQuery } from '@tanstack/react-query'
import { fetchChecks } from '../api/monitors'

/** История проверок монитора за окно в часах; обновляется тем же поллингом, что и дашборд. */
export function useChecks(id: string, hours: number) {
  return useQuery({
    queryKey: ['checks', id, hours],
    queryFn: () => fetchChecks(id, hours),
    refetchInterval: 10_000,
    enabled: id.length > 0,
  })
}
