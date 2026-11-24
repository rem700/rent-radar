// Демо-режим для GitHub Pages: бэкенда нет, данные берутся из public/demo-data.json,
// фильтры и сортировка считаются в браузере, изменения живут только в памяти вкладки.
import type { Filters, Meta, Property, Search, SourceRun, ViewOptions } from './api'

interface DemoData {
  meta: Meta
  searches: Search[]
  properties: Record<string, Property[]>
  runs: Record<string, SourceRun[]>
}

export const IS_DEMO = import.meta.env.VITE_DEMO === '1'
export const DEMO_READONLY = 'demoReadonly'

let cache: Promise<DemoData> | null = null
function data(): Promise<DemoData> {
  if (!cache) {
    const base = import.meta.env.BASE_URL
    cache = fetch(`${base}demo-data.json`)
      .then((r) => r.json())
      .then((d: DemoData) => {
        // картинки демо лежат рядом с сайтом — делаем пути абсолютными для подпапки GitHub Pages
        for (const list of Object.values(d.properties))
          for (const p of list)
            for (const l of [p.main, ...p.others]) l.images = l.images.map((u) => (/^https?:/.test(u) ? u : base + u))
        return d
      })
  }
  return cache
}

const viewedAt: Record<number, string> = {}

function matches(p: Property, f: Filters): boolean {
  const l = p.main
  const rng = (v: number | null, lo?: number | null, hi?: number | null) =>
    (lo == null && hi == null) || (v != null && (lo == null || v >= lo) && (hi == null || v <= hi))
  if (!rng(l.price_eur, f.price_min, f.price_max)) return false
  if (!rng(l.rooms, f.rooms_min, f.rooms_max)) return false
  if (!rng(l.area_m2, f.area_min, f.area_max)) return false
  const place = `${l.district ?? ''} ${l.address ?? ''}`.toLowerCase()
  if (f.district && !place.includes(f.district.toLowerCase())) return false
  const text = `${l.title} ${l.description ?? ''} ${l.address ?? ''}`.toLowerCase()
  const words = (s?: string | null) => (s ?? '').split(',').map((w) => w.trim().toLowerCase()).filter(Boolean)
  if (words(f.keywords).some((w) => !text.includes(w))) return false
  if (words(f.exclude).some((w) => text.includes(w))) return false
  if (f.pets === true && l.pets !== true) return false
  if (f.pets === false && l.pets === false) return false
  if (f.furnished != null && l.furnished != null && l.furnished !== f.furnished) return false
  return true
}

const SORTS: Record<string, (p: Property) => number> = {
  newest: (p) => -new Date(p.main.posted_at ?? p.main.first_seen).getTime(),
  price_asc: (p) => p.main.price_eur ?? 1e12,
  price_desc: (p) => -(p.main.price_eur ?? 0),
  area_desc: (p) => -(p.main.area_m2 ?? 0),
  eur_m2: (p) => (p.main.price_eur && p.main.area_m2 ? p.main.price_eur / p.main.area_m2 : 1e12),
}

const readonly = () => Promise.reject(new Error(DEMO_READONLY))

export const demoApi = {
  meta: async () => (await data()).meta,
  searches: async () => {
    const d = await data()
    return d.searches.map((s) => {
      const props = d.properties[s.id] ?? []
      return { ...s, last_viewed_at: viewedAt[s.id] ?? s.last_viewed_at, new_count: viewedAt[s.id] ? 0 : props.filter((p) => p.is_new).length }
    })
  },
  createSearch: readonly,
  updateSearch: readonly,
  deleteSearch: readonly,
  run: readonly,
  viewed: async (id: number) => {
    viewedAt[id] = new Date().toISOString()
    ;((await data()).properties[id] ?? []).forEach((p) => (p.is_new = false))
    return {}
  },
  runs: async (id: number) => (await data()).runs[id] ?? [],
  properties: async (id: number, f: Filters, v: ViewOptions) => {
    const list = ((await data()).properties[id] ?? []).filter(
      (p) =>
        (v.show_hidden || !p.hidden) &&
        (!v.only_favorites || p.favorite) &&
        (!v.only_new || p.is_new) &&
        (v.show_inactive || p.main.active) &&
        matches(p, f),
    )
    const key = SORTS[v.sort ?? 'newest'] ?? SORTS.newest
    return [...list].sort((a, b) => key(a) - key(b))
  },
  patchProperty: async (id: number, b: Partial<Pick<Property, 'favorite' | 'hidden' | 'note'>>) => {
    for (const list of Object.values((await data()).properties)) {
      const p = list.find((x) => x.id === id)
      if (p) Object.assign(p, b)
    }
    return {}
  },
}
