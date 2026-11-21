import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, toolbar } from '../../core/ui'

function splitWords(raw: string): string[] {
  return raw
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .split(/[^A-Za-z0-9\u4e00-\u9fa5]+/)
    .filter(Boolean)
    .flatMap((part) => (/\d/.test(part) ? part.split(/(\d+)/).filter(Boolean) : [part]))
}

const STYLES: Array<{ label: string; make: (w: string[]) => string }> = [
  { label: 'camelCase', make: (w) => w.map((x, i) => (i === 0 ? x.toLowerCase() : cap(x))).join('') },
  { label: 'PascalCase', make: (w) => w.map(cap).join('') },
  { label: 'snake_case', make: (w) => w.map((x) => x.toLowerCase()).join('_') },
  { label: 'SCREAMING_SNAKE', make: (w) => w.map((x) => x.toUpperCase()).join('_') },
  { label: 'kebab-case', make: (w) => w.map((x) => x.toLowerCase()).join('-') },
  { label: 'dot.case', make: (w) => w.map((x) => x.toLowerCase()).join('.') },
  { label: 'path/case', make: (w) => w.map((x) => x.toLowerCase()).join('/') },
  { label: 'Title Case', make: (w) => w.map(cap).join(' ') },
  { label: 'Sentence case', make: (w) => (w.length ? cap(w[0].toLowerCase()) + ' ' + w.slice(1).map((x) => x.toLowerCase()).join(' ') : '') },
  { label: 'slug', make: (w) => w.map((x) => x.toLowerCase()).join('-') },
]

function cap(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1).toLowerCase()
}

const WHOLE: Array<{ label: string; make: (s: string) => string }> = [
  { label: '全部大写', make: (s) => s.toUpperCase() },
  { label: '全部小写', make: (s) => s.toLowerCase() },
  { label: '每词首字母大写', make: (s) => s.replace(/\b[a-zA-Z]/g, (c) => c.toUpperCase()) },
  { label: '每句首字母大写', make: (s) => s.replace(/(^|[.!?]\s+)([a-z])/g, (_, p: string, c: string) => p + c.toUpperCase()) },
  { label: '大小写互换', make: (s) => s.replace(/[a-zA-Z]/g, (c) => (c === c.toUpperCase() ? c.toLowerCase() : c.toUpperCase())) },
  { label: '反转大小写后再转回来', make: (s) => s.split('').reverse().join('') },
]

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const input = el('input', {
    class: 'inp',
    style: { flex: '1', minWidth: '280px' },
    placeholder: '输入变量名 / 一句话，比如 user profile_name 或 "get user by id"',
    value: ctx.src ?? '',
  })
  const host = el('div')
  const wholeHost = el('div', { class: 'chips' })
  const msg = el('div', { class: 'msg' })

  function renderStyles(): void {
    const raw = input.value
    host.innerHTML = ''
    msg.className = 'msg'
    msg.textContent = ''
    if (!raw.trim()) return
    const words = splitWords(raw)
    if (!words.length) {
      msg.className = 'msg warn'
      msg.textContent = '没切出单词，试试字母 / 数字 / 下划线的组合'
      return
    }

    const table = el('table', { class: 'data' })
    table.append(el('thead', null, el('tr', null, el('th', { text: '风格' }), el('th', { text: '结果' }), el('th', { text: '' }))))
    const tbody = el('tbody')

    for (const style of STYLES) {
      const value = style.make(words)
      tbody.append(
        el(
          'tr',
          null,
          el('td', { class: 'k', text: style.label }),
          el('td', { class: 'v', text: value }),
          el('td', null, btn('复制', () => copy(value), 'ghost')),
        ),
      )
    }
    table.append(tbody)
    host.append(el('div', { class: 'table-wrap', style: { maxHeight: 'none' } }, table))

    msg.className = 'msg info'
    msg.textContent = `切出 ${words.length} 个词：${words.join(' / ')}`
  }

  function renderWhole(): void {
    wholeHost.innerHTML = ''
    for (const op of WHOLE) {
      wholeHost.append(
        el('button', {
          class: 'chip',
          text: op.label,
          onclick: () => {
            input.value = op.make(input.value)
            renderStyles()
          },
        }),
      )
    }
  }

  input.addEventListener('input', renderStyles)

  const bar = toolbar(
    input,
    btn('清空', () => {
      input.value = ''
      renderStyles()
      input.focus()
    }, 'ghost'),
  )

  root.append(
    msg,
    bar,
    el('div', { style: { marginTop: '4px' } }, host),
    el('div', { class: 'card-box', style: { marginTop: '14px' } },
      el('div', { class: 'hint', style: { marginBottom: '8px' } }, '整段文本大小写'),
      wholeHost,
    ),
  )
  renderWhole()
  renderStyles()
  input.focus()
}
