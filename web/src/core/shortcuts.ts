import { el } from './dom'

export const SHORTCUTS: Array<[string, string]> = [
  ['Ctrl / ⌘ + K', '聚焦搜索框'],
  ['Ctrl / ⌘ + Enter', '输入框里立即执行一次'],
  ['?', '打开这个面板'],
  ['Esc', '关闭弹窗，或清空搜索'],
  ['Tab / Shift + Tab', '在输入框里缩进（工具内已接管）'],
  ['拖拽文件到窗口', '丢给当前工具处理'],
  ['#/t/<工具id>?src=…', '深链：把内容直接带进工具'],
]

let host: HTMLElement | null = null

export function openShortcuts(): void {
  if (host) {
    host.remove()
    host = null
    return
  }
  const panel = el(
    'div',
    { class: 'modal-card' },
    el('div', { class: 'modal-head' }, el('strong', { text: '快捷键' }), el('span', { class: 'spacer' })),
    el(
      'div',
      { class: 'shortcut-list' },
      ...SHORTCUTS.map(([k, v]) =>
        el('div', { class: 'shortcut-row' }, el('kbd', { text: k }), el('span', { text: v })),
      ),
    ),
    el('div', { class: 'hint', style: { marginTop: '10px' } }, '按 Esc 或再按一次 ? 关闭'),
  )
  host = el('div', {
    class: 'modal',
    onclick: (e: Event) => {
      if (e.target === host) close()
    },
  }, panel)
  document.body.appendChild(host)

  const onKey = (e: KeyboardEvent) => {
    if (e.key === 'Escape' || e.key === '?') {
      e.preventDefault()
      close()
    }
  }
  window.addEventListener('keydown', onKey)

  function close(): void {
    window.removeEventListener('keydown', onKey)
    host?.remove()
    host = null
  }
}
