import { useEffect, useState } from 'react'
import { AlertCircle, LoaderCircle, PackageCheck, RefreshCw } from 'lucide-react'
import { api } from '../api/client'
import type { CustomerOrder } from '../api/types'

const orderDate = new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium', timeStyle: 'short' })

function formatDate(value: string | null, fallback: string) {
  return value ? orderDate.format(new Date(value)) : fallback
}

function formatTotal(total: number, currency: string) {
  return new Intl.NumberFormat('en-ZA', { style: 'currency', currency }).format(total)
}

export function CustomerOrders() {
  const [orders, setOrders] = useState<CustomerOrder[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')

    api.orders()
      .then(result => {
        if (active) setOrders(result)
      })
      .catch(reason => {
        if (active) setError(reason instanceof Error ? reason.message : 'Orders could not be loaded.')
      })
      .finally(() => {
        if (active) setLoading(false)
      })

    return () => { active = false }
  }, [attempt])

  return <section className="workspace section-wrap customer-orders" aria-labelledby="orders-heading">
    <div className="customer-orders__inner">
      <div className="panel-heading">
        <div>
          <p className="eyebrow">Your purchases</p>
          <h2 id="orders-heading">Orders</h2>
        </div>
      </div>
      {loading ? <div className="loading-state" role="status"><LoaderCircle className="spin"/> Loading your orders…</div>
        : error ? <div className="customer-orders__error" role="alert">
          <AlertCircle/>
          <p>{error}</p>
          <button className="secondary-button" type="button" onClick={() => setAttempt(value => value + 1)}><RefreshCw/> Try again</button>
        </div>
          : orders.length === 0 ? <div className="empty-state compact">
            <PackageCheck/>
            <h3>No orders yet</h3>
            <p>Your completed checkouts will appear here.</p>
          </div>
            : <div className="customer-order-list">{orders.map(order => <article className="customer-order-card" key={order.orderNumber}>
              <header className="customer-order-card__header">
                <div>
                  <h3>Order {order.orderNumber}</h3>
                  <p>Placed <time dateTime={order.placedAt}>{formatDate(order.placedAt, 'Date unavailable')}</time></p>
                </div>
                <span className="tag">{order.status}</span>
              </header>
              <div className="customer-order-card__summary">
                <div>
                  <h4>Items</h4>
                  <ul>{order.items.map((item, index) => <li key={`${item.name}-${index}`}>{item.name} x {item.quantity}</li>)}</ul>
                </div>
                <p className="customer-order-card__total"><span>Total</span><strong>{formatTotal(order.total, order.currency)}</strong></p>
              </div>
              <div className="customer-order-card__shipments">
                <h4>Delivery updates</h4>
                {order.sellerOrders.map((sellerOrder, index) => <section className="customer-shipment" key={`${order.orderNumber}-shipment-${index}`}>
                  <h5>Shipment {index + 1} <span className="tag">{sellerOrder.shipment?.status ?? sellerOrder.status}</span></h5>
                  {sellerOrder.shipment ? <dl>
                    <div><dt>Carrier</dt><dd>{sellerOrder.shipment.carrier || 'Not provided'}</dd></div>
                    <div><dt>Tracking number</dt><dd>{sellerOrder.shipment.trackingNumber || 'Not provided'}</dd></div>
                    <div><dt>Dispatched</dt><dd><time dateTime={sellerOrder.shipment.dispatchedAt ?? undefined}>{formatDate(sellerOrder.shipment.dispatchedAt, 'Not dispatched')}</time></dd></div>
                    <div><dt>Delivered</dt><dd><time dateTime={sellerOrder.shipment.deliveredAt ?? undefined}>{formatDate(sellerOrder.shipment.deliveredAt, 'Not delivered')}</time></dd></div>
                  </dl> : <p>Shipment details are not available yet.</p>}
                </section>)}
              </div>
            </article>)}</div>}
    </div>
  </section>
}