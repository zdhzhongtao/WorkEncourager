import { el } from '../../core/dom'
import { copy, toast } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

type Mode = 'base64' | 'base64url' | 'url' | 'urlfull' | 'html' | 'unicode' | 'hex'

const MODES: Array<{ value: Mode; label: string }> = [
  { value: 'base64', label: 'Base64' },
  { value: 'base64url', label: 'Base64 URL 安全' },
  { value: 'url', label: 'URL 组件（encodeURIComponent）' },
  { value: 'urlfull', label: '整条 URL（encodeURI）' },
  { value: 'html', label: 'HTML 实体' },
  { value: 'unicode', label: 'Unicode 转义' },
  { value: 'hex', label: 'Hex（UTF-8 字节）' },
]

const ENC = new TextEncoder()
const DEC = new TextDecoder()

function bytesToBase64(bytes: Uint8Array): string {
  let bin = ''
  const chunk = 0x8000
  for (let i = 0; i < bytes.length; i += chunk) {
    bin += String.fromCharCode(...bytes.subarray(i, i + chunk))
  }
  return btoa(bin)
}

function base64ToText(raw: string): string {
  const clean = raw
    .replace(/[\s\r\n]+/g, '')
    .replace(/-/g, '+')
    .replace(/_/g, '/')
  const pad = (4 - (clean.length % 4)) % 4
  const bin = atob(clean + '='.repeat(pad))
  const bytes = Uint8Array.from(bin, (c) => c.charCodeAt(0))
  return DEC.decode(bytes)
}

function htmlDecode(text: string): string {
  const ta = document.createElement('textarea')
  ta.innerHTML = text
  return ta.value
}

function encode(text: string, mode: Mode): string {
  switch (mode) {
    case 'base64':
      return bytesToBase64(ENC.encode(text))
    case 'base64url':
      return bytesToBase64(ENC.encode(text)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
    case 'url':
      return encodeURIComponent(text)
    case 'urlfull':
      return encodeURI(text)
    case 'html':
      return text.replace(/[&<>"']|[^\x00-\x7F]/g, (c) => {
        const named: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }
        if (named[c]) return named[c]
        const cp = c.codePointAt(0) ?? 0
        return `&#${cp};`
      })
    case 'unicode':
      return text.replace(/[^\x00-\x7F]/g, (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, '0')}`)
    case 'hex':
      return Array.from(ENC.encode(text))
        .map((b) => b.toString(16).padStart(2, '0'))
        .join('')
  }
}

function decode(text: string, mode: Mode): string {
  switch (mode) {
    case 'base64':
    case 'base64url':
      return base64ToText(text)
    case 'url':
      return decodeURIComponent(text)
    case 'urlfull':
      return decodeURI(text)
    case 'html':
      return htmlDecode(text)
    case 'unicode':
      return text
        .replace(/\\u\{([0-9a-fA-F]+)\}/g, (_, h: string) => String.fromCodePoint(parseInt(h, 16)))
        .replace(/\\u([0-9a-fA-F]{4})/g, (_, h: string) => String.fromCharCode(parseInt(h, 16)))
        .replace(/\\x([0-9a-fA-F]{2})/g, (_, h: string) => String.fromCharCode(parseInt(h, 16)))
    case 'hex': {
      const clean = text.replace(/0x/gi, '').replace(/[^0-9a-fA-F]/g, '')
      if (clean.length % 2 !== 0) throw new Error('Hex 字符个数是奇数，长度不对')
      const bytes = new Uint8Array(clean.length / 2)
      for (let i = 0; i < bytes.length; i++) bytes[i] = parseInt(clean.slice(i * 2, i * 2 + 2), 16)
      return DEC.decode(bytes)
    }
  }
}

export default function render(root: HTMLElement): void {
  let mode: Mode = 'base64'
  let lastResult = ''

  const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '在这里输入…' })
  const out = el('pre', { class: 'out', dataset: { ph: '选好模式后点「编码」或「解码」…' } })
  const msg = el('div', { class: 'msg' })
  const stat = el('span', { text: '' })

  const select = el('select', { class: 'inp' })
  for (const m of MODES) select.append(el('option', { value: m.value, text: m.label }))
  select.addEventListener('change', () => {
    mode = select.value as Mode
    out.textContent = ''
    msg.textContent = ''
    msg.className = 'msg'
  })

  function apply(dir: 'enc' | 'dec'): void {
    const text = input.value
    msg.className = 'msg'
    msg.textContent = ''
    if (!text) {
      out.textContent = ''
      stat.textContent = ''
      return
    }
    try {
      const result = dir === 'enc' ? encode(text, mode) : decode(text, mode)
      lastResult = result
      out.textContent = result
      stat.textContent = `${text.length} → ${result.length} 字符`
    } catch (err) {
      out.textContent = ''
      stat.textContent = ''
      msg.className = 'msg err'
      msg.textContent = `${dir === 'enc' ? '编码' : '解码'}失败：${(err as Error).message}`
    }
  }

  const bar = toolbar(
    el('span', { class: 'hint', text: '模式' }),
    select,
    btn('编码 →', () => apply('enc'), 'primary'),
    btn('← 解码', () => apply('dec')),
    el('span', { class: 'spacer' }),
    btn('交换', () => {
      const previous = out.textContent ?? ''
      input.value = previous
      out.textContent = ''
      apply('enc')
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      out.textContent = ''
      msg.textContent = ''
      stat.textContent = ''
    }, 'ghost'),
  )

  const inputPane = pane('输入', input, { foot: foot(el('span', { text: '文本按 UTF-8 处理' })) })
  const outPane = pane('输出', out, {
    actions: [
      btn('复制', () => copy(lastResult || out.textContent || '')),
      btn('拿去当输入', () => {
        input.value = lastResult || out.textContent || ''
        toast('已填入左侧')
      }),
    ],
    foot: foot(stat),
  })

  root.append(msg, bar, el('div', { class: 'panes' }, inputPane, outPane))
  input.focus()
}
