import type { ThemeConfig } from 'antd'

// Спокойная палитра: туманно-зелёный фон, белая бумага, сосновый акцент
export const C = {
  mist: '#EEF1EE',
  paper: '#FFFFFF',
  ink: '#27302B',
  muted: '#6D7872',
  line: '#DDE3DE',
  pine: '#3F6F5E',
  pineSoft: '#E3ECE7',
  ochre: '#B8912F',
}

export const theme: ThemeConfig = {
  token: {
    colorPrimary: C.pine,
    colorInfo: C.pine,
    colorSuccess: '#4E8A6A',
    colorWarning: C.ochre,
    colorError: '#B5544B',
    colorText: C.ink,
    colorTextSecondary: C.muted,
    colorTextTertiary: '#8D978F',
    colorBorder: C.line,
    colorBorderSecondary: C.line,
    colorBgLayout: C.mist,
    colorBgContainer: C.paper,
    fontFamily: "'Onest', system-ui, -apple-system, 'Segoe UI', sans-serif",
    fontSize: 14,
    borderRadius: 8,
    controlHeight: 34,
    boxShadow: '0 6px 24px rgba(39, 48, 43, 0.08)',
    boxShadowSecondary: '0 6px 24px rgba(39, 48, 43, 0.08)',
  },
  components: {
    Layout: { siderBg: C.mist, headerBg: C.mist, bodyBg: C.mist },
    Menu: {
      itemBg: 'transparent',
      itemSelectedBg: C.paper,
      itemSelectedColor: C.ink,
      itemHoverBg: 'rgba(255,255,255,.6)',
      itemBorderRadius: 8,
      activeBarBorderWidth: 0,
    },
    Segmented: { itemSelectedBg: C.paper, trackBg: '#E2E7E3' },
    Button: { defaultShadow: 'none', primaryShadow: 'none' },
    Tag: { defaultBg: C.mist },
  },
}
