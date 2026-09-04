import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { AlertCircle, ArrowRight, BadgeCheck, Box, ClipboardList, Image, LoaderCircle, MapPin, Package, Pencil, Plus, Settings, Store, Trash2, Truck, X } from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { Category, CurrentUser, FulfilmentOrder, SellerProduct, SellerProductInput, SellerStore } from '../api/types'

type Tab = 'products' | 'orders' | 'store'
const money = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' })
const dateTime = new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium', timeStyle: 'short' })
const errorText = (value: unknown) => value instanceof ApiError ? value.message : value instanceof Error ? value.message : 'Something went wrong.'

export function ResellerWorkspace({ user, announce }: { user: CurrentUser; announce: (text: string) => void }) {
  const [tab, setTab] = useState<Tab>('products')
  const [store, setStore] = useState<SellerStore | null>(null)
  const [products, setProducts] = useState<SellerProduct[]>([])
  const [orders, setOrders] = useState<FulfilmentOrder[]>([])
  const [categories, setCategories] = useState<Category[]>([])
  const [busy, setBusy] = useState(true), [actionBusy, setActionBusy] = useState(''), [error, setError] = useState('')
  const [editing, setEditing] = useState<SellerProduct | 'new' | null>(null)
  const [stockProduct, setStockProduct] = useState<SellerProduct | null>(null)
  const [dispatchOrder, setDispatchOrder] = useState<FulfilmentOrder | null>(null)

  const load = useCallback(async () => {
    setBusy(true); setError('')
    try {
      const [storeResult, productResult, categoryResult] = await Promise.all([api.sellerStore(), api.sellerProducts(), api.categories()])
      setStore(storeResult); setProducts(productResult); setCategories(categoryResult)
      if (storeResult.canPublish) setOrders(await api.fulfilment())
    } catch (value) { setError(errorText(value)) } finally { setBusy(false) }
  }, [])
  useEffect(() => { void load() }, [load])

  async function productAction(product: SellerProduct, action: 'publish' | 'unpublish' | 'delete') {
    if (action === 'delete' && !window.confirm(`Archive ${product.name}? It will no longer appear in your catalogue.`)) return
    setActionBusy(product.id); setError('')
    try {
      if (action === 'publish') await api.publishSellerProduct(product.id)
      if (action === 'unpublish') await api.unpublishSellerProduct(product.id)
      if (action === 'delete') await api.deleteSellerProduct(product.id)
      announce(action === 'publish' ? `${product.name} is now visible to customers.` : action === 'unpublish' ? `${product.name} is no longer public.` : `${product.name} was archived.`)
      await load()
    } catch (value) { setError(errorText(value)) } finally { setActionBusy('') }
  }

  async function transition(order: FulfilmentOrder, action: string, carrier?: string, trackingNumber?: string) {
    setActionBusy(order.sellerOrderId); setError('')
    try { await api.transition(order.sellerOrderId, { action, carrier, trackingNumber }); setDispatchOrder(null); announce(`${order.orderNumber} moved forward.`); setOrders(await api.fulfilment()) }
    catch (value) { setError(errorText(value)) } finally { setActionBusy('') }
  }
  return <section className="workspace seller-workspace section-wrap">
    <div className="workspace-header reseller-heading"><div><p className="eyebrow">Reseller studio</p><h1>{store?.name ?? user.seller?.tradingName ?? 'Your store'}</h1><p>Build your catalogue, control available stock, and fulfil customer orders.</p></div><button className="primary-button compact" onClick={() => setEditing('new')}><Plus/> Add product</button></div>
    {store && <div className="seller-status"><div><span className={`status-dot ${store.sellerStatus === 'Approved' ? 'approved' : ''}`}/><p><small>Application</small><strong>{store.sellerStatus}</strong></p></div><ArrowRight/><div><span className={`status-dot ${store.storeStatus === 'Active' ? 'approved' : ''}`}/><p><small>Store</small><strong>{store.storeStatus}</strong></p></div><ArrowRight/><div><span className={`status-dot ${store.canPublish ? 'approved' : ''}`}/><p><small>Customer visibility</small><strong>{store.canPublish ? 'Enabled' : 'Awaiting approval'}</strong></p></div></div>}
    {store && !store.canPublish && <Notice>Your products are saved as drafts while your reseller application is reviewed. You can prepare the full catalogue now and publish after approval.</Notice>}
    {error && <Notice error>{error}</Notice>}
    <nav className="workspace-tabs" aria-label="Reseller workspace"><button aria-current={tab === 'products' ? 'page' : undefined} onClick={() => setTab('products')}><Box/> Products <span>{products.length}</span></button><button aria-current={tab === 'orders' ? 'page' : undefined} onClick={() => setTab('orders')}><ClipboardList/> Orders <span>{orders.length}</span></button><button aria-current={tab === 'store' ? 'page' : undefined} onClick={() => setTab('store')}><Settings/> Store settings</button></nav>
    {busy ? <Loading/> : tab === 'products' ? <Products products={products} canPublish={store?.canPublish ?? false} busy={actionBusy} edit={setEditing} stock={setStockProduct} action={productAction}/> : tab === 'orders' ? <Orders orders={orders} canPublish={store?.canPublish ?? false} busy={actionBusy} transition={transition} dispatch={setDispatchOrder}/> : store && <StoreSettings store={store} onSaved={(next) => { setStore(next); announce('Store details saved.') }}/>} 
    {editing && <ProductSheet product={editing === 'new' ? null : editing} categories={categories} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await load(); announce('Product saved to your catalogue.') }}/>} 
    {stockProduct && <InventorySheet product={stockProduct} onClose={() => setStockProduct(null)} onSaved={async () => { setStockProduct(null); await load(); announce('Inventory updated.') }}/>} 
    {dispatchOrder && <DispatchSheet onClose={() => setDispatchOrder(null)} onSubmit={(carrier, tracking) => transition(dispatchOrder, 'Dispatch', carrier, tracking)}/>} 
  </section>
}

function Products({ products, canPublish, busy, edit, stock, action }: { products: SellerProduct[]; canPublish: boolean; busy: string; edit: (p: SellerProduct) => void; stock: (p: SellerProduct) => void; action: (p: SellerProduct, a: 'publish'|'unpublish'|'delete') => Promise<void> }) {
  if (!products.length) return <Empty icon={<Package/>} title="Add your first product" text="Create a draft with a category, price, image and opening stock. You can edit it before customers see it."/>
  return <div className="seller-product-grid">{products.map(product => <article className="seller-product-card" key={product.id}>
    <div className="seller-product-image">{product.imageUrl ? <img src={product.imageUrl} alt={product.imageAltText ?? product.name}/> : <Image/>}<span className={`publication-badge publication-badge--${product.status.toLowerCase()}`}>{product.status}</span></div>
    <div className="seller-product-body"><p className="seller-product-meta">{product.sku} · {product.categories.map(c => c.name).join(', ')}</p><h2>{product.name}</h2><p className="seller-product-price">{money.format(product.price)}</p><div className="stock-meter"><span><strong>{product.availableQuantity}</strong> available</span><span>{product.reservedQuantity} reserved</span></div>
      <div className="seller-card-actions"><button className="secondary-button compact" onClick={() => edit(product)}><Pencil/> Edit</button><button className="secondary-button compact" onClick={() => stock(product)}><Box/> Stock</button>{product.status === 'Active' ? <button className="text-button" disabled={busy === product.id} onClick={() => action(product, 'unpublish')}>Unpublish</button> : <button className="primary-button compact" disabled={!canPublish || busy === product.id} title={!canPublish ? 'Approval is required before publishing' : ''} onClick={() => action(product, 'publish')}>Publish</button>}<button className="icon-button danger" aria-label={`Archive ${product.name}`} onClick={() => action(product, 'delete')}><Trash2/></button></div>
    </div></article>)}</div>
}

function Orders({ orders, canPublish, busy, transition, dispatch }: { orders: FulfilmentOrder[]; canPublish: boolean; busy: string; transition: (o: FulfilmentOrder, a: string) => Promise<void>; dispatch: (o: FulfilmentOrder) => void }) {
  if (!canPublish) return <Empty icon={<Store/>} title="Orders unlock after approval" text="Once your store is active and products are published, paid customer orders will enter this queue."/>
  if (!orders.length) return <Empty icon={<ClipboardList/>} title="Your queue is clear" text="Paid customer orders for your store will appear here."/>
  return <div className="fulfilment-list">{orders.map(order => { const action = nextOrderAction(order.status); return <article className="fulfilment-card" key={order.sellerOrderId}><div className="order-title"><span className="tag">{splitStatus(order.status)}</span><small>{order.orderNumber}</small><h3>{order.recipientName}</h3><p><MapPin/> {order.city}, {order.province} · Paid {dateTime.format(new Date(order.paidAt))}</p></div><ul>{order.items.map(item => <li key={item.orderItemId}><span>{item.quantity}×</span>{item.productName}<small>{item.sku}</small></li>)}</ul>{order.shipment?.trackingNumber && <p className="tracking"><Truck/> {order.shipment.carrier}: {order.shipment.trackingNumber}</p>}{action && (action === 'Dispatch' ? <button className="primary-button compact" onClick={() => dispatch(order)}>Add tracking & dispatch</button> : <button className="primary-button compact" disabled={busy === order.sellerOrderId} onClick={() => transition(order, action)}>{splitStatus(action)} <ArrowRight/></button>)}</article>})}</div>
}
const nextOrderAction = (status: string) => status === 'ReadyForFulfilment' ? 'StartPicking' : status === 'Picking' ? 'Pack' : status === 'Packed' ? 'Dispatch' : status === 'Shipped' ? 'Deliver' : null
const splitStatus = (value: string) => value.replace(/([A-Z])/g, ' $1').trim()

function StoreSettings({ store, onSaved }: { store: SellerStore; onSaved: (store: SellerStore) => void }) {
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setBusy(true); setError(''); const data = new FormData(event.currentTarget); try { onSaved(await api.updateSellerStore({ name: String(data.get('name')), description: String(data.get('description')) || null, supportEmail: String(data.get('supportEmail')) || null })) } catch (value) { setError(errorText(value)) } finally { setBusy(false) } }
  return <div className="store-settings panel-surface"><div><span className="store-icon"><Store/></span><h2>Store profile</h2><p>Your public store identity. The permanent address is <strong>/{store.slug}</strong>.</p></div>{error && <Notice error>{error}</Notice>}<form className="stack-form" onSubmit={submit}><label>Store name<input name="name" defaultValue={store.name} required minLength={2} maxLength={180}/></label><label>Store description<textarea name="description" defaultValue={store.description ?? ''} maxLength={2000} rows={5} placeholder="Tell customers what makes your products special."/></label><label>Customer support email<input name="supportEmail" type="email" defaultValue={store.supportEmail ?? ''}/></label><button className="primary-button" disabled={busy}>{busy && <LoaderCircle className="spin"/>} Save store details</button></form></div>
}

function ProductSheet({ product, categories, onClose, onSaved }: { product: SellerProduct | null; categories: Category[]; onClose: () => void; onSaved: () => Promise<void> }) {
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [name, setName] = useState(product?.name ?? ''), [slug, setSlug] = useState(product?.slug ?? '')
  function slugify(value: string) { return value.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') }
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setBusy(true); setError(''); const data = new FormData(event.currentTarget); const body: SellerProductInput = { sku: String(data.get('sku')), name, slug, description: String(data.get('description')) || null, price: Number(data.get('price')), categoryIds: data.getAll('categories').map(String), imageUrl: String(data.get('imageUrl')) || null, imageAltText: String(data.get('imageAltText')) || null, initialStock: product ? 0 : Number(data.get('initialStock')), reorderLevel: product?.reorderLevel ?? Number(data.get('reorderLevel')) }; try { product ? await api.updateSellerProduct(product.id, body) : await api.createSellerProduct(body); await onSaved() } catch (value) { setError(errorText(value)) } finally { setBusy(false) } }
  return <Drawer title={product ? 'Edit product' : 'Add a product'} onClose={onClose} wide>{error && <Notice error>{error}</Notice>}<form className="stack-form" onSubmit={submit}><div className="form-grid"><label>Product name<input name="name" value={name} onChange={e => { setName(e.target.value); if (!product) setSlug(slugify(e.target.value)) }} required minLength={2} maxLength={200}/></label><label>SKU<input name="sku" defaultValue={product?.sku ?? ''} required pattern="[A-Za-z0-9][A-Za-z0-9_-]*" maxLength={80}/></label></div><label>Product address<input name="slug" value={slug} onChange={e => setSlug(slugify(e.target.value))} required pattern="[a-z0-9]+(?:-[a-z0-9]+)*"/></label><label>Description<textarea name="description" defaultValue={product?.description ?? ''} maxLength={4000} rows={4}/></label><label>Price in rand<input name="price" type="number" min="0.01" step="0.01" defaultValue={product?.price ?? ''} required/></label>
    <fieldset className="category-fieldset"><legend>Categories</legend>{categories.map(category => <label key={category.id}><input type="checkbox" name="categories" value={category.id} defaultChecked={product?.categories.some(item => item.id === category.id)}/><span>{category.name}</span></label>)}</fieldset>
    <div className="form-grid"><label>Public image URL (optional)<input name="imageUrl" type="url" pattern="https://.*" defaultValue={product?.imageUrl ?? ''} placeholder="https://…"/></label><label>Image description (optional)<input name="imageAltText" defaultValue={product?.imageAltText ?? ''} maxLength={300} placeholder="Describe the product, not the filename"/></label></div>
    {!product && <div className="form-grid"><label>Opening stock<input name="initialStock" type="number" min="0" max="1000000" defaultValue="0" required/></label><label>Low-stock alert level<input name="reorderLevel" type="number" min="0" max="1000000" defaultValue="2" required/></label></div>}
    <button className="primary-button full-width" disabled={busy || categories.length === 0}>{busy && <LoaderCircle className="spin"/>} {product ? 'Save product' : 'Create draft product'}</button>{categories.length === 0 && <Notice>No active categories exist yet. A product administrator must create one before a reseller can add products.</Notice>}</form></Drawer>
}

function InventorySheet({ product, onClose, onSaved }: { product: SellerProduct; onClose: () => void; onSaved: () => Promise<void> }) {
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setBusy(true); setError(''); const data = new FormData(event.currentTarget); try { await api.updateSellerInventory(product.id, { onHandQuantity: Number(data.get('onHandQuantity')), reorderLevel: Number(data.get('reorderLevel')), reason: String(data.get('reason')) }); await onSaved() } catch (value) { setError(errorText(value)) } finally { setBusy(false) } }
  return <Drawer title={`Stock · ${product.name}`} onClose={onClose}>{error && <Notice error>{error}</Notice>}<Notice>{product.reservedQuantity} units are reserved for customer orders. On-hand stock cannot be set below that amount.</Notice><form className="stack-form" onSubmit={submit}><label>On-hand quantity<input name="onHandQuantity" type="number" min={product.reservedQuantity} max="1000000" defaultValue={product.onHandQuantity} required/></label><label>Low-stock alert level<input name="reorderLevel" type="number" min="0" max="1000000" defaultValue={product.reorderLevel} required/></label><label>Reason for adjustment<textarea name="reason" minLength={3} maxLength={500} rows={3} required placeholder="New delivery, stock count correction…"/></label><button className="primary-button full-width" disabled={busy}>{busy && <LoaderCircle className="spin"/>} Update inventory</button></form></Drawer>
}

function DispatchSheet({ onClose, onSubmit }: { onClose: () => void; onSubmit: (carrier: string, tracking: string) => Promise<void> }) { const [busy, setBusy] = useState(false); return <Drawer title="Dispatch order" onClose={onClose}><form className="stack-form" onSubmit={async event => { event.preventDefault(); setBusy(true); const data = new FormData(event.currentTarget); await onSubmit(String(data.get('carrier')), String(data.get('tracking'))); setBusy(false) }}><label>Carrier<input name="carrier" required maxLength={100}/></label><label>Tracking number<input name="tracking" required maxLength={160}/></label><button className="primary-button full-width" disabled={busy}>{busy && <LoaderCircle className="spin"/>} Confirm dispatch</button></form></Drawer> }

function Drawer({ title, onClose, children, wide }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) { const ref = useRef<HTMLElement>(null), close = useRef(onClose); close.current = onClose; useEffect(() => { const old = document.activeElement as HTMLElement | null; const overflow = document.body.style.overflow; document.body.style.overflow = 'hidden'; ref.current?.querySelector<HTMLElement>('button,input,textarea')?.focus(); const key = (event: KeyboardEvent) => { if (event.key === 'Escape') close.current(); if (event.key === 'Tab' && ref.current) { const controls = [...ref.current.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),textarea:not(:disabled),select:not(:disabled)')]; const first = controls[0], last = controls.at(-1); if (first && last && event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() } else if (first && last && !event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() } } }; document.addEventListener('keydown', key); return () => { document.removeEventListener('keydown', key); document.body.style.overflow = overflow; old?.focus() } }, []); return <div className="sheet-layer" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><section ref={ref} className={`sheet ${wide ? 'sheet--wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}><header><h2>{title}</h2><button className="icon-button" aria-label="Close" onClick={onClose}><X/></button></header><div className="sheet__body">{children}</div></section></div> }
function Notice({ children, error }: { children: ReactNode; error?: boolean }) { return <div className={`notice notice--${error ? 'error' : 'info'}`} role={error ? 'alert' : 'status'}>{error ? <AlertCircle/> : <BadgeCheck/>}{children}</div> }
function Loading() { return <div className="loading-state" role="status"><LoaderCircle className="spin"/> Loading your reseller workspace…</div> }
function Empty({ icon, title, text }: { icon: ReactNode; title: string; text: string }) { return <div className="empty-state reseller-empty">{icon}<h2>{title}</h2><p>{text}</p></div> }
