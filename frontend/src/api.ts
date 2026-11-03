import type { Order, Page, Product, TokenResponse, TopCustomer } from './types'

const baseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5000'
const accessKey = 'store.access'
const refreshKey = 'store.refresh'
let refreshInFlight: Promise<boolean> | undefined

export class ApiError extends Error {
  public status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
    this.name = 'ApiError'
  }
}

export function saveTokens(tokens: TokenResponse) {
  localStorage.setItem(accessKey, tokens.accessToken)
  localStorage.setItem(refreshKey, tokens.refreshToken)
}

export function clearTokens() {
  localStorage.removeItem(accessKey)
  localStorage.removeItem(refreshKey)
}

export function getRole(): 'Admin' | 'Customer' | null {
  const token = localStorage.getItem(accessKey)
  if (!token) return null
  try {
    const payload = JSON.parse(atob(token.split('.')[1])) as Record<string, unknown>
    const role = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ?? payload.role
    return role === 'Admin' || role === 'Customer' ? role : null
  } catch {
    return null
  }
}

async function refreshAccessToken(): Promise<boolean> {
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      const refreshToken = localStorage.getItem(refreshKey)
      if (!refreshToken) return false
      try {
        const response = await fetch(`${baseUrl}/api/auth/refresh`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken }),
        })
        if (!response.ok) return false
        saveTokens(await response.json() as TokenResponse)
        return true
      } catch {
        return false
      }
    })()
  }
  try {
    return await refreshInFlight
  } finally {
    refreshInFlight = undefined
  }
}

export async function api<T>(path: string, init: RequestInit = {}, retried = false): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const token = localStorage.getItem(accessKey)
  if (token) headers.set('Authorization', `Bearer ${token}`)

  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, { ...init, headers })
  } catch {
    throw new ApiError(0, 'Unable to reach the store API. Check your connection and try again.')
  }
  if (response.status === 401 && !retried && await refreshAccessToken()) {
    return api<T>(path, init, true)
  }
  if (response.status === 401 && retried) clearTokens()
  if (!response.ok) {
    let message = response.statusText || 'Request failed.'
    try {
      const problem = await response.json() as { detail?: string; title?: string }
      message = problem.detail ?? problem.title ?? message
    } catch { /* Keep the HTTP status text when no JSON problem body exists. */ }
    if (response.status === 409) message = `Stock or order conflict: ${message}`
    if (response.status === 429) message = 'Too many login attempts. Please wait a minute and try again.'
    throw new ApiError(response.status, message)
  }
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

export const authApi = {
  login: (email: string, password: string) =>
    api<TokenResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (fullName: string, email: string, password: string) =>
    api<TokenResponse>('/api/auth/register', { method: 'POST', body: JSON.stringify({ fullName, email, password }) }),
}

export const storeApi = {
  products: (query: string) => api<Page<Product>>(`/api/products${query}`),
  product: (id: number) => api<Product>(`/api/products/${id}`),
  saveProduct: (product: Partial<Product> & Pick<Product, 'sku' | 'name' | 'price' | 'stockQuantity'>) =>
    product.id
      ? api<Product>(`/api/products/${product.id}`, { method: 'PUT', body: JSON.stringify(product) })
      : api<Product>('/api/products', { method: 'POST', body: JSON.stringify(product) }),
  placeOrder: (items: { productId: number; quantity: number }[], key: string) =>
    api<Order>('/api/orders', { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify({ items }) }),
  myOrders: () => api<Page<Order>>('/api/orders/my'),
  allOrders: () => api<Page<Order>>('/api/orders'),
  setStatus: (id: number, status: string) =>
    api<Order>(`/api/orders/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status }) }),
  topCustomers: () => api<TopCustomer[]>('/api/reports/top-customers?days=30'),
}
