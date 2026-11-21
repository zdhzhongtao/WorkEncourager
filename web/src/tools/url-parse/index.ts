import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'
import { sendMenu } from '../../core/transfer'

interface Row {
  key: string
  raw: string
  decoded: string
}

function safeDecode(v: string): string {
  try {
    return decodeURIComponent(v.replace(/\+/g, ' '))
  } catch {
    return v
  }
}

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const input = el('input', {
    class: 'inp',
    style: { flex: '1', minWidth: '320px' },
    placeholder: 'https://user:pass@example.com:8080/path/a?x=1&y=%E4%B8%AD#hash',
    value: ctx.src ?? '',
  })
  const msg = el('div', { class: 'msg' })
  const baseBox = el('div', { class: 'card-box', style: { marginBottom: '12px' } })
  const paramsHost = el('div')
  const jsonOut = el('pre', { class: 'out', dataset: { ph: '参数 JSON…' }, style: { minHeight: '160px' } })

  let currentJson = ''
  let rows: Row[] = []

  function parse(): void {
    msg.className = 'msg'
    msg.textContent = ''
    baseBox.innerHTML = ''
    paramsHost.innerHTML = ''
    jsonOut.textContent = ''
    currentJson = ''
    rows = []

    const raw = input.value.trim()
    if (!raw) return

    let url: URL
    try {
      url = new URL(raw)
    } catch {
      try {
        url = new URL(`http://${raw}`)
        msg.className = 'msg warn'
        msg.textContent = '没写协议，按 http:// 解析了'
      } catch {
        msg.className = 'msg err'
        msg.textContent = '这不是一个能解析的 URL'
        return
      }
    }

    const pairs: Array<[string, string]> = [
      ['协议 protocol', url.protocol.replace(':', '')],
      ['主机 host', url.hostname],
      ['端口 port', url.port || '（默认）'],
      ['路径 pathname', url.pathname],
      ['查询 query', url.search || '（无）'],
      ['锚点 hash', url.hash || '（无）'],
      ['来源 origin', url.origin],
      ['用户名', url.username || '（无）'],
      ['密码', url.password ? '••••' : '（无）'],
    ]

    const kv = el('div', { class: 'kv' })
    for (const [k, v] of pairs) {
      kv.append(el('div', { text: k }), el('div', { class: 'copyable', text: v, title: '点击复制', onclick: () => copy(v) }))
    }
    baseBox.append(kv)

    url.searchParams.forEach((value, key) => {
      rows.push({ key, raw: value, decoded: safeDecode(value) })
    })

    const table = el('table', { class: 'data' })
    table.append(
      el('thead', null, el('tr', null, el('th', { text: '参数' }), el('th', { text: '原始值' }), el('th', { text: '解码后' }), el('th', { text: '' }))),
    )
    const tbody = el('tbody')
    for (const row of rows) {
      tbody.append(
        el(
          'tr',
          null,
          el('td', { class: 'k', text: row.key }),
          el('td', { class: 'v', text: row.raw }),
          el('td', { class: 'v', text: row.decoded }),
          el('td', null, btn('复制', () => copy(row.decoded), 'ghost')),
        ),
      )
    }
    table.append(tbody)
    paramsHost.append(el('div', { class: 'table-wrap' }, table))

    const obj: Record<string, string | string[]> = {}
    for (const row of rows) {
      const exist = obj[row.key]
      if (exist === undefined) obj[row.key] = row.decoded
      else if (Array.isArray(exist)) exist.push(row.decoded)
      else obj[row.key] = [exist, row.decoded]
    }
    currentJson = JSON.stringify(obj, null, 2)
    jsonOut.textContent = currentJson

    if (!rows.length) {
      msg.className = 'msg info'
      msg.textContent = '这条 URL 没有查询参数'
    }
  }

  const bar = toolbar(
    input,
    btn('解析', parse, 'primary'),
    btn('示例', () => {
      input.value = 'https://user:pass@example.com:8080/a/b?name=%E5%BC%A0%E4%B8%89&tags=js&tags=css&q=a+b#top'
      parse()
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      parse()
      input.focus()
    }, 'ghost'),
  )

  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') parse()
  })
  input.addEventListener('paste', () => setTimeout(parse, 0))

  const jsonPane = pane('参数 JSON', jsonOut, {
    actions: [btn('复制', () => copy(currentJson)), sendMenu(['json'], () => currentJson)],
    foot: foot(el('span', { text: '同名参数会自动合并成数组' })),
  })

  root.append(msg, bar, baseBox, paramsHost, el('div', { style: { marginTop: '12px' } }, jsonPane))
  if (input.value) parse()
  else input.focus()
}
