import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

const product = {
  id: '11111111-1111-1111-1111-111111111111', sku: 'LOCAL-1', name: 'Handwoven Basket', slug: 'handwoven-basket',
  price: 420, currency: 'ZAR', availableQuantity: 5, isInStock: true, storeName: 'Ubuntu Weaves',
  storeSlug: 'ubuntu-weaves', primaryImageUrl: null, primaryImageAltText: null,
}

function json(value: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }))
}

beforeEach(() => {
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
    expect(screen.getByText('1 products')).toBeInTheDocument()
  })

  it('sends catalogue search to the API', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByRole('searchbox'), 'basket')
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('search=basket'), expect.anything()))
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
    expect(screen.getByText(/seller accounts start as pending/i)).toBeInTheDocument()
  })
})
