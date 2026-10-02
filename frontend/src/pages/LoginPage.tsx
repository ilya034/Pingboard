import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { toApiFailure, type ApiFailure } from '../api/client'
import { FailureBanner } from '../components/FailureBanner'
import { FieldErrorText, fieldErrorId, hasFieldError } from '../components/FieldErrorText'
import { useAuth } from '../hooks/useAuth'
import { demoCredentials } from '../lib/demo'
import { apiDocsPath } from '../lib/env'

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
  // Предзаполнение — только на Development-стенде, где демо-учётка действительно создана
  // (см. lib/demo.ts). В проде поля пустые: подставлять пароль несуществующего
  // пользователя в форму входа нельзя.
  const [email, setEmail] = useState(demoCredentials?.email ?? '')
  const [password, setPassword] = useState(demoCredentials?.password ?? '')
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
              aria-invalid={hasFieldError(failure, 'email') || undefined}
              aria-describedby={hasFieldError(failure, 'email') ? fieldErrorId('email') : undefined}
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
              aria-invalid={hasFieldError(failure, 'password') || undefined}
              aria-describedby={hasFieldError(failure, 'password') ? fieldErrorId('password') : undefined}
              onChange={(event) => setPassword(event.target.value)}
            />
            <FieldErrorText failure={failure} field="password" />
            {mode === 'register' && <span className="hint">От 8 до 128 символов.</span>}
          </div>

          <button type="submit" className="btn btn-primary btn-block" disabled={pending}>
            {pending ? 'Отправляем…' : mode === 'login' ? 'Войти' : 'Зарегистрироваться'}
          </button>
        </form>

        {demoCredentials && (
          <p className="login-hint">
            Демо-учётка стенда: <code>{demoCredentials.email}</code> /{' '}
            <code>{demoCredentials.password}</code> — создаётся сидом Api в Development.
            Регистрация ограничена 10 запросами на адрес, вход — 20.
          </p>
        )}

        {/* Ссылка на документацию API: есть только там, где Api её отдаёт (Development). */}
        {apiDocsPath && (
          <p className="login-hint">
            Проверить API напрямую:{' '}
            <a href={apiDocsPath} target="_blank" rel="noreferrer">
              Scalar (только Development)
            </a>
          </p>
        )}
      </div>
    </div>
  )
}
