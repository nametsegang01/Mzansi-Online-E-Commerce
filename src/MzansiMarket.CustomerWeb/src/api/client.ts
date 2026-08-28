import type {
  Address,
  AddressInput,
  ApiProblem,
  Cart,
  Category,
  Checkout,
  CurrentUser,
  FulfilmentOrder,
  PagedProducts,
  Payment,
  RegistrationResponse,
  TokenResponse,
} from './types'

const API_URL = (import.meta.env.VITE_API_URL ?? 'https://mzansi-market-api.onrender.com').replace(/\/$/, '')
const SESSION_KEY = 'mzansi-market-session'

type Session = TokenResponse & { expiresAt: number }

export class ApiError extends Error {
  status: number
  problem: ApiProblem

  constructor(status: number, problem: ApiProblem) {
    super(
      problem.detail ??
        Object.values(problem.errors ?? {}).flat()[0] ??
        problem.title ??
        'Something went wrong. Please try again.',
    )
    this.status = status
    this.problem = problem
  }
}

function readSession(): Session | null {
  try {
    const value = sessionStorage.getItem(SESSION_KEY)
    return value ? (JSON.parse(value) as Session) : null
  } catch {
    return null
  }
}

function saveSession(tokens: TokenResponse | null) {
  if (!tokens) {
    sessionStorage.removeItem(SESSION_KEY)
    return
  }
  sessionStorage.setItem(
    SESSION_KEY,
    JSON.stringify({ ...tokens, expiresAt: Date.now() + tokens.expiresIn * 1000 }),
  )
}

async function parseProblem(response: Response): Promise<ApiProblem> {
  try {
    return (await response.json()) as ApiProblem
  } catch {
    return { title: response.statusText }
  }
}

let refreshPromise: Promise<boolean> | null = null

async function refresh(): Promise<boolean> {
  const current = readSession()
  if (!current?.refreshToken) return false
  const response = await fetch(`${API_URL}/api/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: current.refreshToken }),
  })
  if (!response.ok) {
    saveSession(null)
    return false
  }
  saveSession((await response.json()) as TokenResponse)
  return true
}

async function request<T>(path: string, init: RequestInit = {}, retry = true): Promise<T> {
  const session = readSession()
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  if (session?.accessToken) headers.set('Authorization', `Bearer ${session.accessToken}`)
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers,
    signal: init.signal ?? AbortSignal.timeout(30_000),
  })
  if (response.status === 401 && retry && session?.refreshToken) {
    refreshPromise ??= refresh().finally(() => { refreshPromise = null })
    if (await refreshPromise) return request<T>(path, init, false)
  }
  if (!response.ok) throw new ApiError(response.status, await parseProblem(response))
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  hasSession: () => Boolean(readSession()?.accessToken),
  clearSession: () => saveSession(null),
  categories: () => request<Category[]>('/api/categories'),
  products: (params: URLSearchParams) => request<PagedProducts>(`/api/products?${params}`),
  registerCustomer: (body: object) => request<RegistrationResponse>('/api/auth/register/customer', { method: 'POST', body: JSON.stringify(body) }),
  registerSeller: (body: object) => request<RegistrationResponse>('/api/auth/register/seller', { method: 'POST', body: JSON.stringify(body) }),
  async login(email: string, password: string) {
    const tokens = await request<TokenResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }, false)
    saveSession(tokens)
    return tokens
  },
  me: () => request<CurrentUser>('/api/auth/me'),
  logout: async (everywhere = false) => {
    try { await request<void>(everywhere ? '/api/auth/logout-all' : '/api/auth/logout', { method: 'POST' }) } finally { saveSession(null) }
  },
  addresses: () => request<Address[]>('/api/account/addresses'),
  addAddress: (body: AddressInput) => request<Address>('/api/account/addresses', { method: 'POST', body: JSON.stringify(body) }),
  updateAddress: (id: string, body: AddressInput) => request<Address>(`/api/account/addresses/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteAddress: (id: string) => request<void>(`/api/account/addresses/${id}`, { method: 'DELETE' }),
  cart: () => request<Cart>('/api/cart'),
  addCartItem: (productId: string, quantity = 1) => request<Cart>('/api/cart/items', { method: 'POST', body: JSON.stringify({ productId, quantity }) }),
  updateCartItem: (id: string, quantity: number) => request<Cart>(`/api/cart/items/${id}`, { method: 'PUT', body: JSON.stringify({ quantity }) }),
  removeCartItem: (id: string) => request<Cart>(`/api/cart/items/${id}`, { method: 'DELETE' }),
  checkout: (addressId: string, promotionCode?: string) => request<Checkout>('/api/checkout', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ addressId, promotionCode: promotionCode || null }) }),
  pay: (orderId: string, paymentMethodType: string) => request<Payment>(`/api/orders/${orderId}/payments/sandbox`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ paymentMethodType }) }),
  fulfilment: (status = '') => request<FulfilmentOrder[]>(`/api/fulfilment/orders${status ? `?status=${encodeURIComponent(status)}` : ''}`),
  transition: (id: string, body: { action: string; carrier?: string; trackingNumber?: string }) => request<FulfilmentOrder>(`/api/fulfilment/orders/${id}/transition`, { method: 'POST', body: JSON.stringify(body) }),
}
