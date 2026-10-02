import { Link, Outlet } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import { apiDocsPath } from '../lib/env'

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
          {/* Ссылка на документацию API появляется только там, где Api её действительно отдаёт
              (Development). В проде её нет — иначе вёл бы на 404 чужого хоста: см. lib/env.ts. */}
          {apiDocsPath && (
            <a href={apiDocsPath} target="_blank" rel="noreferrer">
              API
            </a>
          )}
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
