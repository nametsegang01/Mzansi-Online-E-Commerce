# Mzansi Market Customer Web

Responsive React and TypeScript storefront prototype for Mzansi Market customers.

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

- Responsive storefront shell and mobile tab bar
- Product search and category filtering
- Local seller and province context
- Reversible favourites
- Add-to-cart count and screen-reader announcements
- Newsletter interaction
- Reduced-motion, reduced-transparency, increased-contrast, keyboard, and forced-color behavior

Catalogue content is deliberately mock data in `src/data/catalog.ts`. The next work unit should replace it through a typed API client once public catalogue endpoints exist in the ASP.NET backend.

## Visual direction

The interface translates the supplied customer references into reusable tokens: deep forest teal, eucalyptus and sage layers, warm ivory surfaces, and restrained gold accents. Translucency is limited to functional floating chrome, while content cards remain opaque and legible.
