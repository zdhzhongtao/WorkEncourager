import { el } from '../../core/dom'
import { workbench } from '../../core/workbench'

const VOID_TAGS = new Set([
  'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr',
])
const RAW_TAGS = new Set(['script', 'style', 'pre', 'textarea'])

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
        pieces.push({ type: 'raw', raw: src.slice(i, i + m.index) })
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

function minifyMarkup(src: string): string {
  let out = src.replace(/<!--[\s\S]*?-->/g, '')
  out = out
    .split(/(<script[\s\S]*?<\/script>|<style[\s\S]*?<\/style>)/gi)
    .map((part) =>
      /^<(script|style)/i.test(part) ? part : part.replace(/>\s+</g, '><').replace(/\s{2,}/g, ' '),
    )
    .join('')
  return out.trim()
}

const SAMPLE = `<div class="card"><h2>订单</h2><p>共 3 条记录
<span>今天</span></p><ul><li>A</li><li>B</li></ul><input type="text" /><!-- 备注 --></div>`

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let indentSize = 2

  const indentSel = el('select', { class: 'inp w-sm' })
  for (const n of [2, 4]) indentSel.append(el('option', { value: String(n), text: `${n} 空格` }))
  indentSel.value = String(indentSize)
  indentSel.addEventListener('change', () => {
    indentSize = Number(indentSel.value)
    wb.run()
  })

  let wb: ReturnType<typeof workbench>

  wb = workbench(root, {
    toolId: 'markup',
    placeholder: '粘贴 XML / HTML…',
    outPh: '格式化结果…',
    sample: SAMPLE,
    initial: ctx.src ?? undefined,
    accepts: ['xml', 'html', 'text'],
    produces: ['xml', 'html'],
    downloadName: 'formatted.xml',
    controls: [el('span', { class: 'hint', text: '缩进' }), indentSel],
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      w.setOut(formatMarkup(text, indentSize))
      w.ok()
    },
  })

  const minBtn = el('button', {
    class: 'btn',
    text: '压缩',
    onclick: () => {
      if (!wb.input.value.trim()) return
      wb.setOut(minifyMarkup(wb.input.value))
    },
  })
  wb.toolbar.insertBefore(minBtn, wb.toolbar.querySelector('.spacer'))
}
