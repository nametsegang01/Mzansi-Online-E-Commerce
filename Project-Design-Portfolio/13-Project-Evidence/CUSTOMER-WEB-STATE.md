# Customer web implementation state

## Objective and phase

- Objective: establish the customer-facing web application and visual language.
- Current phase: storefront foundation complete and validated.
- Application: `src/MzansiMarket.CustomerWeb`.

## Architecture and decisions

- React 19, TypeScript 6, and Vite 8 provide the frontend foundation.
- The ASP.NET backend remains the source of truth for future catalogue, pricing, stock, basket, account, and order data.
- Mock products are isolated in `src/data/catalog.ts` so the UI can be replaced by a typed API client without redesigning components.
- Product cards preserve seller and province context to reinforce the multi-seller marketplace model.
- Brand tokens follow the supplied customer references: forest teal, sage/eucalyptus, warm ivory, and restrained gold.
- System fonts are used for controls and body copy; a restrained serif display face supplies the editorial marketplace character.
- Translucency is reserved for navigation and floating controls. Opaque fallbacks support reduced transparency.

## Completed and validated

- Responsive header, navigation, search, hero, category filters, product grid, seller story, newsletter, footer, and mobile tab bar.
- Search, category selection, reversible favourites, cart count, and live-region feedback.
- Four automated interaction and accessibility-oriented component tests.
- Production TypeScript/Vite build.
- Dependency audit with zero known vulnerabilities.
- Browser inspection at 1440×1000 and 390×844.
- Mobile document width equals viewport width; no horizontal overflow.
- Browser interaction test confirmed search and cart behavior with no console errors.
- Reduced motion, reduced transparency, increased contrast, focus-visible, and forced-color CSS behavior included.

## Pending work units

1. Public catalogue API: paginated products, categories, sellers, search, filters, stock visibility, and product details.
2. Typed API client and query caching with loading, empty, retry, and offline states.
3. Customer authentication, addresses, favourites persistence, and account screens.
4. Transactional basket and checkout integrated with stock reservation and sandbox payment records.
5. Order tracking, returns, refunds, notifications, and end-to-end accessibility/performance validation.

## Known limitations

- Product imagery is currently code-native illustrative artwork, not seller-uploaded object-storage media.
- Cart and favourites are intentionally in-memory until customer identity and commerce APIs are implemented.
- The filter control is a visual affordance; advanced filter-sheet behavior belongs to the catalogue integration work unit.
