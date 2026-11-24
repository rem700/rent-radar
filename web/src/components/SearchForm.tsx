import { Checkbox, Col, Form, Input, InputNumber, Modal, Row, Select, Switch, Typography } from 'antd'
import { useEffect, useMemo } from 'react'
import type { Meta, Search, SearchIn } from '../api'
import { useI18n } from '../i18n'

interface Props {
  open: boolean
  meta: Meta | null
  initial?: Search | null
  onCancel: () => void
  onSubmit: (v: SearchIn) => Promise<void>
}

export default function SearchForm({ open, meta, initial, onCancel, onSubmit }: Props) {
  const [form] = Form.useForm()
  const { t, country: countryName } = useI18n()
  const country: string | undefined = Form.useWatch('country', form)

  const countryOptions = useMemo(() => {
    const codes = Object.keys(meta?.countries ?? {})
    return [
      ...codes.map((c) => ({ value: c, label: `${countryName(c)} (${c})` })),
      { value: 'XX', label: t('otherCountry') },
    ]
  }, [meta, t, countryName])

  const sourceOptions = useMemo(
    () =>
      (meta?.adapters ?? [])
        .filter((a) => a.countries.includes('*') || (country && a.countries.includes(country)))
        .map((a) => ({ value: a.name, label: a.title })),
    [meta, country],
  )

  useEffect(() => {
    if (!open) return
    form.resetFields()
    if (initial) {
      const all = (meta?.adapters ?? [])
        .filter((a) => a.name !== 'demo' && a.countries.includes(initial.country))
        .map((a) => a.name)
      form.setFieldsValue({ ...initial, sources: initial.sources ?? all, filters: initial.filters ?? {} })
    }
  }, [open, initial, form, meta])

  useEffect(() => {
    // при смене страны — по умолчанию все настоящие источники
    if (!open || !country || initial?.country === country) return
    const real = sourceOptions.filter((o) => o.value !== 'demo').map((o) => o.value)
    form.setFieldValue('sources', real.length ? real : ['demo'])
  }, [country, open, initial, sourceOptions, form])

  return (
    <Modal
      open={open}
      title={initial ? t('searchSettings') : t('newSearch')}
      okText={initial ? t('save') : t('createRun')}
      cancelText={t('cancel')}
      onCancel={onCancel}
      onOk={async () => {
        const v = await form.validateFields()
        // все настоящие сайты отмечены -> null: тогда новые сайты подключатся к поиску сами
        const real = sourceOptions.filter((o) => o.value !== 'demo').map((o) => o.value)
        const picked: string[] = v.sources ?? []
        const allReal = real.length > 0 && real.every((s) => picked.includes(s)) && !picked.includes('demo')
        await onSubmit({
          ...v,
          sources: allReal ? null : picked,
          country: v.country === 'XX' ? (v.country_code || 'XX') : v.country,
        })
      }}
      width={640}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{ country: 'HU', city: 'Budapest', max_pages: 3, auto_update: true, interval_minutes: 60, filters: {} }}
      >
        <Row gutter={12}>
          <Col span={10}>
            <Form.Item name="country" label={t('country')} rules={[{ required: true, message: t('chooseCountry') }]}>
              <Select options={countryOptions} placeholder={t('country')} />
            </Form.Item>
          </Col>
          <Col span={14}>
            <Form.Item name="city" label={t('city')} rules={[{ required: true, message: t('enterCity') }]}>
              <Input placeholder={t('cityPh')} />
            </Form.Item>
          </Col>
        </Row>
        {country === 'XX' && (
          <Form.Item name="country_code" label={t('isoCode')} rules={[{ len: 2 }]}>
            <Input maxLength={2} placeholder="DE" />
          </Form.Item>
        )}
        <Form.Item name="name" label={t('nameOpt')}>
          <Input placeholder={t('namePh')} />
        </Form.Item>
        <Form.Item name="sources" label={t('sources')}>
          <Checkbox.Group options={sourceOptions} />
        </Form.Item>

        <Typography.Text strong>{t('defaultFilters')}</Typography.Text>
        <Row gutter={12} style={{ marginTop: 8 }}>
          <Col span={12}>
            <Form.Item label={t('pricePerMonth')}>
              <Row gutter={6}>
                <Col span={12}><Form.Item name={['filters', 'price_min']} noStyle><InputNumber min={0} placeholder={t('from')} style={{ width: '100%' }} /></Form.Item></Col>
                <Col span={12}><Form.Item name={['filters', 'price_max']} noStyle><InputNumber min={0} placeholder={t('to')} style={{ width: '100%' }} /></Form.Item></Col>
              </Row>
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item label={t('rooms')}>
              <Row gutter={6}>
                <Col span={12}><Form.Item name={['filters', 'rooms_min']} noStyle><InputNumber min={0} step={0.5} placeholder={t('from')} style={{ width: '100%' }} /></Form.Item></Col>
                <Col span={12}><Form.Item name={['filters', 'rooms_max']} noStyle><InputNumber min={0} step={0.5} placeholder={t('to')} style={{ width: '100%' }} /></Form.Item></Col>
              </Row>
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item label={t('area')}>
              <Row gutter={6}>
                <Col span={12}><Form.Item name={['filters', 'area_min']} noStyle><InputNumber min={0} placeholder={t('from')} style={{ width: '100%' }} /></Form.Item></Col>
                <Col span={12}><Form.Item name={['filters', 'area_max']} noStyle><InputNumber min={0} placeholder={t('to')} style={{ width: '100%' }} /></Form.Item></Col>
              </Row>
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name={['filters', 'district']} label={t('districtPart')}>
              <Input placeholder="XIV. kerület" />
            </Form.Item>
          </Col>
        </Row>

        <Typography.Text strong>{t('updates')}</Typography.Text>
        <Row gutter={12} style={{ marginTop: 8 }}>
          <Col span={8}>
            <Form.Item name="auto_update" label={t('autoUpdate')} valuePropName="checked">
              <Switch />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item name="interval_minutes" label={t('everyMin')}>
              <InputNumber min={10} max={1440} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item name="max_pages" label={t('pagesPerSite')} tooltip={t('pagesHint')}>
              <InputNumber min={1} max={20} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </Modal>
  )
}
