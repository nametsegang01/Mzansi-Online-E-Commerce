import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import type { CurrentUser, SellerApplication } from '../api/types'
import { AdminWorkspace } from './AdminWorkspace'

const admin: CurrentUser = { userId: 'admin-1', email: 'admin@example.test', displayName: 'Marketplace Admin', accountStatus: 'Active', emailConfirmed: false, roles: ['SystemAdministrator'], customer: null, seller: null }
const pending: SellerApplication = { sellerId: 'seller-1', displayName: 'Lerato Mokoena', email: 'lerato@example.test', tradingName: 'Ubuntu Goods', registrationNumber: 'REG-001', sellerStatus: 'Pending', storeName: 'Ubuntu Goods', storeSlug: 'ubuntu-goods', storeStatus: 'Draft', createdAt: '2026-09-07T08:00:00Z' }

beforeEach(() => { vi.restoreAllMocks(); vi.spyOn(api, 'adminSellerApplications').mockResolvedValue([pending]) })

describe('administrator reseller approvals', () => {
  it('shows the pending application details', async () => {
    render(<AdminWorkspace user={admin} announce={vi.fn()} onSignOut={vi.fn()} />)
    expect(await screen.findByRole('heading', { name: 'Ubuntu Goods' })).toBeInTheDocument()
    expect(screen.getByText('REG-001')).toBeInTheDocument()
    expect(screen.getByText('lerato@example.test')).toBeInTheDocument()
  })

  it('approves a reseller and updates the review queue', async () => {
    const approve = vi.spyOn(api, 'decideSellerApplication').mockResolvedValue({ ...pending, sellerStatus: 'Approved', storeStatus: 'Active' })
    const announce = vi.fn(); const browser = userEvent.setup()
    render(<AdminWorkspace user={admin} announce={announce} onSignOut={vi.fn()} />)
    await browser.click(await screen.findByRole('button', { name: 'Approve' }))
    await waitFor(() => expect(approve).toHaveBeenCalledWith('seller-1', 'Approve'))
    expect(announce).toHaveBeenCalledWith('Ubuntu Goods is now approved.')
  })

  it('silently reloads applications after a marketplace sync', async () => {
    const applications = vi.mocked(api.adminSellerApplications)
    const { rerender } = render(<AdminWorkspace user={admin} announce={vi.fn()} onSignOut={vi.fn()} syncVersion={0} />)
    await screen.findByRole('heading', { name: 'Ubuntu Goods' })
    rerender(<AdminWorkspace user={admin} announce={vi.fn()} onSignOut={vi.fn()} syncVersion={1} />)
    await waitFor(() => expect(applications).toHaveBeenCalledTimes(2))
  })
})
