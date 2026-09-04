# Customer and seller web implementation state

## Objective and phase

- Objective: deliver one responsive marketplace web application for customer and seller accounts.
- Application: `src/MzansiMarket.CustomerWeb`.
- Current phase: reseller catalogue integration through backend work unit BE-008A.
- Production API default: `https://mzansi-market-api.onrender.com`.

## Architecture and decisions

- React 19, TypeScript 6, and Vite 8 remain the frontend foundation.
- A typed API client now owns all HTTP contracts, bearer-session persistence, one-at-a-time token refresh, API problem parsing, and a bounded 30-second request timeout.
- Authentication tokens are kept in session storage rather than long-lived local storage. Logout-all is exposed by the client for later account-security UI expansion.
- One role-aware application shell serves customers and sellers. Seller registration also creates the customer profile required by the backend.
- Product, stock, cart, price, promotion, delivery, checkout, payment, and fulfilment state comes only from the ASP.NET API. The former mock catalogue is no longer used by the application.
- Translucency remains limited to navigation and modal layers. Content and transactional surfaces use opaque cards with clear focus treatment.
- Sheets restore focus, close with Escape, contain keyboard focus, and lock background scrolling. Reduced-motion and reduced-transparency fallbacks remain active.

## Completed frontend work units

1. `FE-001 API and session foundation` — PASS
   - Typed contracts for identity, catalogue, addresses, cart, checkout, sandbox payment, and fulfilment.
   - Automatic refresh-token exchange after a 401, session clearing on refresh failure, and normalized API errors.
   - Loading, empty, retry, busy, disabled, and live-region feedback states.

2. `FE-002 Customer identity and account` — PASS
   - Customer registration, seller registration, login, session restoration, sign-out, role-aware navigation, and account summary.
   - Address list, create, update, default selection, and recoverable delete confirmation.

3. `FE-003 Live shopping and checkout` — PASS against available APIs
   - Live categories and products, debounced server search, category, stock, and sort filters.
   - Auth-gated cart add, quantity update, removal, current server totals, address selection, promotion input, idempotent checkout, order reservation summary, and sandbox payment initiation.
   - The browser never collects card numbers or provider secrets. Payment completion remains a server/provider event.

4. `FE-004 Reseller studio` — PASS locally and deployed on Render
   - Full reseller application, pending/draft preparation, approval visibility, and active-store publication boundary.
   - Store profile editing; product create/edit/archive; categories; public HTTPS image and alt-text metadata; price; stock and reorder-level adjustments; publish/unpublish; and customer-catalogue visibility.
   - Existing fulfilment queue remains integrated for picking, packing, dispatch with carrier/tracking, and delivery transition.
   - Desktop and mobile reseller workspace navigation uses Products, Orders, and Store settings with responsive product cards and focus-contained task sheets.

5. `FE-005 Quality checkpoint` — PASS locally
   - Production TypeScript/Vite build passes.
   - Vitest interaction suite passes 6/6, including reseller draft creation and publication.
   - Desktop browser inspection confirms the storefront and authentication sheet render without console warnings.
   - Mobile DOM inspection at 390×844 confirms document width remains within the viewport.

## Backend dependencies that prevent a truthful “entire system” frontend

- `BE-007`: cancellations, returns, refunds, customer order history, and refund status endpoints.
- Remaining `BE-008`: staff category/promotion/role administration and direct object-storage image uploads. Reseller approval, store editing, owned product/image metadata/price/stock management, and publication are implemented in BE-008A.
- `BE-009`: customer order tracking history, seller sales/stock/performance reporting, audit access, and export endpoints.
- DATA-005 supplies the base categories. The production catalogue will remain empty until an approved reseller publishes its first product.
- The sandbox payment initiation endpoint creates a pending provider reference. Only the protected server event endpoint can complete it; the frontend correctly does not receive that secret.

## Release configuration

- Static site: `mzansi-market-customer` (`srv-da8d5cajnfac73e1j7p0`).
- Production URL: `https://mzansi-market-customer.onrender.com`.
- Build command: `npm ci && npm run build`.
- Publish directory: `dist`.
- Optional override: `VITE_API_URL`; production defaults to the current Render API URL.
- Live source release: `bd9a58b` (`Integrate customer and seller web journeys`).
- Render deploy: `dep-da8ne8qjnfac73dsk7lg`, status `live` on 2026-08-28.
- CDN verification: root HTTP request succeeded and the deployed JavaScript bundle contains the seller-studio and live-stock application paths.
- Reseller release: source `bd59c46`, Render deploy `dep-dad6ltoae00c73djg1vg`, status `live` on 2026-09-04.
- Reseller CDN verification: deployed bundle contains `Reseller studio` and `Create draft product`; post-deploy error-log scan returned no errors.

## Next dependency-ordered work

1. Implement BE-007, the remaining BE-008 staff tools, and BE-009 with authorization tests.
2. Add customer orders/returns and reseller reporting screens plus direct object-storage image upload.
3. Run the cross-system BE-010 release checkpoint, including authenticated browser journeys and accessibility/performance auditing.
