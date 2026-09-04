import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import type { CurrentUser, SellerProduct, SellerStore } from '../api/types'
import { ResellerWorkspace } from './ResellerWorkspace'

const user: CurrentUser = {
  userId: 'seller-1', email: 'seller@example.test', displayName: 'Lerato Mokoena', accountStatus: 'Active',
  emailConfirmed: false, roles: ['Customer', 'Seller'], customer: null,
  seller: { tradingName: 'Ubuntu Goods', status: 'Pending', storeName: 'Ubuntu Goods', storeSlug: 'ubuntu-goods', storeStatus: 'Draft' },
}
const pendingStore: SellerStore = { id: 'store-1', name: 'Ubuntu Goods', slug: 'ubuntu-goods', description: null, supportEmail: 'seller@example.test', storeStatus: 'Draft', sellerStatus: 'Pending', canPublish: false }
const draft: SellerProduct = { id: 'product-1', sku: 'UG-1', name: 'Woven basket', slug: 'woven-basket', description: null, price: 250, currency: 'ZAR', status: 'Draft', onHandQuantity: 8, reservedQuantity: 0, availableQuantity: 8, reorderLevel: 2, categories: [{ id: 'category-1', name: 'Home', slug: 'home' }], imageUrl: null, imageAltText: null, updatedAt: new Date().toISOString() }

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'sellerStore').mockResolvedValue(pendingStore)
  vi.spyOn(api, 'sellerProducts').mockResolvedValue([])
  vi.spyOn(api, 'categories').mockResolvedValue([{ id: 'category-1', name: 'Home', slug: 'home', parentCategoryId: null, activeProductCount: 0 }])
  vi.spyOn(api, 'fulfilment').mockResolvedValue([])
})

describe('reseller workspace', () => {
  it('lets a pending reseller prepare a complete draft product', async () => {
    const create = vi.spyOn(api, 'createSellerProduct').mockResolvedValue(draft)
    const browser = userEvent.setup()
    render(<ResellerWorkspace user={user} announce={vi.fn()} />)
    await screen.findByText(/products are saved as drafts/i)
    await browser.click(screen.getByRole('button', { name: /add product/i }))
    await browser.type(screen.getByLabelText('Product name'), 'Woven basket')
    await browser.type(screen.getByLabelText('SKU'), 'UG-1')
    await browser.type(screen.getByLabelText('Price in rand'), '250')
    await browser.click(screen.getByRole('checkbox', { name: 'Home' }))
    await browser.clear(screen.getByLabelText('Opening stock'))
    await browser.type(screen.getByLabelText('Opening stock'), '8')
    await browser.click(screen.getByRole('button', { name: /create draft product/i }))
    await waitFor(() => expect(create).toHaveBeenCalledWith(expect.objectContaining({ name: 'Woven basket', slug: 'woven-basket', categoryIds: ['category-1'], initialStock: 8 })))
  })

  it('publishes a draft when the store is approved', async () => {
    vi.spyOn(api, 'sellerStore').mockResolvedValue({ ...pendingStore, sellerStatus: 'Approved', storeStatus: 'Active', canPublish: true })
    vi.spyOn(api, 'sellerProducts').mockResolvedValue([draft])
    const publish = vi.spyOn(api, 'publishSellerProduct').mockResolvedValue({ ...draft, status: 'Active' })
    const browser = userEvent.setup()
    render(<ResellerWorkspace user={user} announce={vi.fn()} />)
    await browser.click(await screen.findByRole('button', { name: 'Publish' }))
    await waitFor(() => expect(publish).toHaveBeenCalledWith('product-1'))
  })
})
