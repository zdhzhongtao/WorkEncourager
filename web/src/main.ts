import './styles/global.css'
import { renderApp } from './app'

const root = document.getElementById('app')
if (root) renderApp(root)

// PWA：只有走 http(s) 才注册 Service Worker；file:// 双击打开本来就没网，不需要
if ('serviceWorker' in navigator && location.protocol.startsWith('http')) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('./sw.js', { scope: './' }).catch(() => {
      /* 注册失败不影响使用 */
    })
  })
}
