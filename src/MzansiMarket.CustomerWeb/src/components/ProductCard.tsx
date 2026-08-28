import {
  Armchair,
  Coffee,
  Heart,
  Lamp,
  Palette,
  Shirt,
  ShoppingBasket,
  Sparkles,
  Star,
  Utensils,
} from 'lucide-react'
import type { Product, ProductArtwork } from '../data/catalog'

const artworkIcons: Record<ProductArtwork, typeof Armchair> = {
  basket: ShoppingBasket,
  chair: Armchair,
  coffee: Coffee,
  lamp: Lamp,
  shirt: Shirt,
  skincare: Sparkles,
  food: Utensils,
  art: Palette,
}

const currency = new Intl.NumberFormat('en-ZA', {
  style: 'currency',
  currency: 'ZAR',
  minimumFractionDigits: 2,
})

type ProductCardProps = {
  product: Product
  favourite: boolean
  onToggleFavourite: (product: Product) => void
  onAddToCart: (product: Product) => void
}

export function ProductCard({
  product,
  favourite,
  onToggleFavourite,
  onAddToCart,
}: ProductCardProps) {
  const ArtworkIcon = artworkIcons[product.artwork]

  return (
    <article className="product-card">
      <div className="product-card__visual" style={{ '--product-tone': product.tone } as React.CSSProperties}>
        {product.badge && <span className="product-card__badge">{product.badge}</span>}
        <button
          className="icon-button product-card__favourite"
          type="button"
          aria-label={`${favourite ? 'Remove' : 'Add'} ${product.name} ${favourite ? 'from' : 'to'} favourites`}
          aria-pressed={favourite}
          onClick={() => onToggleFavourite(product)}
        >
          <Heart fill={favourite ? 'currentColor' : 'none'} size={19} />
        </button>
        <span className={`product-art product-art--${product.artwork}`} aria-hidden="true">
          <ArtworkIcon size={76} strokeWidth={1.15} />
        </span>
      </div>
      <div className="product-card__content">
        <div className="product-card__seller">
          <span>{product.seller}</span>
          <span className="rating" aria-label={`${product.rating} out of 5 stars`}>
            <Star size={13} fill="currentColor" aria-hidden="true" /> {product.rating}
          </span>
        </div>
        <h3>{product.name}</h3>
        <p className="product-card__province">Made in {product.province}</p>
        <div className="product-card__footer">
          <p className="product-card__price">
            <strong>{currency.format(product.price)}</strong>
            {product.previousPrice && <del>{currency.format(product.previousPrice)}</del>}
          </p>
          <button className="add-button" type="button" onClick={() => onAddToCart(product)}>
            Add
          </button>
        </div>
      </div>
    </article>
  )
}
