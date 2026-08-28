import { Heart, Image, ShoppingBag, Store } from 'lucide-react'
import type { Product } from '../api/types'

const currency = new Intl.NumberFormat('en-ZA', {
  style: 'currency', currency: 'ZAR',
})

type ProductCardProps = {
  product: Product
  favourite: boolean
  busy?: boolean
  onToggleFavourite: (product: Product) => void
  onAddToCart: (product: Product) => void
}

export function ProductCard({
  product,
  favourite,
  onToggleFavourite,
  onAddToCart, busy,
}: ProductCardProps) {
  return (
    <article className="product-card">
      <div className="product-card__visual">
        {product.primaryImageUrl ? <img src={product.primaryImageUrl} alt={product.primaryImageAltText || product.name} /> : <span className="product-art" aria-label="Product image unavailable"><Image size={58} strokeWidth={1.2} /></span>}
        <span className={`stock-badge ${product.isInStock ? '' : 'stock-badge--out'}`}>{product.isInStock ? `${product.availableQuantity} available` : 'Sold out'}</span>
        <button
          className="icon-button product-card__favourite"
          type="button"
          aria-label={`${favourite ? 'Remove' : 'Add'} ${product.name} ${favourite ? 'from' : 'to'} favourites`}
          aria-pressed={favourite}
          onClick={() => onToggleFavourite(product)}
        >
          <Heart fill={favourite ? 'currentColor' : 'none'} size={19} />
        </button>
      </div>
      <div className="product-card__content">
        <p className="product-card__seller"><Store size={13} /> {product.storeName}</p>
        <h3>{product.name}</h3>
        <p className="product-card__province">SKU {product.sku}</p>
        <div className="product-card__footer">
          <strong>{currency.format(product.price)}</strong>
          <button className="add-button" type="button" disabled={!product.isInStock || busy} onClick={() => onAddToCart(product)}>
            <ShoppingBag size={15} /> {busy ? 'Adding…' : 'Add'}
          </button>
        </div>
      </div>
    </article>
  )
}
