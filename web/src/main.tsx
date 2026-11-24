import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App as AntApp } from 'antd'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import 'leaflet/dist/leaflet.css'
import './index.css'
import App from './App.tsx'
import { I18nProvider } from './i18n'

dayjs.extend(relativeTime)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <AntApp>
        <App />
      </AntApp>
    </I18nProvider>
  </StrictMode>,
)
