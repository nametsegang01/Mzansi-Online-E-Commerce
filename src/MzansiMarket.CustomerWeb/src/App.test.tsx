import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from './App'

describe('customer storefront', () => {
  it('exposes the marketplace purpose and core navigation', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: /proudly local/i })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeInTheDocument()
    expect(screen.getByRole('searchbox')).toHaveAccessibleName(/search products/i)
  })

  it('filters products from the search field', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.type(screen.getByRole('searchbox'), 'Limpopo')

    expect(screen.getByRole('heading', { name: 'Indigenous Body Oil' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Forest Ceramic Mug' })).not.toBeInTheDocument()
    expect(screen.getByText('1 products')).toBeInTheDocument()
  })

  it('updates category state and cart feedback', async () => {
    const user = userEvent.setup()
    render(<App />)

    const beautyFilter = screen.getByRole('button', { name: /beauty/i })
    await user.click(beautyFilter)
    expect(beautyFilter).toHaveAttribute('aria-pressed', 'true')

    await user.click(screen.getByRole('button', { name: 'Add' }))
    expect(screen.getByRole('button', { name: 'Shopping cart with 1 items' })).toBeInTheDocument()
    expect(screen.getByText(/indigenous body oil added to your cart/i)).toBeInTheDocument()
  })

  it('supports reversible favourites', async () => {
    const user = userEvent.setup()
    render(<App />)

    const favourite = screen.getByRole('button', { name: /add handwoven storage basket to favourites/i })
    await user.click(favourite)
    expect(favourite).toHaveAttribute('aria-pressed', 'true')
    expect(favourite).toHaveAccessibleName(/remove handwoven storage basket from favourites/i)
  })
})
