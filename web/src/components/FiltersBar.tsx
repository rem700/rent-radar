import { FilterOutlined } from '@ant-design/icons'
import { Badge, Button, Drawer, Flex, Input, InputNumber, Select, Space, Switch, Typography } from 'antd'
import { useState } from 'react'
import type { Filters, ViewOptions } from '../api'
import { useI18n } from '../i18n'

interface Props {
  filters: Filters
  view: ViewOptions
  onFilters: (f: Filters) => void
  onView: (v: ViewOptions) => void
  onReset: () => void
}

const tri = (v: boolean | null | undefined) => (v === true ? 'yes' : v === false ? 'no' : 'any')
const untri = (s: string) => (s === 'yes' ? true : s === 'no' ? false : null)

export default function FiltersBar({ filters, view, onFilters, onView, onReset }: Props) {
  const [open, setOpen] = useState(false)
  const { t } = useI18n()
  const set = (patch: Filters) => onFilters({ ...filters, ...patch })

  const range = (lo: keyof Filters, hi: keyof Filters, unit: string, step = 1, w = 76) => (
    <Space size={6}>
    <span style={{ color: '#6D7872' }}>{unit}</span>
    <Space.Compact>
      <InputNumber
        min={0} step={step} placeholder={t('from')} style={{ width: w }}
        value={filters[lo] as number | null | undefined}
        onChange={(v) => set({ [lo]: v })}
      />
      <InputNumber
        min={0} step={step} placeholder={t('to')} style={{ width: w }}
        value={filters[hi] as number | null | undefined}
        onChange={(v) => set({ [hi]: v })}
      />
    </Space.Compact>
    </Space>
  )

  const extraCount = [
    filters.district, filters.keywords, filters.exclude,
    filters.pets != null ? 1 : null, filters.furnished != null ? 1 : null,
    view.only_new, view.only_favorites, view.show_hidden, view.show_inactive,
  ].filter(Boolean).length

  return (
    <Flex wrap gap={8} align="center">
      {range('price_min', 'price_max', t('price'), 50, 84)}
      {range('rooms_min', 'rooms_max', t('rooms'), 0.5, 64)}
      {range('area_min', 'area_max', t('area'), 5, 70)}
      <Badge count={extraCount} size="small" color="#3F6F5E">
        <Button icon={<FilterOutlined />} onClick={() => setOpen(true)}>{t('moreFilters')}</Button>
      </Badge>
      <Select
        value={view.sort ?? 'newest'}
        onChange={(v) => onView({ ...view, sort: v })}
        style={{ width: 190 }}
        variant="borderless"
        options={[
          { value: 'newest', label: t('sortNewest') },
          { value: 'price_asc', label: t('sortCheap') },
          { value: 'price_desc', label: t('sortExpensive') },
          { value: 'area_desc', label: t('sortArea') },
          { value: 'eur_m2', label: t('sortPerM2') },
        ]}
      />

      <Drawer
        title={t('filters')}
        open={open}
        onClose={() => setOpen(false)}
        size={360}
        footer={
          <Flex justify="space-between">
            <Button type="text" onClick={onReset}>{t('resetFilters')}</Button>
            <Button type="primary" onClick={() => setOpen(false)}>{t('show')}</Button>
          </Flex>
        }
      >
        <Flex vertical gap={18}>
          <Field label={t('district')}>
            <Input allowClear placeholder={t('districtPh')} value={filters.district ?? ''}
              onChange={(e) => set({ district: e.target.value || null })} />
          </Field>
          <Field label={t('keywords')} hint={t('keywordsHint')}>
            <Input allowClear placeholder={t('keywordsPh')} value={filters.keywords ?? ''}
              onChange={(e) => set({ keywords: e.target.value || null })} />
          </Field>
          <Field label={t('exclude')} hint={t('excludeHint')}>
            <Input allowClear placeholder={t('excludePh')} value={filters.exclude ?? ''}
              onChange={(e) => set({ exclude: e.target.value || null })} />
          </Field>
          <Field label={t('pets')}>
            <Select style={{ width: '100%' }} value={tri(filters.pets)} onChange={(v) => set({ pets: untri(v) })}
              options={[
                { value: 'any', label: t('any') },
                { value: 'yes', label: t('petsYes') },
                { value: 'no', label: t('petsNo') },
              ]} />
          </Field>
          <Field label={t('furniture')}>
            <Select style={{ width: '100%' }} value={tri(filters.furnished)} onChange={(v) => set({ furnished: untri(v) })}
              options={[
                { value: 'any', label: t('any') },
                { value: 'yes', label: t('furnYes') },
                { value: 'no', label: t('furnNo') },
              ]} />
          </Field>
          <Flex vertical gap={10}>
            <Toggle label={t('onlyNew')} v={!!view.only_new} on={(v) => onView({ ...view, only_new: v })} />
            <Toggle label={t('onlyFav')} v={!!view.only_favorites} on={(v) => onView({ ...view, only_favorites: v })} />
            <Toggle label={t('showHidden')} v={!!view.show_hidden} on={(v) => onView({ ...view, show_hidden: v })} />
            <Toggle label={t('showInactive')} v={!!view.show_inactive} on={(v) => onView({ ...view, show_inactive: v })} />
          </Flex>
        </Flex>
      </Drawer>
    </Flex>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <div style={{ marginBottom: 6 }}>{label}</div>
      {children}
      {hint && <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginTop: 4 }}>{hint}</Typography.Text>}
    </div>
  )
}

function Toggle({ label, v, on }: { label: string; v: boolean; on: (v: boolean) => void }) {
  return (
    <Flex justify="space-between" align="center">
      <span>{label}</span>
      <Switch size="small" checked={v} onChange={on} />
    </Flex>
  )
}
