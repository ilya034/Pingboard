import { api } from './client'
import type { AuthTokenDto } from './types'

export async function login(email: string, password: string): Promise<AuthTokenDto> {
  const { data } = await api.post<AuthTokenDto>('/auth/login', { email, password })
  return data
}

export async function register(email: string, password: string): Promise<AuthTokenDto> {
  const { data } = await api.post<AuthTokenDto>('/auth/register', { email, password })
  return data
}
