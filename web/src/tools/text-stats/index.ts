import { el } from '../../core/dom'
import { copy, download } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'
import { sendMenu } from '../../core/transfer'

type Op = 'none' | 'dedupe' | 'sort' | 'sortDesc' | 'reverse' | 'noBlank' | 'number' | 'trim'

const OPS: Array<{ id: Op; label: string }> = [
  { id: 'dedupe', label: '去重' },
  { id: 'sort', label: '升序' },
  { id: 'sortDesc', label: '降序' },
  { id: 'reverse', label: '反转行序' },
  { id: 'noBlank', label: '去空行' },
  { id: 'number', label: '加行号' },
  { id: 'trim', label: '去首尾空格' },
]

function transform(text: string, op: Op): string {
  const lines = text.split('\n')
  switch (op) {
    case 'dedupe':
      return Array.from(new Set(lines)).join('\n')
    case 'sort':
      return [...lines].sort((a, b) => a.localeCompare(b, 'zh-CN')).join('\n')
    case 'sortDesc':
      return [...lines].sort((a, b) => b.localeCompare(a, 'zh-CN')).join('\n')
    case 'reverse':
      return [...lines].reverse().join('\n')
    case 'noBlank':
      return lines.filter((l) => l.trim() !== '').join('\n')
    case 'number':
      return lines.map((l, i) => `${String(i + 1).padStart(String(lines.length).length, ' ')}  ${l}`).join('\n')
    case 'trim':
      return lines.map((l) => l.trim()).join('\n')
    default:
      return text
  }
}

function analyze(text: string): Array<[string, string]> {
  const chars = text.length
  const noSpace = text.replace(/\s/g, '').length
  const lines = text ? text.split('\n').length : 0
  const words = text.trim() ? text.trim().split(/\s+/).length : 0
  const cjk = (text.match(/[\u4e00-\u9fa5]/g) ?? []).length
  const latin = (text.match(/[A-Za-z]/g) ?? []).length
  const digits = (text.match(/\d/g) ?? []).length
  const punct = (text.match(/[^\w\s\u4e00-\u9fa5]/g) ?? []).length
  const bytes = new Blob([text]).size
  return [
    ['字符（含空格）', chars.toLocaleString('en-US')],
    ['字符（不含空白）', noSpace.toLocaleString('en-US')],
    ['行数', lines.toLocaleString('en-US')],
    ['词数（按空白切）', words.toLocaleString('en-US')],
    ['汉字', cjk.toLocaleString('en-US')],
    ['英文字母', latin.toLocaleString('en-US')],
    ['数字', digits.toLocaleString('en-US')],
    ['标点', punct.toLocaleString('en-US')],
    ['字节（UTF-8）', `${bytes.toLocaleString('en-US')} B`],
  ]
}

function topWords(text: string, limit = 12): Array<[string, number]> {
  const map = new Map<string, number>()
  const tokens = text.toLowerCase().match(/[a-z0-9']+|[\u4e00-\u9fa5]{2,}/g) ?? []
  for (const t of tokens) map.set(t, (map.get(t) ?? 0) + 1)
  return [...map.entries()].sort((a, b) => b[1] - a[1]).slice(0, limit)
}

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '粘贴要统计 / 整理的文本…' })
  const out = el('pre', { class: 'out', dataset: { ph: '点上面的操作按钮，结果出现在这里…' } })
  const statsHost = el('div', { class: 'stat-grid', style: { marginBottom: '12px' } })
  const freqHost = el('div', { style: { marginTop: '12px' } })
  const statFoot = el('span', { text: '' })
  let current = ''

  function refreshStats(): void {
    const text = input.value
    statsHost.innerHTML = ''
    for (const [k, v] of analyze(text)) {
      statsHost.append(el('div', { class: 'stat' }, el('b', { text: v }), el('span', { text: k })))
    }
    statFoot.textContent = `${text.length} 字符`

    freqHost.innerHTML = ''
    const top = topWords(text)
    if (!top.length) return
    const table = el('table', { class: 'data' })
    table.append(el('thead', null, el('tr', null, el('th', { text: '词' }), el('th', { text: '次数' }))))
    table.append(el('tbody', null, ...top.map(([w, n]) => el('tr', null, el('td', { class: 'k', text: w }), el('td', { class: 'v', text: String(n) })))))
    freqHost.append(el('div', { class: 'group-title', text: '词频 Top 12' }), el('div', { class: 'table-wrap' }, table))
  }

  function apply(op: Op): void {
    current = transform(input.value, op)
    out.textContent = current
  }

  const opButtons = OPS.map((o) => btn(o.label, () => apply(o.id)))

  const inputPane = pane('输入', input, { foot: foot(statFoot) })
  const outPane = pane('输出', out, {
    actions: [
      btn('复制', () => copy(current || out.textContent || '')),
      sendMenu(['text'], () => current || out.textContent || ''),
      btn('下载', () => {
        if (!current) return
        download('text.txt', current)
      }, 'ghost'),
    ],
  })

  input.addEventListener('input', refreshStats)

  root.append(
    statsHost,
    toolbar(...opButtons, el('span', { class: 'spacer' }), btn('清空', () => {
      input.value = ''
      out.textContent = ''
      current = ''
      refreshStats()
      input.focus()
    }, 'ghost')),
    el('div', { class: 'panes' }, inputPane, outPane),
    freqHost,
  )

  if (ctx.src) {
    input.value = ctx.src
  }
  refreshStats()
  input.focus()
}
