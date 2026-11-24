import { DeleteOutlined, PlusOutlined, ReloadOutlined, SettingOutlined } from '@ant-design/icons'
import {
  App as AntApp, Alert, Button, Empty, Flex, Layout, Menu, Popconfirm, Segmented, Spin, Tooltip, Typography,
} from 'antd'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  api, type Filters, type Meta, type Property, type Search, type SearchIn,
  type SourceRun, type ViewOptions, utc,
} from './api'
import FiltersBar from './components/FiltersBar'
import MapView from './components/MapView'
import PropertyCard from './components/PropertyCard'
import SearchForm from './components/SearchForm'
import { DEMO_READONLY, IS_DEMO } from './demo'
import { LANGS, useI18n, type Lang } from './i18n'

const REPO_URL = 'https://github.com/rem700/rent-radar'

const { Sider, Content } = Layout

export default function App() {
  const { message } = AntApp.useApp()
  const { t, plural, country, lang, setLang } = useI18n()
  const [meta, setMeta] = useState<Meta | null>(null)
  const [searches, setSearches] = useState<Search[]>([])
  const [activeId, setActiveId] = useState<number | null>(null)
  const [props, setProps] = useState<Property[]>([])
  const [runs, setRuns] = useState<SourceRun[]>([])
  const [loading, setLoading] = useState(false)
  const [filters, setFilters] = useState<Filters>({})
  const [view, setView] = useState<ViewOptions>({ sort: 'newest' })
  const [mode, setMode] = useState<'split' | 'list' | 'map'>('split')
  const [formOpen, setFormOpen] = useState<'new' | 'edit' | null>(null)
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [backendDown, setBackendDown] = useState(false)

  const active = useMemo(() => searches.find((s) => s.id === activeId) ?? null, [searches, activeId])
  const wasRunning = useRef(false)

  const loadSearches = useCallback(async () => {
    try {
      const list = await api.searches()
      setSearches(list)
      setBackendDown(false)
      setActiveId((cur) => cur ?? list[0]?.id ?? null)
    } catch {
      setBackendDown(true)
    }
  }, [])

  const loadProps = useCallback(async () => {
    if (!activeId) return
    setLoading(true)
    try {
      const [p, r] = await Promise.all([api.properties(activeId, filters, view), api.runs(activeId)])
      setProps(p)
      setRuns(r)
    } catch (e) {
      message.error(String(e))
    } finally {
      setLoading(false)
    }
  }, [activeId, filters, view, message])

  useEffect(() => {
    api.meta().then(setMeta).catch(() => setBackendDown(true))
    loadSearches()
  }, [loadSearches])

  const anyRunning = searches.some((s) => s.running)
  useEffect(() => {
    const t = setInterval(loadSearches, anyRunning ? 3000 : 30000)
    return () => clearInterval(t)
  }, [anyRunning, loadSearches])

  useEffect(() => {
    setFilters(active?.filters ?? {})
    setSelectedId(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId])

  useEffect(() => {
    const t = setTimeout(loadProps, 350)
    return () => clearTimeout(t)
  }, [loadProps])

  useEffect(() => {
    if (wasRunning.current && active && !active.running) loadProps()
    wasRunning.current = !!active?.running
  }, [active, loadProps])

  const submitSearch = async (v: SearchIn) => {
    try {
      if (formOpen === 'edit' && active) {
        await api.updateSearch(active.id, v)
        setFilters(v.filters)
        message.success(t('searchSaved'))
      } else {
        const s = await api.createSearch(v)
        setActiveId(s.id)
        message.success(t('searchCreated'))
      }
      setFormOpen(null)
      loadSearches()
    } catch (e) {
      message.error(e instanceof Error && e.message === DEMO_READONLY ? t('demoReadonly') : String(e))
    }
  }

  const toggle = async (p: Property, patch: { favorite?: boolean; hidden?: boolean }) => {
    setProps((list) => list.map((x) => (x.id === p.id ? { ...x, ...patch } : x)))
    await api.patchProperty(p.id, patch)
    if (patch.hidden && !view.show_hidden) setProps((list) => list.filter((x) => x.id !== p.id))
  }

  const runNow = async () => {
    if (!active) return
    await api.run(active.id)
    loadSearches()
  }

  const markViewed = async () => {
    if (!active) return
    await api.viewed(active.id)
    await loadSearches()
    loadProps()
  }

  const remove = async () => {
    if (!active) return
    await api.deleteSearch(active.id)
    setActiveId(null)
    setProps([])
    loadSearches()
  }

  const center: [number, number] | null =
    active?.city_lat != null && active.city_lon != null ? [active.city_lat, active.city_lon] : null

  const failed = runs.filter((r) => !r.ok && r.finished_at)  // идущий сейчас проход — не ошибка

  const list = props.length ? (
    <div className="sheet">
      {props.map((p) => (
        <PropertyCard key={p.id} p={p} selected={p.id === selectedId}
          onToggle={(patch) => toggle(p, patch)} onSelect={() => setSelectedId(p.id)} />
      ))}
    </div>
  ) : (
    <div className="sheet" style={{ padding: '48px 16px' }}>
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={active?.running ? t('collecting') : t('nothingFound')}
      />
    </div>
  )

  const map = (
    <MapView
      items={props}
      center={center}
      selectedId={selectedId}
      onSelect={(id) => {
        setSelectedId(id)
        document.getElementById(`prop-${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' })
      }}
    />
  )

  const status = active
    ? active.running
      ? t('updating')
      : active.last_run_at
        ? t('updatedAgo', { x: dayjs(utc(active.last_run_at)).fromNow() })
        : t('neverUpdated')
    : ''

  return (
    <Layout style={{ height: '100vh' }}>
      <Sider width={248} style={{ padding: '20px 12px', borderRight: '1px solid #DDE3DE' }}>
        <Flex vertical gap={16} style={{ height: '100%' }}>
          <Typography.Title level={5} style={{ margin: '0 8px', fontWeight: 600 }}>{t('appTitle')}</Typography.Title>
          <Button icon={<PlusOutlined />} onClick={() => setFormOpen('new')} block>{t('newSearch')}</Button>
          <Menu
            mode="inline"
            style={{ border: 0 }}
            selectedKeys={activeId ? [String(activeId)] : []}
            onClick={(e) => setActiveId(Number(e.key))}
            items={searches.map((s) => ({
              key: String(s.id),
              label: (
                <div className="side-item">
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }} title={s.name}>{s.name}</span>
                  {s.running ? <Spin size="small" />
                    : s.new_count > 0 ? <span className="side-new num">+{s.new_count}</span>
                    : <span className="side-count">{s.total}</span>}
                </div>
              ),
            }))}
          />
          <Segmented
            size="small"
            style={{ marginTop: 'auto', alignSelf: 'flex-start', marginInline: 8 }}
            value={lang}
            onChange={(v) => setLang(v as Lang)}
            options={LANGS}
            aria-label={t('language')}
          />
        </Flex>
      </Sider>

      <Layout>
        {IS_DEMO && (
          <Alert
            type="info"
            banner
            title={<span>{t('demoBanner')} <a href={REPO_URL} target="_blank" rel="noreferrer">{t('sourceCode')}</a></span>}
          />
        )}
        {backendDown && !IS_DEMO && (
          <Alert type="error" banner title={t('backendDown')} />
        )}
        {active ? (
          <Content style={{ padding: '20px 24px', display: 'flex', flexDirection: 'column', gap: 16, minHeight: 0 }}>
            <Flex justify="space-between" align="flex-start" wrap gap={12}>
              <div>
                <Typography.Title level={3} style={{ margin: 0, fontWeight: 600 }}>{active.name}</Typography.Title>
                <Typography.Text type="secondary">
                  {country(active.country)}, {active.city}. {active.total}{' '}
                  {plural('variants', active.total)}. {status}.
                </Typography.Text>
                {active.new_count > 0 && (
                  <Button type="link" size="small" onClick={markViewed} style={{ paddingInline: 6 }}>
                    {t('markViewed', { n: active.new_count })}
                  </Button>
                )}
                {failed.length > 0 && (
                  <div style={{ marginTop: 4 }}>
                    {failed.map((r) => (
                      <Tooltip key={r.source} title={<pre style={{ whiteSpace: 'pre-wrap', fontSize: 11, margin: 0 }}>{r.error}</pre>}>
                        <Typography.Text type="danger" style={{ fontSize: 13, marginRight: 12, cursor: 'help' }}>
                          {t('sourceFailed', { x: r.source })}
                        </Typography.Text>
                      </Tooltip>
                    ))}
                  </div>
                )}
              </div>
              <Flex gap={8} align="center">
                <Segmented
                  value={mode}
                  onChange={(v) => setMode(v as typeof mode)}
                  options={[
                    { value: 'split', label: t('modeSplit') },
                    { value: 'list', label: t('modeList') },
                    { value: 'map', label: t('modeMap') },
                  ]}
                />
                <Tooltip title={t('checkNow')}>
                  <Button icon={<ReloadOutlined />} loading={active.running} onClick={runNow} aria-label={t('refresh')} disabled={IS_DEMO} />
                </Tooltip>
                <Tooltip title={t('searchSettings')}>
                  <Button icon={<SettingOutlined />} onClick={() => setFormOpen('edit')} aria-label={t('searchSettings')} />
                </Tooltip>
                <Popconfirm title={t('deleteConfirm')} okText={t('delete')} cancelText={t('cancel')} onConfirm={remove} disabled={IS_DEMO}>
                  <Tooltip title={t('deleteSearch')}>
                    <Button icon={<DeleteOutlined />} aria-label={t('deleteSearch')} disabled={IS_DEMO} />
                  </Tooltip>
                </Popconfirm>
              </Flex>
            </Flex>

            <FiltersBar filters={filters} view={view} onFilters={setFilters} onView={setView}
              onReset={() => setFilters(active.filters ?? {})} />

            <Spin spinning={loading && !props.length} style={{ flex: 1 }}>
              {mode === 'list' && <div style={{ maxWidth: 860 }}>{list}</div>}
              {mode === 'map' && <div style={{ height: 'calc(100vh - 210px)' }}>{map}</div>}
              {mode === 'split' && (
                <div style={{ display: 'grid', gridTemplateColumns: 'minmax(380px, 5fr) 6fr', gap: 16, height: 'calc(100vh - 210px)' }}>
                  <div style={{ overflowY: 'auto', paddingRight: 2 }}>{list}</div>
                  {map}
                </div>
              )}
            </Spin>
          </Content>
        ) : (
          <Content style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Flex vertical align="center" gap={12} style={{ maxWidth: 360, textAlign: 'center' }}>
              <Typography.Title level={4} style={{ margin: 0 }}>{t('emptyTitle')}</Typography.Title>
              <Typography.Text type="secondary">{t('emptyText')}</Typography.Text>
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setFormOpen('new')}>{t('newSearch')}</Button>
            </Flex>
          </Content>
        )}
      </Layout>

      <SearchForm
        open={formOpen !== null}
        meta={meta}
        initial={formOpen === 'edit' ? active : null}
        onCancel={() => setFormOpen(null)}
        onSubmit={submitSearch}
      />
    </Layout>
  )
}
