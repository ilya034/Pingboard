import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { retryQuery } from './api/query'
import { AuthProvider } from './hooks/useAuth'
import './styles.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Политика повторов — в query.ts: там же видно, что ручная попытка с кнопки
      // «Повторить» не повторяется автоматически.
      retry: retryQuery,
      staleTime: 5_000,
      refetchOnWindowFocus: true,
    },
  },
})

const rootElement = document.getElementById('root')
if (!rootElement) throw new Error('В index.html нет элемента #root')

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      {/* AuthProvider внутри QueryClientProvider: выход чистит кэш запросов. */}
      <AuthProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
