import { el } from '../../core/dom'
import { copy, download, toast } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

type Mode =
  | 'base64'
  | 'base64url'
  | 'base32'
  | 'base58'
  | 'ascii85'
  | 'url'
  | 'urlfull'
  | 'html'
  | 'unicode'
  | 'hex'

const MODES: Array<{ value: Mode; label: string }> = [
  { value: 'base64', label: 'Base64' },
  { value: 'base64url', label: 'Base64 URL 安全' },
  { value: 'base32', label: 'Base32' },
  { value: 'base58', label: 'Base58（比特币字母表）' },
  { value: 'ascii85', label: 'ASCII85' },
  { value: 'url', label: 'URL 组件（encodeURIComponent）' },
  { value: 'urlfull', label: '整条 URL（encodeURI）' },
  { value: 'html', label: 'HTML 实体' },
  { value: 'unicode', label: 'Unicode 转义' },
  { value: 'hex', label: 'Hex（UTF-8 字节）' },
]

const ENC = new TextEncoder()
const DEC = new TextDecoder()

/* ---------------- 基础编解码 ---------------- */

function bytesToBase64(bytes: Uint8Array): string {
  let bin = ''
  const chunk = 0x8000
  for (let i = 0; i < bytes.length; i += chunk) bin += String.fromCharCode(...bytes.subarray(i, i + chunk))
  return btoa(bin)
}

function base64ToBytes(raw: string): Uint8Array {
  const clean = raw.replace(/[\s\r\n]+/g, '').replace(/-/g, '+').replace(/_/g, '/')
  const pad = (4 - (clean.length % 4)) % 4
  const bin = atob(clean + '='.repeat(pad))
  return Uint8Array.from(bin, (c) => c.charCodeAt(0))
}

const B32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

function base32Encode(bytes: Uint8Array): string {
  let bits = 0
  let value = 0
  let out = ''
  for (const b of bytes) {
    value = (value << 8) | b
    bits += 8
    while (bits >= 5) {
      out += B32[(value >>> (bits - 5)) & 31]
      bits -= 5
    }
  }
  if (bits > 0) out += B32[(value << (5 - bits)) & 31]
  while (out.length % 8 !== 0) out += '='
  return out
}

function base32Decode(text: string): Uint8Array {
  const clean = text.replace(/=+$/g, '').replace(/\s/g, '').toUpperCase()
  let bits = 0
  let value = 0
  const out: number[] = []
  for (const ch of clean) {
    const idx = B32.indexOf(ch)
    if (idx < 0) throw new Error(`Base32 里有非法字符：${ch}`)
    value = (value << 5) | idx
    bits += 5
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 255)
      bits -= 8
    }
  }
  return Uint8Array.from(out)
}

const B58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz'

function base58Encode(bytes: Uint8Array): string {
  let n = 0n
  for (const b of bytes) n = n * 256n + BigInt(b)
  let out = ''
  while (n > 0n) {
    out = B58[Number(n % 58n)] + out
    n /= 58n
  }
  for (const b of bytes) {
    if (b !== 0) break
    out = '1' + out
  }
  return out || '1'
}

function base58Decode(text: string): Uint8Array {
  let n = 0n
  for (const ch of text.trim()) {
    const idx = B58.indexOf(ch)
    if (idx < 0) throw new Error(`Base58 里有非法字符：${ch}`)
    n = n * 58n + BigInt(idx)
  }
  const out: number[] = []
  while (n > 0n) {
    out.unshift(Number(n % 256n))
    n /= 256n
  }
  for (const ch of text.trim()) {
    if (ch !== '1') break
    out.unshift(0)
  }
  return Uint8Array.from(out)
}

function ascii85Encode(bytes: Uint8Array): string {
  let out = ''
  for (let i = 0; i < bytes.length; i += 4) {
    const chunk = bytes.subarray(i, i + 4)
    const pad = 4 - chunk.length
    const block = Uint8Array.from([...chunk, ...new Array(pad).fill(0)])
    let n = 0
    for (const b of block) n = n * 256 + b
    if (n === 0 && pad === 0) {
      out += 'z'
      continue
    }
    const chars: string[] = []
    for (let k = 0; k < 5; k++) {
      chars.unshift(String.fromCharCode((n % 85) + 33))
      n = Math.floor(n / 85)
    }
    out += chars.join('').slice(0, 5 - pad)
  }
  return out
}

function ascii85Decode(text: string): Uint8Array {
  const clean = text.replace(/\s/g, '').replace(/^<~|~>$/g, '')
  const out: number[] = []
  let group: string[] = []
  const flush = (len: number) => {
    const padded = [...group, ...new Array(5 - group.length).fill('u')]
    let n = 0
    for (const c of padded) n = n * 85 + (c.charCodeAt(0) - 33)
    const bytes = [(n >>> 24) & 255, (n >>> 16) & 255, (n >>> 8) & 255, n & 255]
    out.push(...bytes.slice(0, len))
    group = []
  }
  for (const ch of clean) {
    if (ch === 'z' && !group.length) {
      out.push(0, 0, 0, 0)
      continue
    }
    group.push(ch)
    if (group.length === 5) flush(4)
  }
  if (group.length) flush(group.length - 1)
  return Uint8Array.from(out)
}

function htmlDecode(text: string): string {
  const ta = document.createElement('textarea')
  ta.innerHTML = text
  return ta.value
}

/* ---------------- 主逻辑 ---------------- */

function encodeText(text: string, mode: Mode): string {
  const bytes = ENC.encode(text)
  switch (mode) {
    case 'base64':
      return bytesToBase64(bytes)
    case 'base64url':
      return bytesToBase64(bytes).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
    case 'base32':
      return base32Encode(bytes)
    case 'base58':
      return base58Encode(bytes)
    case 'ascii85':
      return ascii85Encode(bytes)
    case 'url':
      return encodeURIComponent(text)
    case 'urlfull':
      return encodeURI(text)
    case 'html':
      return text.replace(/[&<>"']|[^\x00-\x7F]/g, (c) => {
        const named: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }
        if (named[c]) return named[c]
        return `&#${c.codePointAt(0) ?? 0};`
      })
    case 'unicode':
      return text.replace(/[^\x00-\x7F]/g, (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, '0')}`)
    case 'hex':
      return Array.from(bytes).map((b) => b.toString(16).padStart(2, '0')).join('')
  }
}

function decodeText(text: string, mode: Mode): string {
  switch (mode) {
    case 'base64':
    case 'base64url':
      return DEC.decode(base64ToBytes(text))
    case 'base32':
      return DEC.decode(base32Decode(text))
    case 'base58':
      return DEC.decode(base58Decode(text))
    case 'ascii85':
      return DEC.decode(ascii85Decode(text))
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

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let mode: Mode = 'base64'
  let lastResult = ''
  let lastBytes: Uint8Array | null = null

  const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '在这里输入…' })
  const out = el('pre', { class: 'out', dataset: { ph: '选好模式后点「编码」或「解码」…' } })
  const msg = el('div', { class: 'msg' })
  const stat = el('span', { text: '' })

  const select = el('select', { class: 'inp' })
  for (const m of MODES) select.append(el('option', { value: m.value, text: m.label }))
  select.addEventListener('change', () => {
    mode = select.value as Mode
    out.textContent = ''
    msg.className = 'msg'
    msg.textContent = ''
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
      const result = dir === 'enc' ? encodeText(text, mode) : decodeText(text, mode)
      lastResult = result
      lastBytes = dir === 'dec' ? ENC.encode(result) : null
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
      input.value = out.textContent ?? ''
      out.textContent = ''
      apply('enc')
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      out.textContent = ''
      msg.textContent = ''
      msg.className = 'msg'
      stat.textContent = ''
    }, 'ghost'),
  )

  /* ---------- 文件 ↔ Base64 ---------- */

  const fileInput = el('input', { type: 'file', style: { display: 'none' } })
  const fileInfo = el('span', { class: 'hint', text: '没选文件' })
  const fileOut = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '选文件后这里出现 Base64；反过来，粘 Base64 也可以下载成文件。',
    style: { minHeight: '90px' },
  })

  fileInput.addEventListener('change', async () => {
    const file = fileInput.files?.[0]
    if (!file) return
    const bytes = new Uint8Array(await file.arrayBuffer())
    fileOut.value = bytesToBase64(bytes)
    fileInfo.textContent = `${file.name} · ${(file.size / 1024).toFixed(1)} KB`
    toast('已转成 Base64')
  })

  const filePane = pane('文件 ↔ Base64', fileOut, {
    actions: [
      btn('选文件', () => fileInput.click()),
      btn('下载为文件', () => {
        const raw = fileOut.value.trim()
        if (!raw) return toast('先选个文件或粘一段 Base64')
        try {
          const bytes = base64ToBytes(raw)
          download('decoded.bin', new Blob([bytes as unknown as BlobPart]), 'application/octet-stream')
        } catch {
          toast('这段不是合法的 Base64')
        }
      }),
      btn('复制', () => copy(fileOut.value)),
    ],
    foot: foot(fileInfo),
  })

  const inputPane = pane('输入', input, { foot: foot(el('span', { text: '文本按 UTF-8 处理' })) })
  const outPane = pane('输出', out, {
    actions: [
      btn('复制', () => copy(lastResult || out.textContent || '')),
      btn('拿去当输入', () => {
        input.value = lastResult || out.textContent || ''
        toast('已填入左侧')
      }),
      btn('存成文件', () => {
        if (!lastBytes) return toast('先解码一次')
        download('decoded.bin', new Blob([lastBytes as unknown as BlobPart]), 'application/octet-stream')
      }, 'ghost'),
    ],
    foot: foot(stat),
  })

  root.append(msg, bar, el('div', { class: 'panes' }, inputPane, outPane), el('div', { style: { marginTop: '12px' } }, filePane), fileInput)

  if (ctx.src) {
    input.value = ctx.src
    apply('enc')
  } else {
    input.focus()
  }
}
