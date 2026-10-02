import { Navigate, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { RequireAuth } from './components/RequireAuth'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { MonitorDetailPage } from './pages/MonitorDetailPage'

/**
 * Маршруты: логин снаружи рамки, остальное — внутри RequireAuth + Layout.
 * Неизвестный путь уводим на дашборд: SPA отдаётся nginx-ом из index.html, и
 * «страницы 404» на стороне клиента здесь просто нет.
 */
export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/monitors/:id" element={<MonitorDetailPage />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
