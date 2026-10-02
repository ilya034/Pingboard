import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { isRetryable } from './api/client'
import { AuthProvider } from './hooks/useAuth'
import './styles.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Повторяем только сеть/5xx: 400 и 401 повторять бессмысленно, а 429 — вредно.
      retry: (failureCount, error) => isRetryable(error) && failureCount < 2,
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
