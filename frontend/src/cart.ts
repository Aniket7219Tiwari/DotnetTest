import type { Product } from './types'

export interface CartLine {
  product: Product
  quantity: number
}

export function addToCart(cart: CartLine[], product: Product, quantity = 1): CartLine[] {
  if (quantity < 1 || !Number.isInteger(quantity)) return cart
  const current = cart.find(line => line.product.id === product.id)
  if (!current) return [...cart, { product, quantity }]
  return cart.map(line => line.product.id === product.id
    ? { ...line, quantity: Math.min(line.quantity + quantity, product.stockQuantity) }
    : line)
}
