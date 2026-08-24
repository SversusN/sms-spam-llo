import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ConfigProvider } from 'antd'
import ru_RU from 'antd/locale/ru_RU'
import dayjs from 'dayjs'
import 'dayjs/locale/ru'
import './index.css'
import App from './App.tsx'

dayjs.locale('ru')

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ConfigProvider locale={ru_RU}>
      <App />
    </ConfigProvider>
  </StrictMode>,
)
