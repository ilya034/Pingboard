import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { login, register } from '../api/auth'
import {
  readStoredAuth,
  setAuthToken,
  setUnauthorizedHandler,
  writeStoredAuth,
  type StoredAuth,
} from '../api/client'

interface AuthContextValue {
  email: string | null
  isAuthenticated: boolean
  signIn: (email: string, password: string) => Promise<void>
  signUp: (email: string, password: string) => Promise<void>
  signOut: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

/**
 * Вход/выход и токен. Токен лежит в localStorage (переживает F5), а сервер сессий не держит:
 * JWT — это и есть фактор VI «stateless-процессы» со стороны клиента.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<StoredAuth | null>(() => readStoredAuth())
  const queryClient = useQueryClient()

  const signOut = useCallback(() => {
    setAuthToken(null)
    writeStoredAuth(null)
    setAuth(null)
    // Иначе после выхода на экране успел бы мелькнуть кэш мониторов прошлого пользователя.
    queryClient.clear()
  }, [queryClient])

  useEffect(() => {
    setUnauthorizedHandler(signOut)
    return () => setUnauthorizedHandler(null)
  }, [signOut])

  const authenticate = useCallback(
    async (email: string, password: string, mode: 'login' | 'register') => {
      const token = mode === 'login' ? await login(email, password) : await register(email, password)
      const stored: StoredAuth = { accessToken: token.accessToken, email, expiresAt: token.expiresAt }

      // Синхронно, до setState: запросы дашборда стартуют раньше, чем отработают эффекты React,
      // и без этого первый вызов ушёл бы без заголовка Authorization.
      setAuthToken(stored.accessToken)
      writeStoredAuth(stored)
      setAuth(stored)
    },
    [],
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      email: auth?.email ?? null,
      isAuthenticated: auth !== null,
      signIn: (email, password) => authenticate(email, password, 'login'),
      signUp: (email, password) => authenticate(email, password, 'register'),
      signOut,
    }),
    [authenticate, auth, signOut],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth вызван вне AuthProvider')
  return context
}
