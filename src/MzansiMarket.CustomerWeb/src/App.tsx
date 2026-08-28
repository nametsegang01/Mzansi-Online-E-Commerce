import { useMemo, useState } from 'react'
import {
  ArrowRight,
  BadgeCheck,
  Heart,
  HelpCircle,
  Home,
  MapPin,
  Menu,
  PackageCheck,
  Search,
  ShieldCheck,
  ShoppingBag,
  SlidersHorizontal,
  Sparkles,
  Store,
  UserRound,
  X,
} from 'lucide-react'
import { BrandMark } from './components/BrandMark'
import { ProductCard } from './components/ProductCard'
import { categories, products, type Product } from './data/catalog'

export function App() {
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState('all')
  const [cartCount, setCartCount] = useState(0)
  const [favourites, setFavourites] = useState<Set<number>>(new Set())
  const [menuOpen, setMenuOpen] = useState(false)
  const [announcement, setAnnouncement] = useState('')
  const [joined, setJoined] = useState(false)

  const visibleProducts = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    return products.filter((product) => {
      const matchesCategory = category === 'all' || product.category === category
      const matchesSearch =
        normalizedQuery.length === 0 ||
        [product.name, product.seller, product.province].some((value) =>
          value.toLowerCase().includes(normalizedQuery),
        )
      return matchesCategory && matchesSearch
    })
  }, [category, query])

  function toggleFavourite(product: Product) {
    setFavourites((current) => {
      const next = new Set(current)
      if (next.has(product.id)) {
        next.delete(product.id)
        setAnnouncement(`${product.name} removed from favourites.`)
      } else {
        next.add(product.id)
        setAnnouncement(`${product.name} added to favourites.`)
      }
      return next
    })
  }

  function addToCart(product: Product) {
    setCartCount((count) => count + 1)
    setAnnouncement(`${product.name} added to your cart.`)
  }

  return (
    <div id="top" className="app-shell">
      <a className="skip-link" href="#main-content">Skip to products</a>
      <p className="sr-only" aria-live="polite">{announcement}</p>

      <header className="site-header">
        <div className="utility-bar">
          <p>Free delivery over R750 · Easy 14-day returns</p>
          <nav aria-label="Utility navigation">
            <a href="#seller-story"><Store size={14} /> Sell on Mzansi</a>
            <a href="#help"><HelpCircle size={14} /> Help</a>
          </nav>
        </div>

        <div className="header-main glass-surface">
          <button
            className="icon-button mobile-only"
            type="button"
            aria-label={menuOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((open) => !open)}
          >
            {menuOpen ? <X /> : <Menu />}
          </button>
          <BrandMark />

          <nav className={`primary-nav ${menuOpen ? 'primary-nav--open' : ''}`} aria-label="Primary navigation">
            <a href="#featured" onClick={() => setMenuOpen(false)}>New in</a>
            <a href="#categories" onClick={() => setMenuOpen(false)}>Categories</a>
            <a href="#seller-story" onClick={() => setMenuOpen(false)}>Local makers</a>
            <a href="#featured" onClick={() => setMenuOpen(false)}>Deals</a>
          </nav>

          <div className="header-actions">
            <button className="location-button desktop-only" type="button">
              <MapPin size={17} />
              <span><small>Deliver to</small> Gauteng</span>
            </button>
            <button className="icon-button desktop-only" type="button" aria-label="Your account">
              <UserRound />
            </button>
            <button className="cart-button" type="button" aria-label={`Shopping cart with ${cartCount} items`}>
              <ShoppingBag />
              <span className="cart-button__count">{cartCount}</span>
            </button>
          </div>
        </div>
      </header>

      <main id="main-content">
        <section className="search-band" aria-label="Product search">
          <form className="search-box" role="search" onSubmit={(event) => event.preventDefault()}>
            <Search aria-hidden="true" />
            <label className="sr-only" htmlFor="catalog-search">Search products, categories, or sellers</label>
            <input
              id="catalog-search"
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search products, categories or sellers"
            />
            <button className="filter-button" type="button" aria-label="Open filters">
              <SlidersHorizontal /> <span className="desktop-only">Filters</span>
            </button>
          </form>
        </section>

        <section className="hero-section section-wrap" aria-labelledby="hero-heading">
          <div className="hero-copy">
            <p className="eyebrow"><Sparkles size={15} /> Made close to home</p>
            <h1 id="hero-heading">Proudly local.<br /><em>Beautifully made.</em></h1>
            <p className="hero-lede">
              Discover considered pieces from independent South African makers, delivered with care.
            </p>
            <div className="hero-actions">
              <a className="primary-button" href="#featured">Shop local <ArrowRight /></a>
              <a className="text-link" href="#seller-story">Meet our makers</a>
            </div>
            <div className="hero-proof" aria-label="Marketplace highlights">
              <span><strong>9</strong> provinces</span>
              <span><strong>240+</strong> local sellers</span>
              <span><strong>4.8</strong> average rating</span>
            </div>
          </div>

          <div className="hero-gallery" aria-label="Featured local craft collection">
            <div className="hero-orbit hero-orbit--one" aria-hidden="true" />
            <div className="hero-orbit hero-orbit--two" aria-hidden="true" />
            <div className="hero-product hero-product--basket">
              <span aria-hidden="true">M</span>
              <p>Ubuntu weave</p>
            </div>
            <div className="hero-product hero-product--mug"><span aria-hidden="true">◡</span></div>
            <div className="hero-product hero-product--bottle"><span>RENEW</span></div>
            <div className="hero-product hero-product--plant" aria-hidden="true"><i /><i /><i /><i /><b /></div>
            <div className="maker-note glass-surface">
              <BadgeCheck size={18} />
              <span><strong>Seller verified</strong>Handmade in Durban</span>
            </div>
          </div>
        </section>

        <section className="trust-strip section-wrap" aria-label="Shopping benefits">
          <span><ShieldCheck /> Secure checkout</span>
          <span><PackageCheck /> Tracked delivery</span>
          <span><Heart /> Support local</span>
          <span><BadgeCheck /> Verified sellers</span>
        </section>

        <section id="categories" className="category-section section-wrap" aria-labelledby="category-heading">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Find your next favourite</p>
              <h2 id="category-heading">Shop by category</h2>
            </div>
            <a className="text-link desktop-only" href="#featured">Browse everything <ArrowRight /></a>
          </div>
          <div className="category-scroller" role="group" aria-label="Filter products by category">
            {categories.map((item, index) => (
              <button
                key={item.id}
                className={`category-pill category-pill--${index + 1}`}
                type="button"
                aria-pressed={category === item.id}
                onClick={() => setCategory(item.id)}
              >
                <span aria-hidden="true">{['✦', '⌂', '◌', '✧', '♨', '◇'][index]}</span>
                {item.label}
              </button>
            ))}
          </div>
        </section>

        <section id="featured" className="products-section section-wrap" aria-labelledby="featured-heading">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Curated for you</p>
              <h2 id="featured-heading">Made with meaning</h2>
            </div>
            <p className="result-count" aria-live="polite">{visibleProducts.length} products</p>
          </div>

          {visibleProducts.length > 0 ? (
            <div className="product-grid">
              {visibleProducts.map((product) => (
                <ProductCard
                  key={product.id}
                  product={product}
                  favourite={favourites.has(product.id)}
                  onToggleFavourite={toggleFavourite}
                  onAddToCart={addToCart}
                />
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <Search />
              <h3>No local finds yet</h3>
              <p>Try another product, seller, or province.</p>
              <button type="button" className="secondary-button" onClick={() => { setQuery(''); setCategory('all') }}>
                Clear search
              </button>
            </div>
          )}
        </section>

        <section id="seller-story" className="maker-section section-wrap" aria-labelledby="maker-heading">
          <div className="maker-portrait" aria-hidden="true">
            <span className="maker-portrait__sun" />
            <span className="maker-portrait__table" />
            <span className="maker-portrait__vase">✣</span>
          </div>
          <div className="maker-copy">
            <p className="eyebrow">Maker story · Limpopo</p>
            <h2 id="maker-heading">Every purchase carries a story forward.</h2>
            <blockquote>
              “Mzansi Market helps our small studio reach homes across South Africa while we keep making things the way our family taught us.”
            </blockquote>
            <p>— Lerato M., founder of Renew Botanics</p>
            <a className="text-link" href="#featured">Shop seller stories <ArrowRight /></a>
          </div>
        </section>

        <section className="newsletter section-wrap" aria-labelledby="newsletter-heading">
          <div>
            <p className="eyebrow">A little local inspiration</p>
            <h2 id="newsletter-heading">New makers, thoughtful finds, no noise.</h2>
          </div>
          {joined ? (
            <p className="newsletter__success"><BadgeCheck /> You’re on the list. Welcome to Mzansi Market.</p>
          ) : (
            <form onSubmit={(event) => { event.preventDefault(); setJoined(true) }}>
              <label className="sr-only" htmlFor="newsletter-email">Email address</label>
              <input id="newsletter-email" type="email" required placeholder="Email address" autoComplete="email" />
              <button className="primary-button" type="submit">Join us <ArrowRight /></button>
            </form>
          )}
        </section>
      </main>

      <footer id="help" className="site-footer">
        <div className="section-wrap footer-grid">
          <BrandMark />
          <p>Quality finds from trusted local sellers, made for Mzansi.</p>
          <nav aria-label="Footer navigation">
            <a href="#featured">Shop</a>
            <a href="#seller-story">Our sellers</a>
            <a href="#help">Delivery & returns</a>
            <a href="#help">Contact</a>
          </nav>
          <small>© 2026 Mzansi Market Online</small>
        </div>
      </footer>

      <nav className="mobile-tab-bar glass-surface" aria-label="Mobile navigation">
        <a href="#top" aria-current="page"><Home /><span>Home</span></a>
        <a href="#categories"><Menu /><span>Categories</span></a>
        <a href="#featured"><Heart /><span>Favourites</span></a>
        <button type="button" aria-label={`Cart, ${cartCount} items`}><ShoppingBag /><span>Cart</span></button>
        <button type="button"><UserRound /><span>Account</span></button>
      </nav>
    </div>
  )
}
