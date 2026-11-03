export type Role = 'Admin' | 'Customer'
export type OrderStatus = 'Pending' | 'Confirmed' | 'Shipped' | 'Cancelled'

export interface TokenResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  role: Role
}

export interface Product {
  id: number
  sku: string
  name: string
  price: number
  stockQuantity: number
  isActive: boolean
}

export interface OrderItem {
  productId: number
  productName: string
  quantity: number
  unitPrice: number
}

export interface Order {
  id: number
  customerId: number
  status: OrderStatus
  subTotal: number
  discount: number
  total: number
  createdAt: string
  items: OrderItem[]
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface TopCustomer {
  customerId: number
  fullName: string
  email: string
  totalSpend: number
}
