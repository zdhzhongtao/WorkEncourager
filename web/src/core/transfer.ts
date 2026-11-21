/**
 * 工具之间的数据接力 + 深链参数。
 *
 * 两条通道：
 *   1. URL  ?src=<base64url>   —— 短内容，可以直接发给同事，打开即填好
 *   2. sessionStorage          —— 长内容（几万字），避免 URL 超长被截断
 * 读的时候先看 src，再看 pending 暂存。
 */
import { TOOLS } from './registry'
import type { ToolEntry } from './types'
import { el } from './dom'
import { copy, toast } from './kit'

const PENDING_KEY = 'wtb.pending.v1'
const URL_SAFE_LIMIT = 6000

export const KIND_LABEL: Record<string, string> = {
  text: '文本',
  json: 'JSON',
  yaml: 'YAML',
  xml: 'XML',
  csv: 'CSV',
  html: 'HTML',
  markdown: 'Markdown',
  sql: 'SQL',
  jwt: 'JWT',
  url: 'URL',
  ts: 'TypeScript',
  code: '代码',
  css: 'CSS',
  color: '颜色',
  secret: '密钥',
  image: '图片',
}

/* ---------------- 编解码 ---------------- */

export function encodeSrc(text: string): string {
  const bytes = new TextEncoder().encode(text)
  let bin = ''
  for (let i = 0; i < bytes.length; i += 0x8000) {
    bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  }
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

export function decodeSrc(raw: string): string {
  const clean = raw.replace(/-/g, '+').replace(/_/g, '/')
  const pad = (4 - (clean.length % 4)) % 4
  const bin = atob(clean + '='.repeat(pad))
  return new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0)))
}

/* ---------------- 发送 ---------------- */

/** 把 text 送到另一个工具，并跳过去 */
export function sendTo(toolId: string, text: string): void {
  const encoded = encodeSrc(text)
  if (encoded.length <= URL_SAFE_LIMIT) {
    location.hash = `#/t/${toolId}?src=${encoded}`
  } else {
    try {
      sessionStorage.setItem(PENDING_KEY, text)
    } catch {
      toast('内容太大，无法暂存')
      return
    }
    location.hash = `#/t/${toolId}?pending=1`
  }
}

/** 分享当前工具 + 内容的链接 */
export function shareLink(toolId: string, text: string): void {
  const encoded = encodeSrc(text)
  if (encoded.length > URL_SAFE_LIMIT) {
    toast('内容太长，链接会超限，先用「发送到」接力吧')
    return
  }
  const base = location.href.split('#')[0]
  void copy(`${base}#/t/${toolId}?src=${encoded}`, '链接已复制')
}

/** 解析当前 hash 里的 src / pending，取出要填进工具的内容 */
export function readSource(): string | null {
  const raw = location.hash.replace(/^#\/?/, '')
  const qIndex = raw.indexOf('?')
  if (qIndex < 0) return null
  const params = new URLSearchParams(raw.slice(qIndex + 1))
  const src = params.get('src')
  if (src) {
    try {
      return decodeSrc(src)
    } catch {
      return null
    }
  }
  if (params.get('pending')) {
    try {
      const text = sessionStorage.getItem(PENDING_KEY)
      sessionStorage.removeItem(PENDING_KEY)
      return text
    } catch {
      return null
    }
  }
  return null
}

/* ---------------- 发送目标 ---------------- */

/** 哪些工具接得住这些类型 */
export function targetsFor(produces: string[]): ToolEntry[] {
  if (!produces.length) return []
  return TOOLS.filter((t) => t.accepts?.some((k) => produces.includes(k)))
}

export function kindsLabel(kinds: string[]): string {
  return kinds.map((k) => KIND_LABEL[k] ?? k).join(' / ')
}

/**
 * 「发送到…」按钮。produces 声明自己能产出什么，getText 取当前内容。
 * 没匹配到目标时返回一个隐藏节点，不占位。
 */
export function sendMenu(produces: string[], getText: () => string): HTMLElement {
  const list = targetsFor(produces)
  if (!list.length) return el('span', { style: { display: 'none' } })

  const wrap = el('span', { class: 'menu-wrap' })
  const panel = el('div', { class: 'menu' })
  const button = el('button', {
    class: 'btn',
    text: '发送到…',
    onclick: (e: Event) => {
      e.stopPropagation()
      const open = wrap.classList.toggle('open')
      if (!open) return
      panel.innerHTML = ''
      for (const t of list) {
        panel.append(
          el('button', {
            class: 'menu-item',
            onclick: () => {
              const text = getText()
              wrap.classList.remove('open')
              if (!text.trim()) {
                toast('喂，先有点内容再发送')
                return
              }
              sendTo(t.id, text)
            },
          },
            el('span', { text: t.name }),
            el('span', { class: 'menu-hint', text: kindsLabel(t.accepts ?? []) }),
          ),
        )
      }
      setTimeout(() => {
        const off = (ev: Event) => {
          if (!wrap.contains(ev.target as Node)) {
            wrap.classList.remove('open')
            document.removeEventListener('click', off)
          }
        }
        document.addEventListener('click', off)
      }, 0)
    },
  })

  wrap.append(button, panel)
  return wrap
}
