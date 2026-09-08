import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const product = {
  id: '11111111-1111-1111-1111-111111111111', sku: 'LOCAL-1', name: 'Handwoven Basket', slug: 'handwoven-basket',
  price: 420, currency: 'ZAR', availableQuantity: 5, isInStock: true,
  primaryImageUrl: null, primaryImageAltText: null,
}

function json(value: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }))
}

beforeEach(() => {
  vi.unstubAllGlobals()
  sessionStorage.clear()
  vi.stubGlobal('scrollTo', vi.fn())
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/api/categories')) return json([{ id: 'c1', name: 'Home', slug: 'home', parentCategoryId: null, activeProductCount: 1 }])
    if (url.includes('/api/products')) return json({ items: [product], page: 1, pageSize: 48, totalCount: 1, totalPages: 1 })
    return json({ title: 'Unexpected request' }, 404)
  }))
})

describe('integrated marketplace frontend', () => {
  it('loads the public catalogue from the API', async () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: /proudly local/i })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Handwoven Basket' })).toBeInTheDocument()
    expect(screen.getByText(/Auto-updating/)).toHaveTextContent('Auto-updating · 1 products')
    expect(screen.queryByText('Ubuntu Weaves')).not.toBeInTheDocument()
  })

  it('sends catalogue search to the API', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByRole('searchbox'), 'basket')
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('search=basket'), expect.anything()))
  })

  it('refreshes the customer catalogue when a marketplace sync event arrives', async () => {
    const listeners = new Map<string, EventListener>()
    class TestEventSource {
      constructor(_url: string) {}
      addEventListener(type: string, listener: EventListener) { listeners.set(type, listener) }
      close() {}
    }
    vi.stubGlobal('EventSource', TestEventSource)
    render(<App />)
    await screen.findByRole('heading', { name: 'Handwoven Basket' })
    const callsBefore = vi.mocked(fetch).mock.calls.filter(([input]) => String(input).includes('/api/products')).length
    listeners.get('sync')?.({ data: JSON.stringify({ Scopes: ['catalogue'] }) } as unknown as Event)
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([input]) => String(input).includes('/api/products')).length).toBeGreaterThan(callsBefore))
  })

  it('opens authentication before a guest adds to cart', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.click(await screen.findByRole('button', { name: /add$/i }))
    expect(screen.getByRole('dialog', { name: 'Welcome back' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in securely' })).toBeInTheDocument()
  })

  it('exposes the complete seller application fields and pending-state notice', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.click(screen.getByRole('button', { name: /sell on mzansi/i }))
    expect(screen.getByRole('dialog', { name: 'Open your seller studio' })).toBeInTheDocument()
    expect(screen.getByLabelText('Trading name')).toBeInTheDocument()
    expect(screen.getByLabelText('Store address')).not.toHaveAttribute('pattern')
    expect(screen.getByText(/seller accounts start as pending/i)).toBeInTheDocument()
  })

  it('lets a signed-in reseller log out from the account menu', async () => {
    const user = userEvent.setup()
    sessionStorage.setItem('mzansi-market-session', JSON.stringify({ accessToken: 'access', refreshToken: 'refresh', expiresIn: 3600, expiresAt: Date.now() + 3600000 }))
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/auth/me')) return json({ userId: 'seller-1', email: 'seller@example.test', displayName: 'Lerato Mokoena', accountStatus: 'Active', emailConfirmed: true, roles: ['Seller'], customer: null, seller: { status: 'Approved', tradingName: 'Lerato Studio', storeSlug: 'lerato-studio' } })
      if (url.includes('/api/auth/logout')) return Promise.resolve(new Response(null, { status: 204 }))
      if (url.includes('/api/categories')) return json([])
      if (url.includes('/api/products')) return json({ items: [], page: 1, pageSize: 48, totalCount: 0, totalPages: 0 })
      return json({ title: 'Unexpected request' }, 404)
    })
    render(<App />)
    await screen.findByRole('button', { name: 'Open account menu' })
    await user.click(screen.getByRole('button', { name: 'Open account menu' }))
    expect(screen.getByRole('dialog', { name: 'Your account' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open seller studio' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Log out' }))
    await waitFor(() => expect(sessionStorage.getItem('mzansi-market-session')).toBeNull())
    expect(screen.queryByRole('dialog', { name: 'Your account' })).not.toBeInTheDocument()
  })
})
