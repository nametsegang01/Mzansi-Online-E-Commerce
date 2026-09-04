export type TokenResponse = {
  tokenType: string
  accessToken: string
  expiresIn: number
  refreshToken: string
}

export type CustomerProfile = {
  firstName: string
  lastName: string
  mobileNumber: string | null
}

export type SellerProfile = {
  tradingName: string
  status: string
  storeName: string | null
  storeSlug: string | null
  storeStatus: string | null
}

export type CurrentUser = {
  userId: string
  email: string
  displayName: string
  accountStatus: string
  emailConfirmed: boolean
  roles: string[]
  customer: CustomerProfile | null
  seller: SellerProfile | null
}

export type RegistrationResponse = {
  userId: string
  email: string
  displayName: string
  roles: string[]
  sellerStatus: string | null
  storeSlug: string | null
}

export type Category = {
  id: string
  name: string
  slug: string
  parentCategoryId: string | null
  activeProductCount: number
}

export type Product = {
  id: string
  sku: string
  name: string
  slug: string
  price: number
  currency: string
  availableQuantity: number
  isInStock: boolean
  storeName: string
  storeSlug: string
  primaryImageUrl: string | null
  primaryImageAltText: string | null
}

export type PagedProducts = {
  items: Product[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export type Address = {
  id: string
  type: string
  recipientName: string
  line1: string
  line2: string | null
  city: string
  province: string
  postalCode: string
  countryCode: string
  isDefault: boolean
}

export type AddressInput = Omit<Address, 'id'>

export type CartItem = {
  id: string
  productId: string
  productName: string
  productSlug: string
  storeName: string
  storeSlug: string
  quantity: number
  unitPrice: number
  lineTotal: number
  availableQuantity: number
  isAvailable: boolean
  imageUrl: string | null
  imageAltText: string | null
}

export type Cart = {
  cartId: string | null
  items: CartItem[]
  itemCount: number
  subtotal: number
  currency: string
}

export type Checkout = {
  orderId: string
  orderNumber: string
  status: string
  subtotal: number
  discountTotal: number
  deliveryTotal: number
  grandTotal: number
  currency: string
  reservationExpiresAt: string
  promotionCode: string | null
}

export type Payment = {
  paymentId: string
  orderId: string
  provider: string
  providerReference: string
  paymentMethodType: string
  status: string
  amount: number
  currency: string
  paidAt: string | null
}

export type FulfilmentItem = {
  orderItemId: string
  productId: string
  sku: string
  productName: string
  quantity: number
}

export type FulfilmentOrder = {
  sellerOrderId: string
  orderId: string
  orderNumber: string
  storeId: string
  storeName: string
  status: string
  paidAt: string
  recipientName: string
  city: string
  province: string
  items: FulfilmentItem[]
  shipment: null | {
    shipmentId: string
    status: string
    carrier: string | null
    trackingNumber: string | null
    dispatchedAt: string | null
    deliveredAt: string | null
  }
}

export type SellerStore = {
  id: string
  name: string
  slug: string
  description: string | null
  supportEmail: string | null
  storeStatus: string
  sellerStatus: string
  canPublish: boolean
}

export type SellerProduct = {
  id: string
  sku: string
  name: string
  slug: string
  description: string | null
  price: number
  currency: string
  status: string
  onHandQuantity: number
  reservedQuantity: number
  availableQuantity: number
  reorderLevel: number
  categories: Array<{ id: string; name: string; slug: string }>
  imageUrl: string | null
  imageAltText: string | null
  updatedAt: string
}

export type SellerProductInput = {
  sku: string
  name: string
  slug: string
  description: string | null
  price: number
  categoryIds: string[]
  imageUrl: string | null
  imageAltText: string | null
  initialStock: number
  reorderLevel: number
}

export type ApiProblem = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}
