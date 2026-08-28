import { ShoppingBag } from 'lucide-react'

export function BrandMark() {
  return (
    <a className="brand-mark" href="#top" aria-label="Mzansi Market home">
      <span className="brand-mark__bag" aria-hidden="true">
        <ShoppingBag size={24} strokeWidth={1.7} />
        <strong>M</strong>
      </span>
      <span className="brand-mark__wordmark">
        <strong>Mzansi</strong>
        <small>MARKET</small>
      </span>
    </a>
  )
}
