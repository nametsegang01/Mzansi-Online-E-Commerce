export function BrandMark() {
  return (
    <a className="brand-mark" href="#top" aria-label="Mzansi Market home">
      <img
        className="brand-mark__logo"
        src="/mzansi-market-logo-mark.png"
        alt=""
        aria-hidden="true"
      />
      <span className="brand-mark__wordmark">
        <span><strong>Mzansi Market</strong> <em>Online</em></span>
        <small aria-hidden="true"><i /> <b /> <i /></small>
      </span>
    </a>
  )
}
