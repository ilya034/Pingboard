import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { toApiFailure, type ApiFailure } from '../api/client'
import { FailureBanner } from '../components/FailureBanner'
import { FieldErrorText } from '../components/FieldErrorText'
import { useAuth } from '../hooks/useAuth'

type Mode = 'login' | 'register'

/**
 * Вход и регистрация на одном экране: различаются только маршрутом API и подсказками —
 * отдельная страница ради одного поля была бы лишней.
 */
export function LoginPage() {
  const { isAuthenticated, signIn, signUp } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [mode, setMode] = useState<Mode>('login')
  // В Development Api сеет демо-учётку (SeedOnStart): подставляем её, чтобы стенд открывался
  // в один клик. Пароль тот же, что в README и DemoUser.
  const [email, setEmail] = useState('demo@pingboard.local')
  const [password, setPassword] = useState('demo-password')
  const [failure, setFailure] = useState<ApiFailure | null>(null)
  const [pending, setPending] = useState(false)

  const from = (location.state as { from?: string } | null)?.from ?? '/'

  // Уже вошли (например, открыли /login руками с живым токеном в localStorage).
  if (isAuthenticated) return <Navigate to={from} replace />

  async function submit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setFailure(null)

    try {
      if (mode === 'login') await signIn(email.trim(), password)
      else await signUp(email.trim(), password)

      navigate(from, { replace: true })
    } catch (error) {
      setFailure(toApiFailure(error))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="login-wrap">
      <div className="login-card">
        <div className="login-brand">
          <h1>Pingboard</h1>
          <p>Uptime-монитор: статус, доступность и история задержек.</p>
        </div>

        <div className="tabs">
          <button
            type="button"
            className={mode === 'login' ? 'tab tab-active' : 'tab'}
            onClick={() => { setMode('login'); setFailure(null) }}
          >
            Вход
          </button>
          <button
            type="button"
            className={mode === 'register' ? 'tab tab-active' : 'tab'}
            onClick={() => { setMode('register'); setFailure(null) }}
          >
            Регистрация
          </button>
        </div>

        {failure && <FailureBanner failure={failure} />}

        <form onSubmit={submit} className="login-form">
          <div className="field">
            <label htmlFor="email">Email</label>
            <input
              id="email"
              type="email"
              autoComplete="username"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <FieldErrorText failure={failure} field="email" />
          </div>

          <div className="field">
            <label htmlFor="password">Пароль</label>
            <input
              id="password"
              type="password"
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <FieldErrorText failure={failure} field="password" />
            {mode === 'register' && <span className="hint">От 8 до 128 символов.</span>}
          </div>

          <button type="submit" className="btn btn-primary btn-block" disabled={pending}>
            {pending ? 'Отправляем…' : mode === 'login' ? 'Войти' : 'Зарегистрироваться'}
          </button>
        </form>

        <p className="login-hint">
          Демо-учётка стенда: <code>demo@pingboard.local</code> / <code>demo-password</code> — создаётся
          сидом Api в Development. Регистрация ограничена 10 запросами на адрес, вход — 20.
        </p>

        <p className="login-hint">
          Проверить API напрямую:{' '}
          <a href="http://localhost:8080/scalar/v1" target="_blank" rel="noreferrer">
            Scalar (только Development)
          </a>
        </p>
      </div>
    </div>
  )
}
