export type ProductArtwork =
  | 'basket'
  | 'chair'
  | 'coffee'
  | 'lamp'
  | 'shirt'
  | 'skincare'
  | 'food'
  | 'art'

export type Product = {
  id: number
  name: string
  seller: string
  province: string
  price: number
  previousPrice?: number
  rating: number
  category: string
  badge?: string
  artwork: ProductArtwork
  tone: string
}

export const categories = [
  { id: 'all', label: 'Explore all' },
  { id: 'home', label: 'Home & living' },
  { id: 'fashion', label: 'Fashion' },
  { id: 'beauty', label: 'Beauty' },
  { id: 'food', label: 'Food & pantry' },
  { id: 'art', label: 'Art & craft' },
]

export const products: Product[] = [
  {
    id: 1,
    name: 'Handwoven Storage Basket',
    seller: 'Ubuntu Weaves',
    province: 'KwaZulu-Natal',
    price: 420,
    rating: 4.9,
    category: 'home',
    badge: 'Community favourite',
    artwork: 'basket',
    tone: '#e8dfcf',
  },
  {
    id: 2,
    name: 'Forest Ceramic Mug',
    seller: 'Clay & Kin',
    province: 'Western Cape',
    price: 150,
    rating: 4.8,
    category: 'home',
    artwork: 'coffee',
    tone: '#d9e4de',
  },
  {
    id: 3,
    name: 'Modern Accent Chair',
    seller: 'Ndlovu Studio',
    province: 'Gauteng',
    price: 1280,
    previousPrice: 1450,
    rating: 4.7,
    category: 'home',
    badge: 'Made to order',
    artwork: 'chair',
    tone: '#ebe5dc',
  },
  {
    id: 4,
    name: 'Sculptural Table Lamp',
    seller: 'Langa Lightworks',
    province: 'Eastern Cape',
    price: 690,
    rating: 4.8,
    category: 'home',
    artwork: 'lamp',
    tone: '#eee7d8',
  },
  {
    id: 5,
    name: 'Linen Everyday Shirt',
    seller: 'Karoo Cloth Co.',
    province: 'Northern Cape',
    price: 540,
    rating: 4.6,
    category: 'fashion',
    artwork: 'shirt',
    tone: '#dce6e1',
  },
  {
    id: 6,
    name: 'Indigenous Body Oil',
    seller: 'Renew Botanics',
    province: 'Limpopo',
    price: 235,
    rating: 4.9,
    category: 'beauty',
    badge: 'Natural ingredients',
    artwork: 'skincare',
    tone: '#e7eadf',
  },
  {
    id: 7,
    name: 'Cape Spice Collection',
    seller: 'Table Pantry',
    province: 'Western Cape',
    price: 195,
    rating: 4.8,
    category: 'food',
    artwork: 'food',
    tone: '#efe0cf',
  },
  {
    id: 8,
    name: 'Textured Protea Print',
    seller: 'Bona Paper Studio',
    province: 'Free State',
    price: 360,
    rating: 4.7,
    category: 'art',
    artwork: 'art',
    tone: '#e9e2e7',
  },
]
