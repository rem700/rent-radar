import L from 'leaflet'
import { useEffect } from 'react'
import { CircleMarker, MapContainer, Popup, TileLayer, useMap } from 'react-leaflet'
import type { Property } from '../api'
import { useI18n } from '../i18n'

interface Props {
  items: Property[]
  center: [number, number] | null
  selectedId: number | null
  onSelect: (id: number) => void
}

function FitBounds({ items }: { items: Property[] }) {
  const map = useMap()
  useEffect(() => {
    const pts = items
      .filter((p) => p.main.lat != null && p.main.lon != null)
      .map((p) => [p.main.lat!, p.main.lon!] as [number, number])
    if (pts.length) map.fitBounds(L.latLngBounds(pts), { padding: [30, 30], maxZoom: 15 })
  }, [items, map])
  return null
}

export default function MapView({ items, center, selectedId, onSelect }: Props) {
  const { t, money: fmtMoney } = useI18n()
  const withGeo = items.filter((p) => p.main.lat != null && p.main.lon != null)
  const c = center ?? (withGeo[0] ? [withGeo[0].main.lat!, withGeo[0].main.lon!] : [47.4979, 19.0402])

  return (
    <div className="map-wrap">
      <MapContainer center={c as [number, number]} zoom={12} scrollWheelZoom>
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
          url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
          className="calm-tiles"
        />
        <FitBounds items={withGeo} />
        {withGeo.map((p) => {
          const l = p.main
          const sel = p.id === selectedId
          const color = p.favorite ? '#B8912F' : p.is_new ? '#2F5A4B' : '#6E9C89'
          return (
            <CircleMarker
              key={p.id}
              center={[l.lat!, l.lon!]}
              radius={sel ? 11 : 7}
              pathOptions={{ color: sel ? '#27302B' : '#fff', fillColor: color, fillOpacity: l.geo_precise ? 0.9 : 0.4, weight: sel ? 3 : 1.5 }}
              eventHandlers={{ click: () => onSelect(p.id) }}
            >
              <Popup>
                <div style={{ maxWidth: 230 }}>
                  {l.images[0] && <img src={l.images[0]} alt="" style={{ width: '100%', borderRadius: 6, marginBottom: 6 }} referrerPolicy="no-referrer" />}
                  <div style={{ fontWeight: 600, fontSize: 15 }}>{fmtMoney(l.price_eur ?? l.price, l.price_eur != null ? '€' : l.currency)}</div>
                  <div>{[l.rooms && t('roomsShort', { n: l.rooms }), l.area_m2 && `${l.area_m2} m²`].filter(Boolean).join(', ')}</div>
                  <div style={{ color: '#6D7872' }}>{l.address}</div>
                  {!l.geo_precise && <div style={{ color: '#8D978F', fontSize: 12 }}>{t('approxPlace')}</div>}
                  <a href={l.url} target="_blank" rel="noreferrer" style={{ color: '#3F6F5E' }}>{t('openOn', { x: l.source })}</a>
                </div>
              </Popup>
            </CircleMarker>
          )
        })}
      </MapContainer>
    </div>
  )
}
