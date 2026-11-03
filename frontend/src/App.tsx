import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { BrowserRouter, Link, Navigate, Route, Routes, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { authApi, clearTokens, getRole, saveTokens, storeApi } from './api'
import { addToCart, type CartLine } from './cart'
import type { Order, Product, TopCustomer } from './types'
import './App.css'

const cartKey = 'store.cart'
function readCart(): CartLine[] {
  try { return JSON.parse(localStorage.getItem(cartKey) ?? '[]') as CartLine[] }
  catch { return [] }
}
function money(value: number) { return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value) }
function errorText(error: unknown) { return error instanceof Error ? error.message : 'Something went wrong.' }

function Guard({ children, role }: { children: ReactNode; role?: 'Admin' | 'Customer' }) {
  const location = useLocation()
  const actual = getRole()
  if (!actual) return <Navigate to="/login" state={{ from: location.pathname }} replace />
  if (role && actual !== role) return <Navigate to="/" replace />
  return children
}

function Shell({ children }: { children: ReactNode }) {
  const role = getRole()
  const [cartCount, setCartCount] = useState(() => readCart().reduce((n, line) => n + line.quantity, 0))
  useEffect(() => {
    const update = () => setCartCount(readCart().reduce((n, line) => n + line.quantity, 0))
    window.addEventListener('storage', update)
    window.addEventListener('cartchange', update)
    return () => { window.removeEventListener('storage', update); window.removeEventListener('cartchange', update) }
  }, [])
  return <div className="app-shell">
    <header className="topbar">
      <Link to="/" className="brand">Northstar<span>Store</span></Link>
      <nav aria-label="Main navigation">
        <Link to="/">Shop</Link>
        {role === 'Customer' && <Link to="/orders">My orders</Link>}
        {role === 'Admin' && <Link to="/admin">Admin</Link>}
        <Link to="/cart">Cart <span className="cart-count">{cartCount}</span></Link>
        {role ? <button className="link-button" onClick={() => { clearTokens(); location.assign('/login') }}>Sign out</button> : <Link to="/login">Sign in</Link>}
      </nav>
    </header>
    <main>{children}</main>
    <footer>Northstar Store <span>Secure checkout · Inventory updated in real time</span></footer>
  </div>
}

function AuthPage({ register = false }: { register?: boolean }) {
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true); setError('')
    const data = new FormData(event.currentTarget)
    try {
      const tokens = register
        ? await authApi.register(String(data.get('name')), String(data.get('email')), String(data.get('password')))
        : await authApi.login(String(data.get('email')), String(data.get('password')))
      saveTokens(tokens)
      navigate((location.state as { from?: string } | null)?.from ?? (tokens.role === 'Admin' ? '/admin' : '/'), { replace: true })
    } catch (e) { setError(errorText(e)) }
    finally { setBusy(false) }
  }
  return <section className="auth-card">
    <p className="eyebrow">{register ? 'CREATE ACCOUNT' : 'WELCOME BACK'}</p>
    <h1>{register ? 'Join the store' : 'Sign in'}</h1>
    <p className="muted">{register ? 'Create your customer account to get started.' : 'Sign in to browse and manage your orders.'}</p>
    <form onSubmit={submit} className="form-stack">
      {register && <label>Full name<input name="name" autoComplete="name" required maxLength={200} /></label>}
      <label>Email<input name="email" type="email" autoComplete="email" required /></label>
      <label>Password<input name="password" type="password" minLength={8} autoComplete={register ? 'new-password' : 'current-password'} required /></label>
      {error && <div className="alert" role="alert">{error}</div>}
      <button className="primary" disabled={busy}>{busy ? 'Please wait…' : register ? 'Create account' : 'Sign in'}</button>
    </form>
    <p className="switch-auth">{register ? 'Already have an account?' : 'New here?'} <Link to={register ? '/login' : '/register'}>{register ? 'Sign in' : 'Create an account'}</Link></p>
  </section>
}

function Catalog({ onCartChange }: { onCartChange: () => void }) {
  const [params, setParams] = useSearchParams()
  const search = params.get('name') ?? ''
  const paramsKey = params.toString()
  const [searchText, setSearchText] = useState(search)
  const [minPrice, setMinPrice] = useState(params.get('minPrice') ?? '')
  const [maxPrice, setMaxPrice] = useState(params.get('maxPrice') ?? '')
  const inStock = params.get('inStock') === 'true'
  const page = Number(params.get('page') ?? 1)
  const [products, setProducts] = useState<Product[]>([])
  const [total, setTotal] = useState(0)
  const [loadedParamsKey, setLoadedParamsKey] = useState<string | null>(null)
  const loading = loadedParamsKey !== paramsKey
  const [error, setError] = useState('')
  useEffect(() => {
    const timer = window.setTimeout(() => {
      const next = new URLSearchParams(paramsKey)
      if (searchText) next.set('name', searchText)
      else next.delete('name')
      if (searchText !== search) next.delete('page')
      setParams(next, { replace: true })
    }, 300)
    return () => window.clearTimeout(timer)
  }, [searchText, search, paramsKey, setParams]) // debounce search input before syncing it into the shareable URL
  useEffect(() => {
    const currentParams = new URLSearchParams(paramsKey)
    const timer = window.setTimeout(() => {
      setSearchText(search)
      setMinPrice(currentParams.get('minPrice') ?? '')
      setMaxPrice(currentParams.get('maxPrice') ?? '')
    }, 0)
    return () => window.clearTimeout(timer)
  }, [search, paramsKey])
  useEffect(() => {
    const query = new URLSearchParams(paramsKey)
    query.set('pageSize', '9')
    let active = true
    storeApi.products(`?${query.toString()}`).then(result => {
      if (!active) return
      setProducts(result.items); setTotal(result.totalCount); setError(''); setLoadedParamsKey(paramsKey)
    }).catch(e => {
      if (!active) return
      setError(errorText(e)); setLoadedParamsKey(paramsKey)
    })
    return () => { active = false }
  }, [paramsKey])
  function applyFilters(nextMin: string, nextMax: string, stock: boolean) {
    const next = new URLSearchParams(params)
    if (nextMin) next.set('minPrice', nextMin)
    else next.delete('minPrice')
    if (nextMax) next.set('maxPrice', nextMax)
    else next.delete('maxPrice')
    if (stock) next.set('inStock', 'true')
    else next.delete('inStock')
    next.delete('page'); setParams(next)
  }
  function add(product: Product) {
    const cart = addToCart(readCart(), product)
    localStorage.setItem(cartKey, JSON.stringify(cart))
    window.dispatchEvent(new Event('cartchange'))
    onCartChange()
  }
  const pageCount = Math.max(1, Math.ceil(total / 9))
  return <section className="page">
    <div className="hero-row">
      <div><p className="eyebrow">CURATED FOR EVERY DAY</p><h1>Find your next favorite.</h1><p className="muted">Thoughtful essentials, delivered with care.</p></div>
      <div className="hero-art" aria-hidden="true">✦</div>
    </div>
    <div className="catalog-layout">
      <aside className="filters">
        <h2>Filter products</h2>
        <label>Search<input value={searchText} onChange={e => setSearchText(e.target.value)} placeholder="Search products…" /></label>
        <label>Min price<input type="number" min="0" value={minPrice} onChange={e => { setMinPrice(e.target.value); applyFilters(e.target.value, maxPrice, inStock) }} /></label>
        <label>Max price<input type="number" min="0" value={maxPrice} onChange={e => { setMaxPrice(e.target.value); applyFilters(minPrice, e.target.value, inStock) }} /></label>
        <label className="check"><input type="checkbox" checked={inStock} onChange={e => applyFilters(minPrice, maxPrice, e.target.checked)} /> In stock only</label>
      </aside>
      <div className="catalog-results">
        {loading ? <div className="state">Loading products…</div> : error ? <div className="alert">{error}</div> : products.length === 0 ? <div className="state">No products match these filters.</div> :
          <div className="product-grid">{products.map(product => <article className="product-card" key={product.id}>
            <div className="product-art"><span>{product.name.slice(0, 1)}</span><small>{product.sku}</small></div>
            <div className="product-info"><h3>{product.name}</h3><p className="muted">{product.stockQuantity > 0 ? `${product.stockQuantity} available` : 'Out of stock'}</p>
              <div className="product-bottom"><strong>{money(product.price)}</strong><button className="primary compact" disabled={!product.stockQuantity || !getRole()} onClick={() => add(product)}>{!getRole() ? 'Sign in to buy' : 'Add to cart'}</button></div>
            </div>
          </article>)}</div>}
        <div className="pager"><span>{total} items · Page {page} of {pageCount}</span><div><button disabled={page <= 1} onClick={() => { const p = new URLSearchParams(params); p.set('page', String(page - 1)); setParams(p) }}>Previous</button><button disabled={page >= pageCount} onClick={() => { const p = new URLSearchParams(params); p.set('page', String(page + 1)); setParams(p) }}>Next</button></div></div>
      </div>
    </div>
  </section>
}

function CartPage({ onCartChange }: { onCartChange: () => void }) {
  const [cart, setCart] = useState(readCart)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const busyRef = useRef(false)
  function update(next: CartLine[]) { setCart(next); localStorage.setItem(cartKey, JSON.stringify(next)); window.dispatchEvent(new Event('cartchange')); onCartChange() }
  async function checkout() {
    if (busyRef.current || cart.length === 0) return
    busyRef.current = true; setBusy(true); setError(''); setMessage('')
    const key = sessionStorage.getItem('store.checkoutKey') ?? crypto.randomUUID()
    sessionStorage.setItem('store.checkoutKey', key)
    try {
      const order = await storeApi.placeOrder(cart.map(line => ({ productId: line.product.id, quantity: line.quantity })), key)
      update([]); sessionStorage.removeItem('store.checkoutKey')
      setMessage(`Order #${order.id} placed. Your order is pending confirmation.`)
    } catch (e) { setError(errorText(e)) }
    finally { busyRef.current = false; setBusy(false) }
  }
  const total = cart.reduce((sum, line) => sum + line.product.price * line.quantity, 0)
  return <section className="page narrow"><p className="eyebrow">YOUR BAG</p><h1>Shopping cart</h1>
    {message && <div className="success" role="status">{message}</div>}
    {error && <div className="alert" role="alert">{error}</div>}
    {cart.length === 0 ? <div className="state">Your cart is empty. <Link to="/">Browse products</Link></div> : <>
      <div className="cart-lines">{cart.map(line => <div className="cart-line" key={line.product.id}>
        <div className="mini-art">{line.product.name.slice(0, 1)}</div><div className="cart-name"><b>{line.product.name}</b><span>{money(line.product.price)} each</span></div>
        <input aria-label={`Quantity for ${line.product.name}`} type="number" min="1" max={line.product.stockQuantity} value={line.quantity} onChange={e => update(cart.map(x => x.product.id === line.product.id ? { ...x, quantity: Math.max(1, Math.min(Number(e.target.value) || 1, line.product.stockQuantity)) } : x))} />
        <strong>{money(line.product.price * line.quantity)}</strong>
        <button className="link-button danger-text" onClick={() => update(cart.filter(x => x.product.id !== line.product.id))}>Remove</button>
      </div>)}</div>
      <div className="cart-summary"><div><span>Subtotal (discount applied by server)</span><strong>{money(total)}</strong></div><p>Final total may include server-calculated discounts at checkout.</p>
        {!getRole() ? <Link className="primary button-link" to="/login">Sign in to checkout</Link> : <button className="primary" disabled={busy} onClick={checkout}>{busy ? 'Placing order…' : 'Place order'}</button>}
      </div>
    </>}
  </section>
}

function OrderList({ admin = false }: { admin?: boolean }) {
  const [orders, setOrders] = useState<Order[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [working, setWorking] = useState<number | null>(null)
  async function load() {
    setLoading(true); setError('')
    try { const page = admin ? await storeApi.allOrders() : await storeApi.myOrders(); setOrders(page.items) }
    catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }
  useEffect(() => {
    let active = true
    const request = admin ? storeApi.allOrders() : storeApi.myOrders()
    request.then(result => { if (active) setOrders(result.items) })
      .catch(e => { if (active) setError(errorText(e)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [admin])
  async function changeStatus(order: Order, status: string) {
    setWorking(order.id); setError('')
    try { await storeApi.setStatus(order.id, status); await load() }
    catch (e) { setError(errorText(e)) } finally { setWorking(null) }
  }
  return <section className="page narrow"><p className="eyebrow">{admin ? 'FULFILLMENT' : 'ACCOUNT'}</p><h1>{admin ? 'All orders' : 'My orders'}</h1>
    {loading ? <div className="state">Loading orders…</div> : error ? <div className="alert" role="alert">{error}</div> : orders.length === 0 ? <div className="state">No orders yet. <Link to="/">Explore the catalog</Link></div> :
      <div className="order-list">{orders.map(order => <article className="order-card" key={order.id}>
        <div className="order-heading"><div><b>Order #{order.id}</b><span>{new Date(order.createdAt).toLocaleString()}</span></div><span className={`status status-${order.status.toLowerCase()}`}>{order.status}</span></div>
        <ul>{order.items.map((item, index) => <li key={`${item.productId}-${index}`}>{item.productName} × {item.quantity} <span>{money(item.unitPrice * item.quantity)}</span></li>)}</ul>
        <div className="order-foot"><span>Subtotal {money(order.subTotal)} · Discount {money(order.discount)}</span><strong>{money(order.total)}</strong></div>
        {order.status === 'Pending' && <button className="outline" disabled={working === order.id} onClick={() => changeStatus(order, 'Cancelled')}>Cancel order</button>}
        {admin && order.status === 'Pending' && <button className="primary compact" disabled={working === order.id} onClick={() => changeStatus(order, 'Confirmed')}>Confirm</button>}
        {admin && order.status === 'Confirmed' && <button className="primary compact" disabled={working === order.id} onClick={() => changeStatus(order, 'Shipped')}>Mark shipped</button>}
        {working === order.id && <span className="muted">Updating…</span>}
      </article>)}</div>}
  </section>
}

function AdminPage() {
  const [products, setProducts] = useState<Product[]>([])
  const [customers, setCustomers] = useState<TopCustomer[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState<Product | null>(null)
  const [notice, setNotice] = useState('')
  async function load() {
    setLoading(true); setError('')
    try {
      const [productPage, top] = await Promise.all([storeApi.products('?page=1&pageSize=100'), storeApi.topCustomers()])
      setProducts(productPage.items); setCustomers(top)
    } catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }
  useEffect(() => {
    let active = true
    Promise.all([storeApi.products('?page=1&pageSize=100'), storeApi.topCustomers()])
      .then(([productPage, top]) => { if (active) { setProducts(productPage.items); setCustomers(top) } })
      .catch(e => { if (active) setError(errorText(e)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setNotice(''); setError('')
    const form = new FormData(event.currentTarget)
    try {
      await storeApi.saveProduct({ id: editing?.id, sku: String(form.get('sku')), name: String(form.get('name')),
        price: Number(form.get('price')), stockQuantity: Number(form.get('stock')), isActive: true })
      setEditing(null); setNotice('Product saved.'); await load()
    } catch (e) { setError(errorText(e)) }
  }
  return <section className="page"><p className="eyebrow">STORE MANAGEMENT</p><h1>Admin dashboard</h1>
    {notice && <div className="success" role="status">{notice}</div>}{error && <div className="alert" role="alert">{error}</div>}
    <div className="admin-grid">
      <div className="panel"><div className="panel-heading"><div><p className="eyebrow">INVENTORY</p><h2>Products</h2></div><button className="primary compact" onClick={() => setEditing({ id: 0, sku: '', name: '', price: 0, stockQuantity: 0, isActive: true })}>Add product</button></div>
        {editing && <form className="product-form" onSubmit={save}><label>SKU<input name="sku" defaultValue={editing.sku} required maxLength={64} /></label><label>Name<input name="name" defaultValue={editing.name} required maxLength={200} /></label><label>Price<input name="price" type="number" min="0" step="0.01" defaultValue={editing.price} required /></label><label>Stock<input name="stock" type="number" min="0" defaultValue={editing.stockQuantity} required /></label><div><button className="primary compact">Save</button> <button type="button" className="outline" onClick={() => setEditing(null)}>Cancel</button></div></form>}
        {loading ? <p className="muted">Loading products…</p> : products.map(product => <div className="admin-product" key={product.id}><span><b>{product.name}</b><small>{product.sku} · {product.stockQuantity} in stock · {money(product.price)}</small></span><button className="outline compact" onClick={() => setEditing(product)}>Edit</button></div>)}
      </div>
      <div className="panel"><p className="eyebrow">LAST 30 DAYS</p><h2>Top customers</h2>
        {loading ? <p className="muted">Loading report…</p> : customers.length === 0 ? <p className="muted">No qualifying purchases in this period.</p> : <div className="table-scroll"><table><thead><tr><th>Customer</th><th>Email</th><th>Total spend</th></tr></thead><tbody>{customers.map((c: TopCustomer) => <tr key={c.customerId}><td>{c.fullName}</td><td>{c.email}</td><td>{money(c.totalSpend)}</td></tr>)}</tbody></table></div>}
      </div>
    </div>
    <OrderList admin />
  </section>
}

function AppRoutes() {
  const [, forceCartRender] = useState(0)
  const onCartChange = () => forceCartRender(v => v + 1)
  return <Shell><Routes>
    <Route path="/" element={<Catalog onCartChange={onCartChange} />} />
    <Route path="/login" element={getRole() ? <Navigate to="/" replace /> : <AuthPage />} />
    <Route path="/register" element={getRole() ? <Navigate to="/" replace /> : <AuthPage register />} />
    <Route path="/cart" element={<CartPage onCartChange={onCartChange} />} />
    <Route path="/orders" element={<Guard role="Customer"><OrderList /></Guard>} />
    <Route path="/admin" element={<Guard role="Admin"><AdminPage /></Guard>} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></Shell>
}

export default function App() {
  return <BrowserRouter><AppRoutes /></BrowserRouter>
}
