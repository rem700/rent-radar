import { EyeInvisibleOutlined, EyeOutlined, StarFilled, StarOutlined } from '@ant-design/icons'
import { Tooltip } from 'antd'
import dayjs from 'dayjs'
import { type Property, utc } from '../api'
import { useI18n } from '../i18n'

interface Props {
  p: Property
  selected?: boolean
  onToggle: (patch: { favorite?: boolean; hidden?: boolean }) => void
  onSelect?: () => void
}

export default function PropertyCard({ p, selected, onToggle, onSelect }: Props) {
  const { t, money: fmtMoney } = useI18n()
  const l = p.main
  const all = [l, ...p.others]
  const img = l.images[0] ?? p.others.find((o) => o.images.length)?.images[0]
  const perM2 = l.price_eur && l.area_m2 ? Math.round(l.price_eur / l.area_m2) : null
  const lastChange = l.price_history.at(-1)
  const eurMain = l.currency !== 'EUR' && l.price_eur != null
  const facts = [
    l.rooms != null && t('roomsShort', { n: l.rooms }),
    l.area_m2 != null && `${l.area_m2} m²`,
    perM2 && `${perM2} €/m²`,
    l.floor && t('floorN', { x: l.floor }),
  ].filter(Boolean)
  const traits = [
    l.pets === true && t('petsOk'),
    l.pets === false && t('petsNotOk'),
    l.furnished === true && t('furnished'),
    !l.active && t('inactive'),
  ].filter(Boolean)

  return (
    <div
      id={`prop-${p.id}`}
      className={`row${selected ? ' selected' : ''}${p.hidden ? ' dim' : ''}`}
      onClick={onSelect}
    >
      {img ? (
        <img className="row-img" src={img} alt="" loading="lazy" referrerPolicy="no-referrer" />
      ) : (
        <div className="row-img empty">{t('noPhoto')}</div>
      )}

      <div style={{ minWidth: 0 }}>
        <div className="row-price num">
          {eurMain ? fmtMoney(l.price_eur, '€') : fmtMoney(l.price, l.currency)}
          {eurMain && <span className="row-local">{fmtMoney(l.price, l.currency)}</span>}
        </div>
        <div className="row-facts num">{facts.join(', ')}</div>
        <div className="row-addr" title={l.title}>{l.address || l.district || l.title}</div>
        <div className="row-meta">
          {p.is_new && <span className="row-new">{t('isNew')}</span>}
          {all.map((x) => (
            <a key={x.id} href={x.url} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>
              {x.source}{x !== l && x.price_eur ? ` ${fmtMoney(x.price_eur, '€')}` : ''}
            </a>
          ))}
          {traits.map((t) => <span key={t as string}>{t}</span>)}
          {lastChange && (
            <Tooltip title={`${dayjs(lastChange.at).format('D MMM')}: ${fmtMoney(lastChange.price, l.currency)}`}>
              <span>{lastChange.price > (l.price ?? 0) ? t('priceDown') : t('priceUp')}</span>
            </Tooltip>
          )}
          <span>{l.posted_at ? dayjs(l.posted_at).fromNow() : t('foundAgo', { x: dayjs(utc(l.first_seen)).fromNow() })}</span>
        </div>
      </div>

      <div className="row-actions">
        <Tooltip title={p.favorite ? t('favRemove') : t('favAdd')} placement="left">
          <button
            className={`icon-btn${p.favorite ? ' on' : ''}`}
            aria-label={t('favorite')}
            onClick={(e) => { e.stopPropagation(); onToggle({ favorite: !p.favorite }) }}
          >
            {p.favorite ? <StarFilled /> : <StarOutlined />}
          </button>
        </Tooltip>
        <Tooltip title={p.hidden ? t('unhide') : t('hide')} placement="left">
          <button
            className="icon-btn"
            aria-label={t('hide')}
            onClick={(e) => { e.stopPropagation(); onToggle({ hidden: !p.hidden }) }}
          >
            {p.hidden ? <EyeOutlined /> : <EyeInvisibleOutlined />}
          </button>
        </Tooltip>
      </div>
    </div>
  )
}
