# Mzansi Market Customer Web

Responsive React and TypeScript marketplace application for Mzansi Market customers and sellers.

## Run locally

```powershell
npm install
npm run dev
```

Quality checks:

```powershell
npm test
npm run build
npm audit --audit-level=high
```

## Current scope

- Live public catalogue search, filters, stock, and server pricing
- Customer and seller registration, login, token refresh, and role-aware navigation
- Customer address book, cart, checkout, and sandbox payment initiation
- Reseller application status, store profile, product catalogue, image metadata, inventory, publication, and approved-seller fulfilment workflow
- Responsive sheets, mobile navigation, screen-reader announcements, and focus management
- Loading, empty, error, retry, reduced-motion, reduced-transparency, contrast, and forced-color behavior

The app uses `https://mzansi-market-api.onrender.com` by default. Set `VITE_API_URL` to override it locally. Direct image upload storage, reports, returns, refunds, and staff category/promotion tools remain later backend work.

## Visual direction

The interface translates the supplied customer references into reusable tokens: deep forest teal, eucalyptus and sage layers, warm ivory surfaces, and restrained gold accents. Translucency is limited to functional floating chrome, while content cards remain opaque and legible.
