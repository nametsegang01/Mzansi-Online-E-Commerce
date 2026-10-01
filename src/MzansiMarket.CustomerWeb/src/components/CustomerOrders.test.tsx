import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import type { CustomerOrder } from '../api/types'
import { CustomerOrders } from './CustomerOrders'

const order: CustomerOrder = {
  orderNumber: 'MM-20260912-ABC123',
  placedAt: '2026-09-12T10:15:00Z',
  status: 'PartiallyShipped',
  total: 525,
  currency: 'ZAR',
  items: [
    { name: 'Handwoven Basket', quantity: 2 },
    { name: 'Ceramic Mug', quantity: 1 },
  ],
  sellerOrders: [
    {
      status: 'Shipped',
      shipment: {
        status: 'Dispatched',
        carrier: 'Courier One',
        trackingNumber: 'TRACK-ONE',
        dispatchedAt: '2026-09-12T12:00:00Z',
        deliveredAt: null,
      },
    },
    {
      status: 'Delivered',
      shipment: {
        status: 'Delivered',
        carrier: 'Courier Two',
        trackingNumber: 'TRACK-TWO',
        dispatchedAt: '2026-09-11T09:00:00Z',
        deliveredAt: '2026-09-13T14:30:00Z',
      },
    },
  ],
}

beforeEach(() => vi.restoreAllMocks())

describe('customer order history', () => {
  it('shows a loading state while orders are requested', async () => {
    let resolveOrders!: (orders: CustomerOrder[]) => void
    vi.spyOn(api, 'orders').mockReturnValue(new Promise(resolve => { resolveOrders = resolve }))

    render(<CustomerOrders />)

    expect(screen.getByRole('status')).toHaveTextContent('Loading your orders')
    resolveOrders([])
    expect(await screen.findByRole('heading', { name: 'No orders yet' })).toBeInTheDocument()
  })

  it('shows an empty state when the customer has no orders', async () => {
    vi.spyOn(api, 'orders').mockResolvedValue([])

    render(<CustomerOrders />)

    expect(await screen.findByRole('heading', { name: 'No orders yet' })).toBeInTheDocument()
  })

  it('shows order details and separate tracking for each seller shipment', async () => {
    vi.spyOn(api, 'orders').mockResolvedValue([order])

    render(<CustomerOrders />)

    expect(await screen.findByRole('heading', { name: `Order ${order.orderNumber}` })).toBeInTheDocument()
    expect(screen.getByText('PartiallyShipped')).toBeInTheDocument()
    expect(screen.getByText('Handwoven Basket x 2')).toBeInTheDocument()
    expect(screen.getByText('Ceramic Mug x 1')).toBeInTheDocument()
    expect(screen.getByText(/525,00/)).toBeInTheDocument()
    expect(screen.getByText('TRACK-ONE')).toBeInTheDocument()
    expect(screen.getByText('TRACK-TWO')).toBeInTheDocument()
    expect(screen.getByText('Courier One')).toBeInTheDocument()
    expect(screen.getByText('Courier Two')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Shipment 1 Dispatched/ })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Shipment 2 Delivered/ })).toBeInTheDocument()
    expect(screen.getByText('Not delivered')).toBeInTheDocument()
    expect(screen.getByText(/13 Sept 2026/)).toBeInTheDocument()
  })

  it('shows a retryable error state', async () => {
    const loadOrders = vi.spyOn(api, 'orders')
      .mockRejectedValueOnce(new Error('Orders are unavailable'))
      .mockResolvedValueOnce([])
    const user = userEvent.setup()

    render(<CustomerOrders />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Orders are unavailable')
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('heading', { name: 'No orders yet' })).toBeInTheDocument()
    await waitFor(() => expect(loadOrders).toHaveBeenCalledTimes(2))
  })
})