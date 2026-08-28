# Customer and seller web implementation state

## Objective and phase

- Objective: deliver one responsive marketplace web application for customer and seller accounts.
- Application: `src/MzansiMarket.CustomerWeb`.
- Current phase: live API integration through backend work unit BE-006.
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

4. `FE-004 Seller onboarding and fulfilment` — PASS against available APIs
   - Full seller application, pending/draft status journey, approved-store authorization boundary, fulfilment queue, picking, packing, dispatch with carrier/tracking, and delivery transition.
   - Clear dependency cards identify unavailable seller catalogue, store administration, and analytics capabilities.

5. `FE-005 Quality checkpoint` — PASS locally
   - Production TypeScript/Vite build passes.
   - Vitest interaction suite passes 4/4.
   - Desktop browser inspection confirms the storefront and authentication sheet render without console warnings.
   - Mobile DOM inspection at 390×844 confirms document width remains within the viewport.

## Backend dependencies that prevent a truthful “entire system” frontend

- `BE-007`: cancellations, returns, refunds, customer order history, and refund status endpoints.
- `BE-008`: seller approval, store editing/publishing, product/category/image/price/stock/promotion administration, and staff role endpoints.
- `BE-009`: customer order tracking history, seller sales/stock/performance reporting, audit access, and export endpoints.
- Product administration or release seed data is needed before the live production catalogue can show sellable products. The current Render catalogue returns zero products.
- The sandbox payment initiation endpoint creates a pending provider reference. Only the protected server event endpoint can complete it; the frontend correctly does not receive that secret.

## Release configuration

- Static site: `mzansi-market-customer` (`srv-da8d5cajnfac73e1j7p0`).
- Production URL: `https://mzansi-market-customer.onrender.com`.
- Build command: `npm ci && npm run build`.
- Publish directory: `dist`.
- Optional override: `VITE_API_URL`; production defaults to the current Render API URL.

## Next dependency-ordered work

1. Implement BE-007 through BE-009 and their authorization tests.
2. Add the corresponding customer orders/returns and seller catalogue/reporting screens.
3. Seed or administer fictional catalogue inventory for end-to-end demonstration.
4. Run the cross-system BE-010 release checkpoint, including authenticated browser journeys and accessibility/performance auditing.
