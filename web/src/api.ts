import { demoApi, IS_DEMO } from './demo'

export interface Filters {
  price_min?: number | null
  price_max?: number | null
  rooms_min?: number | null
  rooms_max?: number | null
  area_min?: number | null
  area_max?: number | null
  district?: string | null
  keywords?: string | null
  exclude?: string | null
  pets?: boolean | null
  furnished?: boolean | null
}

export interface Search {
  id: number
  name: string
  country: string
  city: string
  filters: Filters
  sources: string[] | null
  max_pages: number
  auto_update: boolean
  interval_minutes: number
  last_run_at: string | null
  last_viewed_at: string | null
  city_lat: number | null
  city_lon: number | null
  running: boolean
  total: number
  new_count: number
}

export interface SearchIn {
  name?: string
  country: string
  city: string
  filters: Filters
  sources: string[] | null
  max_pages: number
  auto_update: boolean
  interval_minutes: number
}

export interface Listing {
  id: number
  source: string
  url: string
  title: string
  description: string | null
  price: number | null
  currency: string | null
  price_eur: number | null
  rooms: number | null
  area_m2: number | null
  floor: string | null
  address: string | null
  district: string | null
  lat: number | null
  lon: number | null
  geo_precise: boolean
  pets: boolean | null
  furnished: boolean | null
  images: string[]
  posted_at: string | null
  first_seen: string
  active: boolean
  price_history: { at: string; price: number }[]
}

export interface Property {
  id: number
  favorite: boolean
  hidden: boolean
  note: string | null
  is_new: boolean
  main: Listing
  others: Listing[]
}

export interface Adapter {
  name: string
  title: string
  countries: string[]
  homepage: string
}

export interface Meta {
  countries: Record<string, string[]>
  adapters: Adapter[]
  currency: { source: string; rates: Record<string, number> }
}

export interface SourceRun {
  source: string
  ok: boolean
  found: number
  new: number
  error: string | null
  started_at: string
  finished_at: string | null
}

export interface ViewOptions {
  only_favorites?: boolean
  only_new?: boolean
  show_hidden?: boolean
  show_inactive?: boolean
  sort?: string
}

async function req<T>(url: string, init?: RequestInit): Promise<T> {
  const r = await fetch(url, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init?.headers || {}) },
  })
  if (!r.ok) {
    let msg = `${r.status} ${r.statusText}`
    try {
      const j = await r.json()
      msg = typeof j.detail === 'string' ? j.detail : JSON.stringify(j.detail)
    } catch {
      /* ignore */
    }
    throw new Error(msg)
  }
  return r.json()
}

function qs(obj: Record<string, unknown>): string {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(obj)) {
    if (v !== undefined && v !== null && v !== '' && v !== false) p.set(k, String(v))
  }
  return p.toString()
}

const realApi = {
  meta: () => req<Meta>('/api/meta'),
  searches: () => req<Search[]>('/api/searches'),
  createSearch: (b: SearchIn) => req<Search>('/api/searches', { method: 'POST', body: JSON.stringify(b) }),
  updateSearch: (id: number, b: SearchIn) =>
    req<Search>(`/api/searches/${id}`, { method: 'PUT', body: JSON.stringify(b) }),
  deleteSearch: (id: number) => req(`/api/searches/${id}`, { method: 'DELETE' }),
  run: (id: number) => req<{ status: string }>(`/api/searches/${id}/run`, { method: 'POST' }),
  viewed: (id: number) => req(`/api/searches/${id}/viewed`, { method: 'POST' }),
  runs: (id: number) => req<SourceRun[]>(`/api/searches/${id}/runs`),
  properties: (id: number, f: Filters, v: ViewOptions) =>
    req<Property[]>(`/api/searches/${id}/properties?${qs({ ...f, ...v })}`),
  patchProperty: (id: number, b: Partial<Pick<Property, 'favorite' | 'hidden' | 'note'>>) =>
    req(`/api/properties/${id}`, { method: 'PATCH', body: JSON.stringify(b) }),
}

export const api: typeof realApi = IS_DEMO ? (demoApi as unknown as typeof realApi) : realApi

/** Server timestamps are UTC; older demo data has them without a zone designator. */
export function utc(s: string): string {
  return /([zZ]|[+-]\d\d:?\d\d)$/.test(s) ? s : s + 'Z'
}

export function fmtMoney(v: number | null, cur: string | null): string {
  if (v == null) return '—'
  return `${Math.round(v).toLocaleString('ru-RU')} ${cur ?? ''}`.trim()
}
