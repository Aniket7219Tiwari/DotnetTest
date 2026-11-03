import { describe, expect, it } from 'vitest'
import { addToCart } from './cart'
import type { Product } from './types'

const mug: Product = {
  id: 1, sku: 'MUG-1', name: 'Stoneware mug', price: 600, stockQuantity: 3, isActive: true,
}

describe('addToCart', () => {
  it('adds a new item without mutating the existing cart', () => {
    const empty: ReturnType<typeof addToCart> = []
    const updated = addToCart(empty, mug)
    expect(updated).toEqual([{ product: mug, quantity: 1 }])
    expect(empty).toHaveLength(0)
  })

  it('combines quantities but never exceeds available stock', () => {
    const cart = addToCart([{ product: mug, quantity: 2 }], mug, 5)
    expect(cart).toEqual([{ product: mug, quantity: 3 }])
  })
})
