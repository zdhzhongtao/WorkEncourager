import { el } from '../../core/dom'
import { parseJSON, workbench } from '../../core/workbench'

type Mode = 'query' | 'flatten' | 'unflatten' | 'stats'

const MODES: Array<{ value: Mode; label: string; hint: string }> = [
  { value: 'query', label: 'JSONPath 查询', hint: '例如 $.tools[*].name、$..id' },
  { value: 'flatten', label: '扁平化', hint: '把嵌套结构压成 a.b.0.c 一行一个' },
  { value: 'unflatten', label: '还原', hint: '把上面的扁平结构还原成树' },
  { value: 'stats', label: '结构统计', hint: '节点数、深度、类型分布' },
]

/* ---------------- JSONPath（够用版） ---------------- */

interface Token {
  type: 'key' | 'index' | 'wildcard' | 'descend'
  value?: string
}

function tokenize(path: string): Token[] {
  const src = path.trim().replace(/^\$/, '')
  const tokens: Token[] = []
  let i = 0

  while (i < src.length) {
    const ch = src[i]
    if (ch === '.') {
      if (src[i + 1] === '.') {
        const m = /^\.\.([A-Za-z0-9_$-]+|\*)/.exec(src.slice(i))
        if (!m) throw new Error('.. 后面要跟字段名')
        tokens.push(m[1] === '*' ? { type: 'descend' } : { type: 'descend', value: m[1] })
        i += m[0].length
        continue
      }
      const m = /^\.([A-Za-z0-9_$-]+|\*)/.exec(src.slice(i))
      if (!m) throw new Error('「.」后面要跟字段名')
      tokens.push(m[1] === '*' ? { type: 'wildcard' } : { type: 'key', value: m[1] })
      i += m[0].length
      continue
    }
    if (ch === '[') {
      const end = src.indexOf(']', i)
      if (end < 0) throw new Error('方括号没闭合')
      const inner = src.slice(i + 1, end).trim()
      if (inner === '*') tokens.push({ type: 'wildcard' })
      else if (/^-?\d+$/.test(inner)) tokens.push({ type: 'index', value: inner })
      else {
        const m = /^['"](.+)['"]$/.exec(inner)
        if (!m) throw new Error(`看不懂的选择器：[${inner}]`)
        tokens.push({ type: 'key', value: m[1] })
      }
      i = end + 1
      continue
    }
    if (/[A-Za-z0-9_$-]/.test(ch)) {
      const m = /^[A-Za-z0-9_$-]+/.exec(src.slice(i))
      if (!m) break
      tokens.push({ type: 'key', value: m[0] })
      i += m[0].length
      continue
    }
    throw new Error(`路径里有多余字符：${ch}`)
  }
  return tokens
}

function descend(value: unknown, key: string | undefined, out: unknown[]): void {
  if (key === undefined) out.push(value)
  else if (value && typeof value === 'object') {
    const obj = value as Record<string, unknown>
    if (key in obj) out.push(obj[key])
  }
  if (value && typeof value === 'object') {
    for (const child of Object.values(value as Record<string, unknown>)) descend(child, key, out)
  }
}

function query(data: unknown, path: string): unknown[] {
  let nodes: unknown[] = [data]
  for (const token of tokenize(path)) {
    const next: unknown[] = []
    for (const node of nodes) {
      if (token.type === 'descend') {
        descend(node, token.value, next)
        continue
      }
      if (token.type === 'wildcard') {
        if (Array.isArray(node)) next.push(...node)
        else if (node && typeof node === 'object') next.push(...Object.values(node as Record<string, unknown>))
        continue
      }
      if (token.type === 'index') {
        const idx = Number(token.value)
        if (Array.isArray(node)) {
          const target = node[idx < 0 ? node.length + idx : idx]
          if (target !== undefined) next.push(target)
        }
        continue
      }
      if (node && typeof node === 'object' && !Array.isArray(node)) {
        const obj = node as Record<string, unknown>
        if (token.value !== undefined && token.value in obj) next.push(obj[token.value])
      }
    }
    nodes = next
  }
  return nodes
}

/* ---------------- 扁平化 ---------------- */

function flatten(value: unknown, prefix = '', out: Record<string, unknown> = {}, sep = '.'): Record<string, unknown> {
  if (value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0) {
    out[prefix || ''] = {}
    return out
  }
  if (Array.isArray(value) && value.length === 0) {
    out[prefix || ''] = []
    return out
  }
  if (value && typeof value === 'object') {
    for (const [k, v] of Object.entries(value as Record<string, unknown>)) {
      const key = prefix ? `${prefix}${sep}${k}` : k
      flatten(v, key, out, sep)
    }
    return out
  }
  out[prefix] = value
  return out
}

function unflatten(flat: Record<string, unknown>, sep = '.'): unknown {
  const root: unknown = {}
  for (const [path, value] of Object.entries(flat)) {
    const parts = path.split(sep)
    let node = root as Record<string, unknown> | unknown[]
    for (let i = 0; i < parts.length; i++) {
      const key = parts[i]
      const last = i === parts.length - 1
      const asIndex = /^\d+$/.test(key)
      if (last) {
        if (Array.isArray(node) && asIndex) node[Number(key)] = value
        else (node as Record<string, unknown>)[key] = value
        break
      }
      const nextKey = parts[i + 1]
      const nextIsIndex = /^\d+$/.test(nextKey)
      if (Array.isArray(node) && asIndex) {
        const idx = Number(key)
        if (!node[idx]) node[idx] = nextIsIndex ? [] : {}
        node = node[idx] as Record<string, unknown> | unknown[]
      } else {
        const obj = node as Record<string, unknown>
        if (!obj[key]) obj[key] = nextIsIndex ? [] : {}
        node = obj[key] as Record<string, unknown> | unknown[]
      }
    }
  }
  return root
}

/* ---------------- 统计 ---------------- */

interface Stats {
  nodes: number
  objects: number
  arrays: number
  strings: number
  numbers: number
  booleans: number
  nulls: number
  depth: number
}

function walkStats(value: unknown, depth: number, s: Stats): number {
  s.nodes++
  s.depth = Math.max(s.depth, depth)
  if (value === null) {
    s.nulls++
    return depth
  }
  if (Array.isArray(value)) {
    s.arrays++
    value.forEach((v) => walkStats(v, depth + 1, s))
    return depth
  }
  if (typeof value === 'object') {
    s.objects++
    Object.values(value as Record<string, unknown>).forEach((v) => walkStats(v, depth + 1, s))
    return depth
  }
  if (typeof value === 'string') s.strings++
  else if (typeof value === 'number') s.numbers++
  else if (typeof value === 'boolean') s.booleans++
  return depth
}

/* ---------------- 页面 ---------------- */

const SAMPLE = JSON.stringify(
  {
    store: {
      book: [
        { category: 'reference', author: 'Nigel Rees', title: 'Sayings of the Century', price: 8.95 },
        { category: 'fiction', author: 'Evelyn Waugh', title: 'Sword of Honour', price: 12.99 },
      ],
      bicycle: { color: 'red', price: 19.95 },
    },
    id: 42,
  },
  null,
  2,
)

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let mode: Mode = 'query'
  const chips = el('div', { class: 'chips' })
  const argInput = el('input', { class: 'inp w-md', value: '$..price' })
  const argLabel = el('span', { class: 'hint', text: '路径' })

  let wb: ReturnType<typeof workbench>

  function paint(): void {
    chips.innerHTML = ''
    for (const m of MODES) {
      chips.append(
        el('button', {
          class: `chip${mode === m.value ? ' on' : ''}`,
          text: m.label,
          onclick: () => {
            mode = m.value
            argLabel.style.display = mode === 'query' || mode === 'flatten' || mode === 'unflatten' ? '' : 'none'
            argInput.style.display = argLabel.style.display
            argInput.value = mode === 'query' ? '$..price' : '.'
            paint()
            wb.run()
          },
        }),
      )
    }
  }

  wb = workbench(root, {
    toolId: 'json-tools',
    placeholder: '粘贴 JSON…',
    outPh: '查询结果 / 扁平结果…',
    sample: SAMPLE,
    initial: ctx.src ?? undefined,
    produces: ['json', 'text'],
    downloadName: 'json-result.json',
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      const data = parseJSON(text)

      if (mode === 'query') {
        const path = argInput.value.trim() || '$'
        const hits = query(data, path)
        w.setOut(JSON.stringify(hits.length === 1 ? hits[0] : hits, null, 2))
        w.ok(`命中 ${hits.length} 处`)
        return
      }

      if (mode === 'flatten') {
        const flat = flatten(data, '', {}, argInput.value.trim() || '.')
        w.setOut(JSON.stringify(flat, null, 2))
        w.ok(`${Object.keys(flat).length} 个叶子节点`)
        return
      }

      if (mode === 'unflatten') {
        if (!data || typeof data !== 'object' || Array.isArray(data)) {
          throw new Error('还原需要「键 → 值」形式的扁平对象')
        }
        w.setOut(JSON.stringify(unflatten(data as Record<string, unknown>, argInput.value.trim() || '.'), null, 2))
        w.ok('已还原')
        return
      }

      const s: Stats = { nodes: 0, objects: 0, arrays: 0, strings: 0, numbers: 0, booleans: 0, nulls: 0, depth: 0 }
      walkStats(data, 1, s)
      w.setOut(
        [
          `总节点      ${s.nodes}`,
          `对象        ${s.objects}`,
          `数组        ${s.arrays}`,
          `字符串      ${s.strings}`,
          `数字        ${s.numbers}`,
          `布尔        ${s.booleans}`,
          `null        ${s.nulls}`,
          `最大深度    ${s.depth}`,
          `字节大小    ${new Blob([text]).size} B`,
        ].join('\n'),
      )
      w.ok()
    },
  })

  paint()
  const box = el(
    'div',
    { class: 'card-box stack', style: { marginBottom: '12px' } },
    el('div', { class: 'row' }, chips),
    el('div', { class: 'row' }, argLabel, argInput),
  )
  root.insertBefore(box, root.firstChild)
  argInput.addEventListener('input', () => wb.run())
}
