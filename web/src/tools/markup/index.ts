import { seg } from '../../core/ui'
import { workbench } from '../../core/workbench'

const VOID_TAGS = new Set([
  'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr',
])
const RAW_TAGS = new Set(['script', 'style', 'pre', 'textarea'])

type Mode = 'pretty' | 'minify'

interface Piece {
  type: 'open' | 'close' | 'self' | 'text' | 'comment' | 'decl' | 'raw'
  raw: string
  name?: string
}

interface Node {
  type: 'tag' | 'leaf'
  open: string
  close?: string
  name?: string
  children: Node[]
}

function findTagEnd(src: string, start: number): number {
  let quote: string | null = null
  for (let i = start + 1; i < src.length; i++) {
    const ch = src[i]
    if (quote) {
      if (ch === quote) quote = null
      continue
    }
    if (ch === '"' || ch === "'") quote = ch
    else if (ch === '>') return i
  }
  return -1
}

function tokenize(src: string): Piece[] {
  const pieces: Piece[] = []
  let i = 0

  while (i < src.length) {
    const lt = src.indexOf('<', i)
    if (lt < 0) {
      if (i < src.length) pieces.push({ type: 'text', raw: src.slice(i) })
      break
    }
    if (lt > i) pieces.push({ type: 'text', raw: src.slice(i, lt) })

    if (src.startsWith('<!--', lt)) {
      const end = src.indexOf('-->', lt + 4)
      const stop = end < 0 ? src.length : end + 3
      pieces.push({ type: 'comment', raw: src.slice(lt, stop) })
      i = stop
      continue
    }
    if (src[lt + 1] === '!' || src[lt + 1] === '?') {
      const end = src.indexOf('>', lt)
      const stop = end < 0 ? src.length : end + 1
      pieces.push({ type: 'decl', raw: src.slice(lt, stop) })
      i = stop
      continue
    }

    const gt = findTagEnd(src, lt)
    if (gt < 0) {
      pieces.push({ type: 'text', raw: src.slice(lt) })
      break
    }
    const raw = src.slice(lt, gt + 1)
    const closeMatch = /^<\s*\/\s*([\w:-]+)/.exec(raw)
    const openMatch = /^<\s*([\w:-]+)/.exec(raw)

    if (closeMatch) {
      pieces.push({ type: 'close', raw, name: closeMatch[1].toLowerCase() })
      i = gt + 1
      continue
    }
    if (!openMatch) {
      pieces.push({ type: 'text', raw })
      i = gt + 1
      continue
    }

    const name = openMatch[1].toLowerCase()
    const selfClosing = /\/>$/.test(raw) || VOID_TAGS.has(name)
    pieces.push({ type: selfClosing ? 'self' : 'open', raw, name })
    i = gt + 1

    if (RAW_TAGS.has(name) && !selfClosing) {
      const re = new RegExp(`</\\s*${name}\\s*>`, 'i')
      const m = re.exec(src.slice(i))
      if (m) {
        pieces.push({ type: 'raw', raw: src.slice(i, i + m.index), name })
        i += m.index
      }
    }
  }
  return pieces
}

function buildTree(pieces: Piece[]): Node[] {
  const root: Node = { type: 'tag', open: '', children: [] }
  const stack: Node[] = [root]

  for (const piece of pieces) {
    const top = stack[stack.length - 1]
    if (piece.type === 'open') {
      const node: Node = { type: 'tag', open: piece.raw, name: piece.name, children: [] }
      top.children.push(node)
      stack.push(node)
      continue
    }
    if (piece.type === 'close') {
      // 找到匹配的开标签；找不到就当文本丢弃
      for (let i = stack.length - 1; i > 0; i--) {
        if (stack[i].name === piece.name) {
          stack[i].close = piece.raw
          stack.length = i
          break
        }
      }
      continue
    }
    top.children.push({ type: 'leaf', open: piece.raw, children: [] })
  }
  return root.children
}

function serialize(nodes: Node[], indent: string, pad = ''): string[] {
  const lines: string[] = []

  for (const node of nodes) {
    if (node.type === 'leaf') {
      const raw = node.open.trim()
      if (raw) lines.push(pad + raw)
      continue
    }

    const open = node.open.trim()
    const close = (node.close ?? `</${node.name}>`).trim()
    const kids = node.children.filter((c) => c.type === 'tag' || c.open.trim() !== '')

    if (!kids.length) {
      lines.push(pad + open + close)
      continue
    }

    const allLeaves = kids.every((c) => c.type === 'leaf')
    const inlineText = kids.map((c) => c.open.trim()).join('')
    if (allLeaves && inlineText.length <= 80 && !inlineText.includes('\n')) {
      lines.push(pad + open + inlineText + close)
      continue
    }

    lines.push(pad + open)
    for (const child of kids) {
      if (child.type === 'leaf') {
        const text = child.open.trim()
        if (text) lines.push(pad + indent + text)
        continue
      }
      lines.push(...serialize([child], indent, pad + indent))
    }
    lines.push(pad + close)
  }

  return lines
}

function formatMarkup(src: string, indentSize: number): string {
  const tree = buildTree(tokenize(src))
  return serialize(tree, ' '.repeat(indentSize)).join('\n')
}

/** 标签内部折叠空白，但不碰引号里的属性值 */
function collapseTagSpace(raw: string): string {
  let out = ''
  let quote: string | null = null
  let space = false
  for (const ch of raw) {
    if (quote) {
      out += ch
      if (ch === quote) quote = null
      continue
    }
    if (ch === '"' || ch === "'") {
      if (space) out += ' '
      space = false
      quote = ch
      out += ch
      continue
    }
    if (/\s/.test(ch)) {
      space = true
      continue
    }
    if (space) out += ' '
    space = false
    out += ch
  }
  return out
}

/**
 * 压缩：删注释、折叠标签间与标签内的空白。
 * script / style / pre / textarea 的内容一字不动（压了会改语义）。
 */
function minifyMarkup(src: string): string {
  const pieces = tokenize(src)
  let out = ''

  for (const p of pieces) {
    if (p.type === 'comment') continue
    if (p.type === 'raw') {
      out += p.raw
      continue
    }
    if (p.type === 'text') {
      const text = p.raw.replace(/\s+/g, ' ')
      if (!text.trim()) continue
      // 前后各留一个空格，避免把相邻标签的文字粘在一起
      const lead = /^\s/.test(text) ? ' ' : ''
      const tail = /\s$/.test(text) ? ' ' : ''
      out += lead + text.trim() + tail
      continue
    }
    out += collapseTagSpace(p.raw)
  }

  return out.replace(/>\s+</g, '><').trim()
}

const SAMPLE = `<div class="card"><h2>订单</h2><p>共 3 条记录
<span>今天</span></p><ul><li>A</li><li>B</li></ul><input type="text" /><!-- 备注 --></div>`

export default function render(
  root: HTMLElement,
  ctx?: { src: string | null; params?: URLSearchParams },
): void {
  let indentSize = 2
  let mode: Mode = 'pretty'

  const indentSeg = seg<number>(
    [
      { label: '2 空格', value: 2 },
      { label: '4 空格', value: 4 },
    ],
    indentSize,
    (v) => {
      indentSize = v
      wb.run()
    },
  )

  const modeSeg = seg<Mode>(
    [
      { label: '格式化', value: 'pretty' },
      { label: '压缩', value: 'minify' },
    ],
    mode,
    (v) => setMode(v),
  )

  function syncControls(): void {
    const min = mode === 'minify'
    indentSeg.querySelectorAll('button').forEach((b) => {
      ;(b as HTMLButtonElement).disabled = min
      b.title = min ? '压缩不缩进' : ''
    })
  }

  function setMode(next: Mode): void {
    mode = next
    modeSeg.set(next)
    syncControls()
    wb.run()
  }

  let wb: ReturnType<typeof workbench>

  wb = workbench(root, {
    toolId: 'markup',
    placeholder: '粘贴 XML / HTML…',
    outPh: '格式化结果…',
    sample: SAMPLE,
    initial: ctx?.src ?? undefined,
    accepts: ['xml', 'html', 'text'],
    produces: ['xml', 'html'],
    downloadName: 'formatted.xml',
    controls: [modeSeg, indentSeg],
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      if (mode === 'minify') {
        w.setOut(minifyMarkup(text))
        if (w.outFoot.textContent) w.outFoot.textContent = `已压缩（删注释、折叠空白）· ${w.outFoot.textContent}`
        w.ok()
        return
      }
      w.setOut(formatMarkup(text, indentSize))
      if (w.outFoot.textContent) w.outFoot.textContent = `已格式化 · ${indentSize} 空格缩进 · ${w.outFoot.textContent}`
      w.ok()
    },
  })

  syncControls()

  if (ctx?.params?.get('mode') === 'minify') {
    setMode('minify')
    return
  }
  if (!ctx?.src) wb.input.focus()
}
