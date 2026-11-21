import { el } from '../../core/dom'
import { parseJSON, workbench } from '../../core/workbench'

type Lang = 'ts' | 'go' | 'java' | 'csharp' | 'python'

const LANG_LABEL: Record<Lang, string> = {
  ts: 'TypeScript',
  go: 'Go',
  java: 'Java',
  csharp: 'C#',
  python: 'Python',
}

interface Field {
  key: string
  node: Node
  optional: boolean
}

type Node =
  | { kind: 'prim'; t: 'string' | 'number' | 'boolean' | 'null' | 'any' }
  | { kind: 'array'; item: Node }
  | { kind: 'object'; name: string; fields: Field[] }

/* ---------------- 类型推导 ---------------- */

function pascal(raw: string): string {
  const parts = raw
    .replace(/[^a-zA-Z0-9\u4e00-\u9fa5]+/g, ' ')
    .trim()
    .split(/\s+/)
    .filter(Boolean)
  const s = parts.map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join('')
  return /^[A-Za-z]/.test(s) ? s : `T${s}`
}

function mergeNodes(a: Node, b: Node): Node {
  if (a.kind === 'object' && b.kind === 'object') {
    const fields = [...a.fields]
    for (const bf of b.fields) {
      const hit = fields.find((f) => f.key === bf.key)
      if (hit) {
        hit.node = mergeNodes(hit.node, bf.node)
        hit.optional = hit.optional || bf.optional
      } else {
        fields.push({ ...bf, optional: true })
      }
    }
    return { kind: 'object', name: a.name, fields }
  }
  if (a.kind === 'array' && b.kind === 'array') return { kind: 'array', item: mergeNodes(a.item, b.item) }
  if (a.kind === 'prim' && b.kind === 'prim' && a.t !== b.t) {
    const pair = new Set([a.t, b.t])
    if (pair.has('null') && pair.size === 2) return a.t === 'null' ? b : a
    return { kind: 'prim', t: 'any' }
  }
  return a
}

function infer(value: unknown, name: string, used: Set<string>): Node {
  if (value === null) return { kind: 'prim', t: 'null' }
  if (Array.isArray(value)) {
    if (!value.length) return { kind: 'array', item: { kind: 'prim', t: 'any' } }
    const itemName = name.endsWith('s') ? name.slice(0, -1) : `${name}Item`
    return {
      kind: 'array',
      item: value.slice(1).reduce<Node>((acc, v) => mergeNodes(acc, infer(v, itemName, used)), infer(value[0], itemName, used)),
    }
  }
  if (typeof value === 'object') {
    let typeName = pascal(name)
    let n = 2
    while (used.has(typeName)) typeName = `${pascal(name)}${n++}`
    used.add(typeName)
    const fields: Field[] = Object.entries(value as Record<string, unknown>).map(([key, val]) => ({
      key,
      node: infer(val, key, used),
      optional: val === null,
    }))
    return { kind: 'object', name: typeName, fields }
  }
  if (typeof value === 'string') return { kind: 'prim', t: 'string' }
  if (typeof value === 'number') return { kind: 'prim', t: 'number' }
  if (typeof value === 'boolean') return { kind: 'prim', t: 'boolean' }
  return { kind: 'prim', t: 'any' }
}

function collect(node: Node, out: Node[] = []): Node[] {
  if (node.kind === 'object') {
    out.push(node)
    node.fields.forEach((f) => collect(f.node, out))
  } else if (node.kind === 'array') collect(node.item, out)
  return out
}

/* ---------------- 各语言输出 ---------------- */

function emitTS(root: Node): string {
  const out: string[] = []
  for (const node of collect(root).filter((n): n is Extract<Node, { kind: 'object' }> => n.kind === 'object')) {
    out.push(`export interface ${node.name} {`)
    for (const f of node.fields) {
      const key = /^[A-Za-z_$][\w$]*$/.test(f.key) ? f.key : JSON.stringify(f.key)
      out.push(`  ${key}${f.optional ? '?' : ''}: ${tsType(f.node)}`)
    }
    out.push('}', '')
  }
  return out.join('\n').trimEnd()
}

function tsType(n: Node): string {
  if (n.kind === 'prim') return n.t === 'null' ? 'null' : n.t
  if (n.kind === 'array') return `${tsType(n.item)}[]`
  return n.name
}

function goType(n: Node): string {
  if (n.kind === 'prim') return n.t === 'number' ? 'float64' : n.t === 'boolean' ? 'bool' : n.t === 'any' || n.t === 'null' ? 'interface{}' : 'string'
  if (n.kind === 'array') return `[]${goType(n.item)}`
  return n.name
}

function snapKey(key: string): string {
  const s = key.replace(/[^A-Za-z0-9]+/g, ' ').trim().split(/\s+/)
  return pascal(s.join(' ') || 'Field')
}

function emitGo(root: Node): string {
  const out: string[] = []
  for (const node of collect(root).filter((n): n is Extract<Node, { kind: 'object' }> => n.kind === 'object')) {
    out.push(`type ${node.name} struct {`)
    for (const f of node.fields) {
      out.push(`\t${snapKey(f.key)} ${goType(f.node)} \`json:"${f.key}${f.optional ? ',omitempty' : ''}"\``)
    }
    out.push('}', '')
  }
  return out.join('\n').trimEnd()
}

function javaType(n: Node): string {
  if (n.kind === 'prim') {
    if (n.t === 'number') return 'Double'
    if (n.t === 'boolean') return 'Boolean'
    if (n.t === 'any' || n.t === 'null') return 'Object'
    return 'String'
  }
  if (n.kind === 'array') return `List<${javaType(n.item)}>`
  return n.name
}

function emitJava(root: Node): string {
  const nodes = collect(root).filter((n): n is Extract<Node, { kind: 'object' }> => n.kind === 'object')
  const out: string[] = ['import java.util.List;', '']
  for (const node of nodes) {
    out.push(`public class ${node.name} {`)
    for (const f of node.fields) {
      const name = f.key.replace(/[^A-Za-z0-9_]/g, '_')
      out.push(`    public ${javaType(f.node)} ${name};`)
    }
    out.push('}', '')
  }
  return out.join('\n').trimEnd()
}

function csType(n: Node): string {
  if (n.kind === 'prim') {
    if (n.t === 'number') return 'double'
    if (n.t === 'boolean') return 'bool'
    if (n.t === 'any' || n.t === 'null') return "object?"
    return 'string'
  }
  if (n.kind === 'array') return `List<${csType(n.item).replace('?', '')}>`
  return n.name
}

function emitCSharp(root: Node): string {
  const nodes = collect(root).filter((n): n is Extract<Node, { kind: 'object' }> => n.kind === 'object')
  const out: string[] = ['using System.Collections.Generic;', '']
  for (const node of nodes) {
    out.push(`public class ${node.name}`, '{')
    for (const f of node.fields) {
      const prop = snapKey(f.key)
      const type = csType(f.node)
      out.push(`    public ${type}? ${prop} { get; set; }`)
    }
    out.push('}', '')
  }
  return out.join('\n').trimEnd()
}

function pyType(n: Node): string {
  if (n.kind === 'prim') {
    if (n.t === 'number') return 'float'
    if (n.t === 'boolean') return 'bool'
    if (n.t === 'any' || n.t === 'null') return 'Any'
    return 'str'
  }
  if (n.kind === 'array') return `List[${pyType(n.item)}]`
  return n.name
}

function emitPython(root: Node): string {
  const nodes = collect(root).filter((n): n is Extract<Node, { kind: 'object' }> => n.kind === 'object')
  const out: string[] = ['from dataclasses import dataclass', 'from typing import Any, List, Optional', '']
  for (const node of nodes) {
    out.push('@dataclass', `class ${node.name}:`)
    if (!node.fields.length) out.push('    pass')
    for (const f of node.fields) {
      const name = f.key.replace(/[^A-Za-z0-9_]/g, '_') || 'field'
      const type = pyType(f.node)
      out.push(`    ${name}: ${f.optional ? `Optional[${type}]` : type}`)
    }
    out.push('')
  }
  return out.join('\n').trimEnd()
}

const EMIT: Record<Lang, (n: Node) => string> = {
  ts: emitTS,
  go: emitGo,
  java: emitJava,
  csharp: emitCSharp,
  python: emitPython,
}

/* ---------------- 页面 ---------------- */

const SAMPLE = JSON.stringify(
  {
    id: 1024,
    name: 'WadeToolbox',
    active: true,
    tags: ['dev', 'offline'],
    owner: { name: 'wade', email: 'wade@example.com' },
    items: [{ sku: 'A-1', price: 9.9, qty: 2 }],
  },
  null,
  2,
)

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let lang: Lang = 'ts'
  const chips = el('div', { class: 'chips' })
  let wb: ReturnType<typeof workbench>

  function paint(): void {
    chips.innerHTML = ''
    for (const key of Object.keys(LANG_LABEL) as Lang[]) {
      chips.append(
        el('button', {
          class: `chip${lang === key ? ' on' : ''}`,
          text: LANG_LABEL[key],
          onclick: () => {
            lang = key
            paint()
            wb.run()
          },
        }),
      )
    }
  }

  wb = workbench(root, {
    toolId: 'codegen',
    placeholder: '粘贴一段 JSON 样例…',
    outPh: '类型定义…',
    sample: SAMPLE,
    initial: ctx.src ?? undefined,
    produces: ['code', 'ts'],
    downloadName: 'types.txt',
    controls: [],
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      const data = parseJSON(text)
      const rootNode = infer(data, 'Root', new Set<string>())
      w.setOut(EMIT[lang](rootNode))
      w.ok(`已按 ${LANG_LABEL[lang]} 生成`)
    },
  })

  paint()
  const box = el('div', { class: 'card-box row', style: { marginBottom: '12px' } }, el('span', { class: 'hint', text: '目标语言' }), chips)
  root.insertBefore(box, root.firstChild)
}
