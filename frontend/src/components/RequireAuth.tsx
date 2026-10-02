import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

/**
 * Защита маршрутов на клиенте. Это удобство, а не безопасность: настоящая проверка —
 * на сервере (`RequireAuthorization` + владелец из claim sub), он же отдаёт 401/403.
 */
export function RequireAuth() {
  const { isAuthenticated } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    // Куда пользователь шёл — чтобы вернуть его туда после входа.
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  return <Outlet />
}
