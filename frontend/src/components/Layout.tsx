import { Link, Outlet } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

/** Общая рамка приложения: шапка с пользователем, содержимое страницы, подвал. */
export function Layout() {
  const { email, signOut } = useAuth()

  return (
    <div className="app">
      <header className="app-header">
        <Link to="/" className="brand">
          Pingboard
          <small>uptime-монитор</small>
        </Link>

        <nav className="app-nav">
          <Link to="/">Дашборд</Link>
          {/* Swagger/Scalar живёт на Api (только Development) — во время отладки это ближе всего. */}
          <a href="http://localhost:8080/scalar/v1" target="_blank" rel="noreferrer">
            API
          </a>
        </nav>

        <div className="app-header-right">
          {email && <span className="user-email" title={email}>{email}</span>}
          <button type="button" className="btn btn-ghost btn-small" onClick={signOut}>
            Выйти
          </button>
        </div>
      </header>

      <main className="app-main">
        <Outlet />
      </main>

      <footer className="app-footer">
        Опрос API каждые 10 секунд · Pingboard · SRE-курс
      </footer>
    </div>
  )
}
