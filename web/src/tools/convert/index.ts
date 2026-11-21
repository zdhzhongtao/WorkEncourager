import { load as yamlLoad, dump as yamlDump } from 'js-yaml'
import { el } from '../../core/dom'
import { btn } from '../../core/ui'
import { parseJSON, workbench } from '../../core/workbench'
import type { Workbench } from '../../core/workbench'

/* ---------------- 类型 ---------------- */

type Fmt = 'json' | 'yaml' | 'toml' | 'xml' | 'csv' | 'markdown'

const FMT_LABEL: Record<Fmt, string> = {
  json: 'JSON',
  yaml: 'YAML',
  toml: 'TOML',
  xml: 'XML',
  csv: 'CSV',
  markdown: 'Markdown 表格',
}

/* ---------------- TOML ---------------- */

function parseToml(text: string): unknown {
  const root: Record<string, unknown> = {}
  let current: Record<string, unknown> = root

  const parseValue = (raw: string): unknown => {
    const v = raw.trim()
    if (/^".*"$/.test(v) || /^'.*'$/.test(v)) return v.slice(1, -1).replace(/\\"/g, '"')
    if (v === 'true') return true
    if (v === 'false') return false
    if (/^[[{]/.test(v)) {
      try {
        return JSON.parse(v.replace(/'/g, '"'))
      } catch {
        return v
      }
    }
    const num = Number(v.replace(/_/g, ''))
    return Number.isFinite(num) && v !== '' ? num : v
  }

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim()
    if (!line || line.startsWith('#')) continue

    const arrayTable = /^\[\[(.+)\]\]$/.exec(line)
    if (arrayTable) {
      const path = arrayTable[1].split('.').map((s) => s.trim())
      let node = root
      for (const key of path.slice(0, -1)) {
        if (!node[key] || typeof node[key] !== 'object') node[key] = {}
        node = node[key] as Record<string, unknown>
      }
      const last = path[path.length - 1]
      const arr = (node[last] as unknown[]) ?? []
      const item: Record<string, unknown> = {}
      arr.push(item)
      node[last] = arr
      current = item
      continue
    }

    const table = /^\[(.+)\]$/.exec(line)
    if (table) {
      const path = table[1].split('.').map((s) => s.trim())
      let node = root
      for (const key of path) {
        if (!node[key] || typeof node[key] !== 'object') node[key] = {}
        node = node[key] as Record<string, unknown>
      }
      current = node
      continue
    }

    const eq = line.indexOf('=')
    if (eq < 0) continue
    current[line.slice(0, eq).trim()] = parseValue(line.slice(eq + 1))
  }
  return root
}

function stringifyToml(value: unknown): string {
  const lines: string[] = []
  const isObj = (v: unknown): v is Record<string, unknown> =>
    Boolean(v) && typeof v === 'object' && !Array.isArray(v)

  const scalar = (v: unknown): string => {
    if (typeof v === 'string') return JSON.stringify(v)
    if (typeof v === 'number' || typeof v === 'boolean') return String(v)
    if (v === null || v === undefined) return '""'
    return JSON.stringify(v)
  }

  const inlineArray = (arr: unknown[]): string => `[${arr.map(scalar).join(', ')}]`

  const walk = (obj: Record<string, unknown>, path: string[]): void => {
    const tables: Array<[string, Record<string, unknown>]> = []
    const tableArrays: Array<[string, Array<Record<string, unknown>>]> = []

    for (const [key, val] of Object.entries(obj)) {
      if (isObj(val)) tables.push([key, val])
      else if (Array.isArray(val) && val.every(isObj)) tableArrays.push([key, val as Array<Record<string, unknown>>])
      else if (Array.isArray(val)) lines.push(`${key} = ${inlineArray(val)}`)
      else lines.push(`${key} = ${scalar(val)}`)
    }

    for (const [key, val] of tableArrays) {
      for (const item of val) {
        lines.push('', `[[${[...path, key].join('.')}]]`)
        for (const [k, v] of Object.entries(item)) {
          if (isObj(v)) continue
          lines.push(`${k} = ${Array.isArray(v) ? inlineArray(v) : scalar(v)}`)
        }
      }
    }

    for (const [key, val] of tables) {
      lines.push('', `[${[...path, key].join('.')}]`)
      walk(val, [...path, key])
    }
  }

  if (isObj(value)) walk(value, [])
  return lines.join('\n').replace(/^\n+/, '')
}

/* ---------------- XML ---------------- */

function xmlNodeToValue(node: Element): unknown {
  const children = Array.from(node.children)
  if (!children.length) return node.textContent?.trim() ?? ''
  const out: Record<string, unknown> = {}
  for (const attr of Array.from(node.attributes)) out[`@${attr.name}`] = attr.value
  for (const child of children) {
    const value = xmlNodeToValue(child)
    const name = child.tagName
    if (out[name] === undefined) out[name] = value
    else if (Array.isArray(out[name])) (out[name] as unknown[]).push(value)
    else out[name] = [out[name], value]
  }
  return out
}

function parseXml(text: string): unknown {
  const doc = new DOMParser().parseFromString(text, 'application/xml')
  const err = doc.querySelector('parsererror')
  if (err) throw new Error(`XML 不合法：${err.textContent?.slice(0, 160) ?? '解析失败'}`)
  const root = doc.documentElement
  return { [root.tagName]: xmlNodeToValue(root) }
}

function stringifyXml(value: unknown, indent = 2): string {
  const pad = (n: number) => ' '.repeat(n * indent)
  const esc = (s: string) => s.replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c] ?? c)

  const build = (tag: string, val: unknown, depth: number): string => {
    if (val === null || val === undefined) return `${pad(depth)}<${tag}/>`
    if (typeof val !== 'object') return `${pad(depth)}<${tag}>${esc(String(val))}</${tag}>`
    if (Array.isArray(val)) return val.map((v) => build(tag, v, depth)).join('\n')

    const obj = val as Record<string, unknown>
    const attrs = Object.entries(obj)
      .filter(([k]) => k.startsWith('@'))
      .map(([k, v]) => ` ${k.slice(1)}="${esc(String(v))}"`)
      .join('')
    const entries = Object.entries(obj).filter(([k]) => !k.startsWith('@'))

    if (!entries.length) return `${pad(depth)}<${tag}${attrs}/>`
    if (entries.length === 1 && typeof entries[0][1] !== 'object') {
      return `${pad(depth)}<${tag}${attrs}>${esc(String(entries[0][1]))}</${tag}>`
    }
    const inner = entries.map(([k, v]) => build(k, v, depth + 1)).join('\n')
    return `${pad(depth)}<${tag}${attrs}>\n${inner}\n${pad(depth)}</${tag}>`
  }

  const obj = value as Record<string, unknown>
  const keys = Object.keys(obj)
  if (keys.length === 1) return `<?xml version="1.0" encoding="UTF-8"?>\n${build(keys[0], obj[keys[0]], 0)}`
  return `<?xml version="1.0" encoding="UTF-8"?>\n${build('root', value, 0)}`
}

/* ---------------- CSV ---------------- */

function parseCsv(text: string): unknown[] {
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let quoted = false

  for (let i = 0; i < text.length; i++) {
    const ch = text[i]
    if (quoted) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          cell += '"'
          i++
        } else quoted = false
      } else cell += ch
      continue
    }
    if (ch === '"') quoted = true
    else if (ch === ',') {
      row.push(cell)
      cell = ''
    } else if (ch === '\n') {
      row.push(cell)
      rows.push(row)
      row = []
      cell = ''
    } else if (ch !== '\r') cell += ch
  }
  if (cell || row.length) {
    row.push(cell)
    rows.push(row)
  }

  const clean = rows.filter((r) => r.some((c) => c.trim() !== ''))
  if (!clean.length) return []
  const header = clean[0]
  return clean.slice(1).map((r) => {
    const obj: Record<string, string> = {}
    header.forEach((h, i) => (obj[h.trim() || `col${i + 1}`] = r[i] ?? ''))
    return obj
  })
}

function stringifyCsv(value: unknown): string {
  const rows = Array.isArray(value) ? value : [value]
  const objs = rows.filter((r): r is Record<string, unknown> => Boolean(r) && typeof r === 'object' && !Array.isArray(r))
  if (!objs.length) throw new Error('CSV 需要对象数组，当前结构转不了')

  const headers: string[] = []
  for (const row of objs) for (const k of Object.keys(row)) if (!headers.includes(k)) headers.push(k)

  const cell = (v: unknown) => {
    const s = v === null || v === undefined ? '' : typeof v === 'object' ? JSON.stringify(v) : String(v)
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }

  return [headers.join(','), ...objs.map((row) => headers.map((h) => cell(row[h])).join(','))].join('\n')
}

/* ---------------- Markdown 表格 ---------------- */

function parseMarkdownTable(text: string): unknown[] {
  const lines = text.split(/\r?\n/).filter((l) => l.trim().startsWith('|'))
  if (lines.length < 2) throw new Error('没找到 Markdown 表格（需要有 | 开头的行）')
  const cells = (line: string) =>
    line.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim())
  const header = cells(lines[0])
  const body = lines.filter((l) => !/^\s*\|?[\s:|-]+\|?\s*$/.test(l)).slice(1)
  return body.map((line) => {
    const parts = cells(line)
    const obj: Record<string, string> = {}
    header.forEach((h, i) => (obj[h || `col${i + 1}`] = parts[i] ?? ''))
    return obj
  })
}

function stringifyMarkdownTable(value: unknown): string {
  const rows = (Array.isArray(value) ? value : [value]).filter(
    (r): r is Record<string, unknown> => Boolean(r) && typeof r === 'object' && !Array.isArray(r),
  )
  if (!rows.length) throw new Error('Markdown 表格需要对象数组')

  const headers: string[] = []
  for (const row of rows) for (const k of Object.keys(row)) if (!headers.includes(k)) headers.push(k)

  const cell = (v: unknown) => String(v ?? '').replace(/\|/g, '\\|').replace(/\n/g, ' ')
  const head = `| ${headers.join(' | ')} |`
  const sep = `| ${headers.map(() => '---').join(' | ')} |`
  const body = rows.map((r) => `| ${headers.map((h) => cell(r[h])).join(' | ')} |`)
  return [head, sep, ...body].join('\n')
}

/* ---------------- 统一入口 ---------------- */

function parse(text: string, fmt: Fmt): unknown {
  switch (fmt) {
    case 'json':
      return parseJSON(text)
    case 'yaml':
      return yamlLoad(text)
    case 'toml':
      return parseToml(text)
    case 'xml':
      return parseXml(text)
    case 'csv':
      return parseCsv(text)
    case 'markdown':
      return parseMarkdownTable(text)
  }
}

function serialize(value: unknown, fmt: Fmt): string {
  switch (fmt) {
    case 'json':
      return JSON.stringify(value, null, 2)
    case 'yaml':
      return yamlDump(value, { indent: 2, lineWidth: 120, noRefs: true })
    case 'toml':
      return stringifyToml(value)
    case 'xml':
      return stringifyXml(value)
    case 'csv':
      return stringifyCsv(value)
    case 'markdown':
      return stringifyMarkdownTable(value)
  }
}

/* ---------------- 页面 ---------------- */

const SAMPLE = '{"name":"WadeToolbox","version":"0.2.0","tools":[{"id":"convert","on":true}],"author":{"name":"wade"}}'

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let wb: Workbench
  let from: Fmt = 'json'
  let to: Fmt = 'yaml'

  const fromBar = el('div', { class: 'chips' })
  const toBar = el('div', { class: 'chips' })

  const renderChips = (): void => {
    fromBar.innerHTML = ''
    toBar.innerHTML = ''
    for (const f of Object.keys(FMT_LABEL) as Fmt[]) {
      fromBar.append(
        el('button', {
          class: `chip${from === f ? ' on' : ''}`,
          text: FMT_LABEL[f],
          onclick: () => {
            from = f
            renderChips()
          },
        }),
      )
      toBar.append(
        el('button', {
          class: `chip${to === f ? ' on' : ''}`,
          text: FMT_LABEL[f],
          onclick: () => {
            to = f
            renderChips()
          },
        }),
      )
    }
  }
  renderChips()

  const bar = el(
    'div',
    { class: 'card-box stack', style: { marginBottom: '12px' } },
    el('div', { class: 'row' }, el('span', { class: 'hint', style: { width: '46px' } }, '从'), fromBar),
    el('div', { class: 'row' }, el('span', { class: 'hint', style: { width: '46px' } }, '到'), toBar),
    el(
      'div',
      { class: 'row' },
      btn('⇅ 交换', () => {
        const t = from
        from = to
        to = t
        renderChips()
        wb.run()
      }, 'ghost'),
      el('span', { class: 'hint', text: 'CSV / Markdown 表格按「对象数组」处理，第一行自动当表头。' }),
    ),
  )

  wb = workbench(root, {
    toolId: 'convert',
    placeholder: '粘贴源格式内容…',
    outPh: '转换结果…',
    sample: SAMPLE,
    accepts: ['json', 'yaml', 'xml', 'csv', 'markdown', 'text'],
    produces: ['json', 'yaml', 'xml', 'csv', 'markdown'],
    initial: ctx.src ?? undefined,
    downloadName: 'converted.txt',
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      const data = parse(text, from)
      w.setOut(serialize(data, to))
      w.ok(`${FMT_LABEL[from]} → ${FMT_LABEL[to]}`)
    },
  })

  root.insertBefore(bar, root.firstChild)
  void wb
}
