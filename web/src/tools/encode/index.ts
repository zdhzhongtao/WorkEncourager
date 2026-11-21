import { el } from '../../core/dom'
import { copy, download, toast } from '../../core/kit'
import { btn, foot, pane, seg, toolbar } from '../../core/ui'

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

/** short 用于勾选项上的短名，demo 是这种编码的典型长相（静态示意，不随输入变化） */
const MODES: Array<{ value: Mode; label: string; short: string; demo: string }> = [
  { value: 'base64', label: 'Base64', short: 'Base64', demo: 'A-Za-z0-9+/' },
  { value: 'base64url', label: 'Base64 URL 安全', short: 'Base64URL', demo: '-_ 且无 =' },
  { value: 'base32', label: 'Base32', short: 'Base32', demo: 'A-Z 2-7' },
  { value: 'base58', label: 'Base58（比特币字母表）', short: 'Base58', demo: '无 0OIl +/' },
  { value: 'ascii85', label: 'ASCII85', short: 'ASCII85', demo: '! 到 u' },
  { value: 'url', label: 'URL 组件（encodeURIComponent）', short: 'URL 编码', demo: '%E4%BD' },
  { value: 'urlfull', label: '整条 URL（encodeURI）', short: '整条 URL', demo: '留 :/?#&=' },
  { value: 'html', label: 'HTML 实体', short: 'HTML 实体', demo: '&amp; &lt; &#39;' },
  { value: 'unicode', label: 'Unicode 转义', short: 'Unicode', demo: '\\u4f60' },
  { value: 'hex', label: 'Hex（UTF-8 字节）', short: 'Hex', demo: '0-9 a-f' },
]

const LABEL: Record<Mode, string> = MODES.reduce(
  (acc, m) => ({ ...acc, [m.value]: m.label }),
  {} as Record<Mode, string>,
)

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

/* ---------------- 编码 / 解码 ---------------- */

type EncOpts = { htmlNonAscii?: boolean }

function encodeText(text: string, mode: Mode, opts: EncOpts = {}): string {
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
      return text.replace(opts.htmlNonAscii ? /[&<>"']|[^\x00-\x7F]/g : /[&<>"']/g, (c) => {
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
        .replace(/%u([0-9a-fA-F]{4})/g, (_, h: string) => String.fromCharCode(parseInt(h, 16)))
    case 'hex': {
      const clean = text.replace(/0x/gi, '').replace(/[^0-9a-fA-F]/g, '')
      if (clean.length % 2 !== 0) throw new Error('Hex 字符个数是奇数，长度不对')
      const bytes = new Uint8Array(clean.length / 2)
      for (let i = 0; i < bytes.length; i++) bytes[i] = parseInt(clean.slice(i * 2, i * 2 + 2), 16)
      return DEC.decode(bytes)
    }
  }
}

/* ---------------- 每种模式：说明 + 特征 + 示例 ---------------- */

type Meta = { note: string; hint: string; demo: string }

const META: Record<Mode, Meta> = {
  base64: {
    note: '任意字节每 3 个变 4 个字符，字符集 A-Z a-z 0-9 + /，不够 4 的用 = 补齐。JWT 的中间段、data:image/png;base64、邮件附件都是它。',
    hint: '只含 A-Z a-z 0-9 + /，常以 = 结尾，长度是 4 的倍数',
    demo: '你好，世界',
  },
  base64url: {
    note: '就是 Base64，把 + 和 / 换成 - 和 _、再去掉末尾的 =。这样能安全塞进 URL、文件名和 cookie —— JWT 用的正是这种。',
    hint: '同上，但会出现 - 和 _，且没有被 = 补齐',
    demo: '你好?world=1&x=2',
  },
  base32: {
    note: '只用 A-Z 和 2-7，故意避开 0/1/8/9 免得看错；不区分大小写，能手抄。TOTP 两步验证的密钥、部分 DNS 记录用它。',
    hint: '只含 A-Z 和 2-7，末尾常有 =，长度是 8 的倍数',
    demo: 'hello',
  },
  base58: {
    note: 'Base64 去掉 0 O I l + / 这些容易看错的字符，纯粹为了手抄不出错。比特币地址、IPFS 的 CID 用它。',
    hint: '没有 0 O I l + /，地址通常 26-35 位，CID 通常 44-46 位',
    demo: 'hello',
  },
  ascii85: {
    note: '每 4 个字节变 5 个可打印字符，比 Base64 省约 25% 体积。PDF 里的数据流、PostScript 用它，外面常包一层 <~ ~>。',
    hint: '一串可打印 ASCII（! 到 u 之间），可能以 ~> 结尾',
    demo: 'hello world',
  },
  url: {
    note: '把参数值里的空格、中文、& = ? / 都转成 %XX（按 UTF-8 字节转）。等价于 JS 的 encodeURIComponent，拼查询参数值时用它。',
    hint: '含 %E4%BD%A0 这种「% + 两位十六进制」',
    demo: '搜索 关键词 & a=1',
  },
  urlfull: {
    note: '只转必须转的（空格、中文），保留 : / ? # & = 这些分隔符，适合整条 URL。等价于 JS 的 encodeURI。',
    hint: '同样有 %XX，但 : / ? & = 保持原样',
    demo: 'https://a.com/搜索?q=关键字&x=1 2',
  },
  html: {
    note:
      '把 & < > " \' 换成 &amp; &lt; &gt; &quot; &#39;。作用是让代码或用户输入在网页里「显示成字面」，' +
      '而不是被浏览器当成标签执行 —— 比如要展示 <div> 这个标签本身，就必须写成 &lt;div&gt;，否则页面结构直接被它吃进去。\n' +
      '注意：只有这 5 个符号和属性里的引号是必须转的，中文不用转（UTF-8 页面直接写就行）；' +
      '解码时命名实体和数字实体都认，&#20320; 和 &#x4F60; 都会还原成「你」。',
    hint: '含 &amp; &lt; &gt; &quot; 或 &#20320; 这样的实体',
    demo: '<a href="x">A&B 中文</a>',
  },
  unicode: {
    note: '把非 ASCII 字符转成 \\u4e2d 形式。JS / Java / properties 配置文件里常见；排查「接口返回的怎么是 \\u4e2d」也用这个解开。',
    hint: '含 \\u4e2d、\\u{1F600}、\\xE4 或 %u4E2D',
    demo: '中文',
  },
  hex: {
    note: '每个字节写成两位十六进制。抓包、文件头（PNG 开头是 89 50 4E 47）、颜色值都长这样。',
    hint: '只含 0-9 a-f，长度是偶数，可能带空格或 0x',
    demo: '你好',
  },
}

/* HTML 实体小抄：用户说这块最常用，摊开写清楚 */
const HTML_TABLE: Array<[string, string, string]> = [
  ['&amp;', '&', '和号，必须最先转，否则会把后面的实体吃掉'],
  ['&lt;', '<', '左尖括号，不转就会被当成标签开头'],
  ['&gt;', '>', '右尖括号'],
  ['&quot;', '"', '双引号，写在属性值里时用'],
  ['&#39;', "'", '单引号（&apos; 是 XML 写法，老浏览器不认）'],
  ['&nbsp;', '⍽', '不换行空格，排版占位用'],
  ['&copy;', '©', '版权符号，这类叫「命名实体」'],
  ['&#20320;', '你', '十进制码点：你 = U+4F60 = 20320'],
  ['&#x4F60;', '你', '十六进制写法，和上面完全等价'],
]

/* ---------------- 自动识别 ---------------- */

type Hit = { mode: Mode | 'jwt' | 'plain'; label: string; score: number; why: string[]; preview: string }

/** 解开后能不能当文本读；返回 null 表示是二进制或者不合法 */
function readableText(bytes: Uint8Array): string | null {
  try {
    const s = new TextDecoder('utf-8', { fatal: true }).decode(bytes)
    if (!s) return null
    let bad = 0
    for (const ch of s) {
      const c = ch.codePointAt(0) ?? 0
      if ((c < 32 && c !== 9 && c !== 10 && c !== 13) || c === 127) bad++
    }
    if (bad / s.length > 0.02) return null
    return s
  } catch {
    return null
  }
}

function tryBytes(fn: () => Uint8Array): Uint8Array | null {
  try {
    return fn()
  } catch {
    return null
  }
}

function cut(s: string, n = 60): string {
  const one = s.replace(/\s+/g, ' ').trim()
  return one.length > n ? `${one.slice(0, n)}…` : one
}

const B58_RE = /^[1-9A-HJ-NP-Za-km-z]+$/

export function detect(raw: string): Hit[] {
  const text = raw.trim()
  if (!text) return []
  const head = text.slice(0, 8192)
  const noWs = text.replace(/\s+/g, '')
  const hits: Hit[] = []

  /* JWT：三段 Base64URL 用点连起来 */
  if (/^[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*$/.test(text)) {
    hits.push({
      mode: 'jwt',
      label: 'JWT',
      score: 94,
      why: ['三段用 . 分隔', '前两段是 Base64URL'],
      preview: '这是 JWT，用「JWT 工具」能拆开看 Header / Payload',
    })
  }

  /* HTML 实体 */
  const ent = text.match(/&(#\d{1,7}|#x[0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,10});/g)
  if (ent) {
    const density = ent.join('').length / text.length
    hits.push({
      mode: 'html',
      label: 'HTML 实体',
      score: Math.min(96, 58 + ent.length * 6 + (density > 0.3 ? 8 : 0)),
      why: [`出现 ${ent.length} 个实体，如 ${ent[0]}`, '& < > " 被换成了命名实体或数字码点'],
      preview: cut(htmlDecode(text)),
    })
  }

  /* Unicode 转义 */
  const uni = text.match(/\\u\{[0-9a-fA-F]{1,6}\}|\\u[0-9a-fA-F]{4}|\\x[0-9a-fA-F]{2}|%u[0-9a-fA-F]{4}/g)
  if (uni) {
    hits.push({
      mode: 'unicode',
      label: 'Unicode 转义',
      score: Math.min(95, 60 + uni.length * 6),
      why: [`出现 ${uni.length} 处 \\uXXXX 形式，如 ${uni[0]}`],
      preview: cut(decodeText(text, 'unicode')),
    })
  }

  /* URL 百分号编码 */
  const pct = head.match(/%[0-9a-fA-F]{2}/g)
  if (pct) {
    let decoded = ''
    let ok = true
    try {
      decoded = decodeURIComponent(text)
    } catch {
      ok = false
    }
    hits.push({
      mode: 'url',
      label: 'URL 编码',
      score: ok ? Math.min(92, 48 + pct.length * 6) : 45,
      why: [`出现 ${pct.length} 个 %XX，如 ${pct[0]}`, ok ? '整体能被 decodeURIComponent 还原' : '有孤立的 % 号，可能只是片段'],
      preview: ok ? cut(decoded) : '（还原失败，可能只是网址片段）',
    })
  }

  /* Base64（标准字母表） */
  if (/^[A-Za-z0-9+/]+={0,2}$/.test(noWs) && noWs.length >= 8) {
    const why = ['字符集正好是 A-Z a-z 0-9 + /']
    let score = 40
    if (noWs.length % 4 === 0) {
      score += 10
      why.push('长度是 4 的倍数')
    } else {
      score += 4
      why.push(`没补齐，长度 ${noWs.length}`)
    }
    const bytes = tryBytes(() => base64ToBytes(noWs))
    const readable = bytes ? readableText(bytes) : null
    if (readable) {
      score += 40
      why.push('解开是可读文本')
    } else if (bytes) {
      score += 15
      why.push('解开是二进制（可能是图片 / 压缩包）')
    } else {
      score -= 20
      why.push('解不开，字符组合不合法')
    }
    hits.push({
      mode: 'base64',
      label: 'Base64',
      score: Math.max(0, Math.min(97, score)),
      why,
      preview: readable ? cut(readable) : bytes ? '（二进制数据，预览略）' : '—',
    })
  }

  /* Base64URL：出现 - 或 _ 才算 */
  if (/^[A-Za-z0-9_-]+$/.test(noWs) && /[-_]/.test(noWs) && noWs.length >= 8) {
    const bytes = tryBytes(() => base64ToBytes(noWs))
    const readable = bytes ? readableText(bytes) : null
    hits.push({
      mode: 'base64url',
      label: 'Base64 URL 安全',
      score: readable ? 88 : 62,
      why: ['含 - 或 _，不是标准 Base64 的字母表', readable ? '解开是可读文本' : '解开是二进制'],
      preview: readable ? cut(readable) : '（二进制数据，预览略）',
    })
  }

  /* Base32 */
  if (/^[A-Za-z2-7]+=*$/.test(noWs) && noWs.length >= 8 && (/[2-7]/.test(noWs) || /=/.test(noWs))) {
    const bytes = tryBytes(() => base32Decode(noWs))
    const readable = bytes ? readableText(bytes) : null
    hits.push({
      mode: 'base32',
      label: 'Base32',
      score: (readable ? 88 : 60) + (noWs.length % 8 === 0 ? 6 : 0),
      why: ['只含 A-Z 和 2-7', noWs.length % 8 === 0 ? '长度是 8 的倍数' : '长度不是 8 的倍数', readable ? '解开是可读文本' : '解开是二进制'],
      preview: readable ? cut(readable) : '（二进制数据，预览略）',
    })
  }

  /* Base58 */
  if (B58_RE.test(noWs) && noWs.length >= 12) {
    const bytes = tryBytes(() => base58Decode(noWs))
    const readable = bytes ? readableText(bytes) : null
    const typical = (noWs.length >= 26 && noWs.length <= 35) || (noWs.length >= 44 && noWs.length <= 46)
    const why = ['没有 0 O I l + / 这些易混字符']
    let score = readable ? 70 : 46
    if (bytes && bytes.length === 25 && bytes[0] === 0) {
      score = 92
      why.push('解出来正好 25 字节、首字节 0x00 —— 比特币地址结构')
    } else if (noWs.startsWith('Qm') && noWs.length === 46) {
      score = 90
      why.push('Qm 开头、46 位 —— IPFS 的 CIDv0')
    } else if (typical) {
      score += 10
      why.push('长度像比特币地址或 IPFS CID')
    } else {
      why.push('长度不像常见 Base58 结构')
    }
    why.push(readable ? '解开是可读文本' : '解开是二进制')
    hits.push({
      mode: 'base58',
      label: 'Base58',
      score,
      why,
      preview: readable ? cut(readable) : '（二进制数据，预览略）',
    })
  }

  /* ASCII85 */
  if (/^[!-u]+$/.test(noWs) && noWs.length >= 10 && noWs.length % 5 === 0) {
    const bytes = tryBytes(() => ascii85Decode(noWs))
    const readable = bytes ? readableText(bytes) : null
    hits.push({
      mode: 'ascii85',
      label: 'ASCII85',
      score: readable ? 78 : 52,
      why: ['全是可打印字符，且长度是 5 的倍数', readable ? '解开是可读文本' : '解开是二进制'],
      preview: readable ? cut(readable) : '（二进制数据，预览略）',
    })
  }

  /* Hex：允许空格、换行、0x 前缀 */
  if (/^(0x)?[0-9a-fA-F\s]+$/.test(head) && /\d/.test(noWs)) {
    const clean = noWs.replace(/0x/gi, '')
    if (clean.length >= 8 && clean.length % 2 === 0) {
      const bytes = new Uint8Array(clean.length / 2)
      for (let i = 0; i < bytes.length; i++) bytes[i] = parseInt(clean.slice(i * 2, i * 2 + 2), 16)
      const readable = readableText(bytes)
      hits.push({
        mode: 'hex',
        label: 'Hex',
        score: readable ? 92 : 62,
        why: ['只含 0-9 a-f', `共 ${clean.length} 个十六进制字符（偶数）`, readable ? '按字节解开是可读文本' : '按字节解开是二进制'],
        preview: readable ? cut(readable) : '（二进制数据，预览略）',
      })
    }
  }

  const strong = hits.filter((h) => h.score >= 55).sort((a, b) => b.score - a.score)
  if (!strong.length) {
    if (/[\u4e00-\u9fff]/.test(text)) return []
    return [{ mode: 'plain', label: '看起来是明文', score: 0, why: ['没有匹配到任何编码特征'], preview: cut(text) }]
  }
  return strong.slice(0, 4)
}

/* ---------------- 文件类型：扩展名 ⇄ MIME ⇄ 文件头 ---------------- */

const MIME_BY_EXT: Record<string, string> = {
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  gif: 'image/gif',
  webp: 'image/webp',
  avif: 'image/avif',
  bmp: 'image/bmp',
  svg: 'image/svg+xml',
  ico: 'image/x-icon',
  tif: 'image/tiff',
  tiff: 'image/tiff',
  pdf: 'application/pdf',
  json: 'application/json',
  xml: 'application/xml',
  txt: 'text/plain',
  csv: 'text/csv',
  html: 'text/html',
  css: 'text/css',
  js: 'text/javascript',
  md: 'text/markdown',
  zip: 'application/zip',
  gz: 'application/gzip',
  tar: 'application/x-tar',
  '7z': 'application/x-7z-compressed',
  rar: 'application/vnd.rar',
  doc: 'application/msword',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  xls: 'application/vnd.ms-excel',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  ppt: 'application/vnd.ms-powerpoint',
  pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  mp3: 'audio/mpeg',
  wav: 'audio/wav',
  ogg: 'audio/ogg',
  m4a: 'audio/mp4',
  mp4: 'video/mp4',
  webm: 'video/webm',
  mov: 'video/quicktime',
  woff: 'font/woff',
  woff2: 'font/woff2',
  ttf: 'font/ttf',
  otf: 'font/otf',
  wasm: 'application/wasm',
}

const EXT_BY_MIME: Record<string, string> = {}
for (const [ext, mime] of Object.entries(MIME_BY_EXT)) if (!EXT_BY_MIME[mime]) EXT_BY_MIME[mime] = ext
EXT_BY_MIME['image/jpeg'] = 'jpg'
EXT_BY_MIME['image/tiff'] = 'tif'
EXT_BY_MIME['text/plain'] = 'txt'

function extOfName(name: string): string {
  const m = /\.([A-Za-z0-9]{1,8})$/.exec(name.trim())
  return m ? m[1].toLowerCase() : ''
}

/** 没有 file.type 时按扩展名猜 */
function mimeOfName(name: string, declared?: string): string {
  if (declared && declared !== 'application/octet-stream') return declared
  return MIME_BY_EXT[extOfName(name)] ?? ''
}

/** 文件头嗅探：粘进来一段没名字的 Base64 也能判断出类型 */
function sniffMime(bytes: Uint8Array): string {
  const b = bytes
  if (!b.length) return ''
  const txt = (from: number, len: number): string => {
    let s = ''
    for (let i = from; i < Math.min(from + len, b.length); i++) s += String.fromCharCode(b[i])
    return s
  }
  if (b.length >= 8 && b[0] === 0x89 && txt(1, 3) === 'PNG') return 'image/png'
  if (b.length >= 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) return 'image/jpeg'
  if (b.length >= 6 && (txt(0, 6) === 'GIF87a' || txt(0, 6) === 'GIF89a')) return 'image/gif'
  if (b.length >= 12 && txt(0, 4) === 'RIFF' && txt(8, 4) === 'WEBP') return 'image/webp'
  if (b.length >= 12 && txt(0, 4) === 'RIFF' && txt(8, 4) === 'WAVE') return 'audio/wav'
  if (b.length >= 5 && txt(0, 5) === '%PDF-') return 'application/pdf'
  if (b.length >= 4 && b[0] === 0x50 && b[1] === 0x4b && b[2] === 0x03 && b[3] === 0x04) return 'application/zip'
  if (b.length >= 2 && b[0] === 0x1f && b[1] === 0x8b) return 'application/gzip'
  if (b.length >= 4 && b[0] === 0x00 && b[1] === 0x00 && b[2] === 0x01 && b[3] === 0x00) return 'image/x-icon'
  if (b.length >= 4 && txt(0, 4) === 'OggS') return 'audio/ogg'
  if (b.length >= 4 && txt(0, 4) === 'fLaC') return 'audio/flac'
  if (b.length >= 3 && txt(0, 3) === 'ID3') return 'audio/mpeg'
  if (b.length >= 2 && b[0] === 0xff && (b[1] & 0xe0) === 0xe0) return 'audio/mpeg'
  if (b.length >= 12 && txt(4, 4) === 'ftyp') return 'video/mp4'
  if (b.length >= 4 && b[0] === 0x1a && b[1] === 0x45 && b[2] === 0xdf && b[3] === 0xa3) return 'video/webm'
  if (b.length >= 4 && txt(0, 4) === 'wOFF') return 'font/woff'
  if (b.length >= 4 && txt(0, 4) === 'wOF2') return 'font/woff2'
  if (b.length >= 4 && b[0] === 0x00 && txt(1, 3) === 'asm') return 'application/wasm'
  if (b.length >= 2 && txt(0, 2) === 'BM') return 'image/bmp'
  return ''
}

/** 生成能直接贴进 HTML 的片段 */
function htmlSnippet(mime: string, raw: string, name: string, ext: string): string {
  const uri = `data:${mime || 'application/octet-stream'};base64,${raw}`
  const file = name || `file.${ext}`
  if (mime.startsWith('image/')) return `<img src="${uri}" alt="${file}" />`
  if (mime.startsWith('audio/')) return `<audio controls src="${uri}"></audio>`
  if (mime.startsWith('video/')) return `<video controls src="${uri}"></video>`
  if (mime === 'application/pdf') return `<iframe src="${uri}" width="100%" height="600" title="${file}"></iframe>`
  if (mime.startsWith('font/')) return `<style>@font-face { font-family: "custom"; src: url("${uri}"); }</style>`
  return `<a download="${file}" href="${uri}">下载 ${file}</a>`
}

/** 粘进来的东西可能是纯 Base64，也可能是整串 data URI */
function parseAnyBase64(text: string): { raw: string; mime: string } {
  const t = text.trim()
  const m = /^data:([^;,]*)(;[^,]*)?,/i.exec(t)
  if (m) return { raw: t.slice(m[0].length).replace(/\s+/g, ''), mime: (m[1] || '').toLowerCase() }
  return { raw: t.replace(/\s+/g, ''), mime: '' }
}

function approxBytes(b64: string): number {
  const pad = b64.endsWith('==') ? 2 : b64.endsWith('=') ? 1 : 0
  return Math.max(0, Math.floor((b64.length * 3) / 4) - pad)
}

function fmtSize(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(2)} MB`
}

/* ---------------- 页面 ---------------- */

export default function render(root: HTMLElement, ctx: { src: string | null; params?: URLSearchParams }): void {
  let mode: Mode = 'base64'
  let curDir: 'enc' | 'dec' = 'enc'
  let lastResult = ''
  let lastBytes: Uint8Array | null = null
  let htmlNonAscii = false

  const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '把要处理的字符串粘到这里 —— 下面会自动告诉你它像哪种编码' })
  const out = el('pre', { class: 'out', dataset: { ph: '粘上内容，勾选上面任意一个模式就会直接出结果…' } })
  const msg = el('div', { class: 'msg' })
  const stat = el('span', { text: '' })

  /* 当前模式的说明 + 示例 */
  const noteText = el('div', { class: 'enc-note-note' })
  const noteDemo = el('div', { class: 'enc-note-demo mono' })
  const htmlCheat = el('div', { class: 'enc-cheat' })
  const noteBox = el('div', { class: 'enc-note' }, el('div', { class: 'enc-note-head' }, el('b', { text: '' }), el('span', { class: 'hint' })), noteText, noteDemo, htmlCheat)

  /* 模式：一行「编码」一行「解码」，勾哪个就直接执行 */
  const radioInputs: Record<'enc' | 'dec', HTMLInputElement[]> = { enc: [], dec: [] }

  function buildModeRow(rowDir: 'enc' | 'dec'): HTMLElement {
    const opts = el('div', { class: 'enc-mode-opts' })
    for (const m of MODES) {
      const radio = el('input', { type: 'radio', name: `enc-mode-${rowDir}`, value: m.value })
      radio.addEventListener('change', () => {
        if (!radio.checked) return
        pickMode(m.value, rowDir)
        if (input.value) apply(rowDir)
      })
      radioInputs[rowDir].push(radio)
      opts.append(
        el('label', { class: 'enc-radio', title: `${m.label}\n字符集：${m.demo}\n${META[m.value].hint}` },
          radio,
          el('span', { class: 'enc-radio-name', text: m.short }),
          el('code', { class: 'enc-radio-demo', text: m.demo }),
        ),
      )
    }
    return el('div', { class: 'enc-mode-row' },
      el('span', { class: 'enc-mode-label', text: rowDir === 'enc' ? '编码 →' : '← 解码' }),
      opts,
    )
  }

  const modeBox = el('div', { class: 'enc-modes' }, buildModeRow('enc'), buildModeRow('dec'))
  const modeTip = el('div', {
    class: 'enc-modes-tip',
    text: '勾哪一项就直接执行。右边灰色小字是这种编码的字符集特征 —— 拿手里的串对照一下，就知道它像哪一种。',
  })

  const nonAsciiBox = el(
    'label',
    { class: 'enc-opt', style: { display: 'none' } },
    el('input', {
      type: 'checkbox',
      onchange: (e: Event) => {
        htmlNonAscii = (e.target as HTMLInputElement).checked
        if (input.value) apply('enc')
      },
    }),
    el('span', { text: '中文等非 ASCII 也转成 &#20320;' }),
  )

  /* 识别面板 */
  const detectHost = el('div', { class: 'enc-detect' })

  function syncRadios(): void {
    for (const d of ['enc', 'dec'] as const) {
      for (const radio of radioInputs[d]) {
        const on = d === curDir && radio.value === mode
        radio.checked = on
        radio.parentElement?.classList.toggle('on', on)
      }
    }
  }

  function pickMode(next: Mode, nextDir?: 'enc' | 'dec'): void {
    mode = next
    if (nextDir) curDir = nextDir
    syncRadios()
    nonAsciiBox.style.display = next === 'html' ? 'flex' : 'none'
    paintNote()
  }

  function paintNote(): void {
    const meta = META[mode]
    const head = noteBox.querySelector('.enc-note-head') as HTMLElement
    head.children[0].textContent = LABEL[mode]
    head.children[1].textContent = meta.hint
    noteText.textContent = meta.note
    noteDemo.textContent = `${meta.demo}  →  ${encodeText(meta.demo, mode, { htmlNonAscii })}`
    htmlCheat.style.display = mode === 'html' ? 'grid' : 'none'
    if (mode === 'html' && !htmlCheat.childElementCount) {
      for (const [entity, char, why] of HTML_TABLE) {
        const cell = el('div', { class: 'enc-cheat-row' },
          el('code', { text: entity }),
          el('span', { class: 'enc-cheat-char', text: char }),
          el('span', { class: 'hint', text: why }),
        )
        cell.addEventListener('click', () => copy(entity))
        cell.title = '点击复制'
        htmlCheat.append(cell)
      }
    }
  }

  function renderDetect(rawText: string): void {
    detectHost.innerHTML = ''
    const hits = detect(rawText)
    if (!hits.length) return
    const head = el('div', { class: 'enc-detect-head' }, el('b', { text: '这看起来是' }))
    detectHost.append(head)
    hits.forEach((hit, idx) => {
      if (hit.mode === 'plain') {
        detectHost.append(
          el('div', { class: 'enc-hit plain' },
            el('div', { class: 'enc-hit-main' },
              el('div', { class: 'enc-hit-title' }, el('b', { text: hit.label })),
              el('div', { class: 'enc-hit-why', text: hit.why.join(' · ') }),
            ),
          ),
        )
        return
      }
      const bar = el('div', { class: 'enc-score' }, el('i', { style: { width: `${hit.score}%` } }))
      const row = el('div', { class: `enc-hit${idx === 0 ? ' top' : ''}` },
        el('div', { class: 'enc-hit-main' },
          el('div', { class: 'enc-hit-title' },
            el('b', { text: hit.label }),
            el('span', { class: 'enc-pct', text: `${hit.score}%` }),
          ),
          el('div', { class: 'enc-hit-why', text: hit.why.join(' · ') }),
          el('div', { class: 'enc-hit-pv', text: hit.preview ? `解开：${hit.preview}` : '' }),
          bar,
        ),
      )
      const label = hit.mode === 'jwt' ? '打开 JWT 工具' : `按 ${hit.label} 解码`
      const target = hit.mode
      row.append(
        el('div', { class: 'enc-hit-act' },
          btn(label, () => {
            if (target === 'jwt') {
              const s = input.value.trim()
              const b64u = bytesToBase64(ENC.encode(s)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
              location.hash = `#/t/jwt?src=${b64u}`
              return
            }
            pickMode(target)
            apply('dec')
          }, idx === 0 ? 'primary' : 'ghost'),
        ),
      )
      detectHost.append(row)
    })
  }

  let detectTimer = 0
  function scheduleDetect(): void {
    window.clearTimeout(detectTimer)
    detectTimer = window.setTimeout(() => renderDetect(input.value), 260)
  }

  function apply(dir: 'enc' | 'dec'): void {
    curDir = dir
    syncRadios()
    const text = input.value
    msg.className = 'msg'
    msg.textContent = ''
    if (!text) {
      out.textContent = ''
      stat.textContent = ''
      return
    }
    try {
      const result = dir === 'enc' ? encodeText(text, mode, { htmlNonAscii }) : decodeText(text, mode)
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
    btn('编码 →', () => apply('enc'), 'primary'),
    btn('← 解码', () => apply('dec')),
    nonAsciiBox,
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
      renderDetect('')
    }, 'ghost'),
  )

  /* ---------- 同一句话的 10 种编码长什么样 ---------- */

  const sample = '你好 <b>&</b>'
  const cmpTable = el('table', { class: 'data' })
  cmpTable.append(
    el('thead', null, el('tr', null,
      el('th', { text: '模式' }),
      el('th', { text: `同一句话「${sample}」编码后` }),
      el('th', { text: '怎么一眼认出来' }),
      el('th', { text: '' }),
    )),
  )
  const cmpBody = el('tbody')
  for (const m of MODES) {
    const encoded = encodeText(sample, m.value, {})
    const outTd = el('td', { class: 'v', text: encoded, style: { cursor: 'copy' } })
    outTd.title = '点击复制'
    outTd.addEventListener('click', () => copy(encoded))
    cmpBody.append(
      el('tr', null,
        el('td', { class: 'k', text: m.label }),
        outTd,
        el('td', { class: 'hint', text: META[m.value].hint }),
        el('td', null, btn('试一下', () => {
          pickMode(m.value)
          input.value = sample
          apply('enc')
          scheduleDetect()
        }, 'ghost')),
      ),
    )
  }
  cmpTable.append(cmpBody)

  const cmpPane = pane('示例｜同一句话，10 种编码长什么样', el('div', { class: 'table-wrap', style: { maxHeight: 'none' } }, cmpTable), {
    foot: foot(el('span', { class: 'hint', text: '点「试一下」把示例填进输入框；点编码结果直接复制' })),
  })

  /* ---------- 文件 ↔ Base64 ---------- */

  type FileForm = 'raw' | 'uri' | 'html'
  let fileForm: FileForm = 'raw'
  let rawB64 = ''
  let fileName = ''
  let sniffedMime = ''

  const fileInput = el('input', { type: 'file', style: { display: 'none' } })
  const fileInfo = el('span', { class: 'hint', text: '还没选文件' })
  const mimeInput = el('input', {
    class: 'inp',
    type: 'text',
    spellcheck: false,
    placeholder: '自动识别',
    style: { width: '224px' },
    oninput: () => renderFile(),
  })
  mimeInput.setAttribute('list', 'enc-mime-options')
  const mimeList = el('datalist', { id: 'enc-mime-options' })
  for (const m of [
    'image/png',
    'image/jpeg',
    'image/gif',
    'image/webp',
    'image/svg+xml',
    'application/pdf',
    'application/zip',
    'audio/mpeg',
    'video/mp4',
    'text/plain',
    'application/json',
  ])
    mimeList.append(el('option', { value: m }))

  const fileOut = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '选个文件，这里会出现能直接贴进网页的 Base64；反过来，粘 Base64 或整串 data:image/png;base64,... 也能下载成文件。',
    style: { minHeight: '98px' },
  })
  const preview = el('div', { class: 'enc-preview' })

  function mimeNow(): string {
    return mimeInput.value.trim() || sniffedMime || 'application/octet-stream'
  }

  function effectiveExt(): string {
    return EXT_BY_MIME[mimeNow()] || extOfName(fileName) || 'bin'
  }

  function renderFile(): void {
    const mime = mimeNow()
    if (fileForm === 'raw') fileOut.value = rawB64
    else if (fileForm === 'uri') fileOut.value = rawB64 ? `data:${mime};base64,${rawB64}` : ''
    else fileOut.value = rawB64 ? htmlSnippet(mime, rawB64, fileName, effectiveExt()) : ''
    renderPreview(mime)
    renderFileInfo()
  }

  function renderFileInfo(): void {
    if (!rawB64) {
      fileInfo.textContent = '还没选文件'
      return
    }
    const who = fileName || '粘贴进来的数据'
    fileInfo.textContent = `${who} · ${fmtSize(approxBytes(rawB64))} · ${mimeNow()}`
  }

  function renderPreview(mime: string): void {
    preview.innerHTML = ''
    if (!rawB64) return
    if (approxBytes(rawB64) > 8 * 1024 * 1024) {
      preview.append(el('div', { class: 'hint', text: '文件超过 8 MB，为了不卡页面这里就不预览了，直接下载看。' }))
      return
    }
    const uri = `data:${mime};base64,${rawB64}`
    if (mime.startsWith('image/')) {
      preview.append(
        el('img', { src: uri, alt: '预览', style: { maxWidth: '100%', maxHeight: '240px', borderRadius: '8px', border: '1px solid var(--border)' } }),
      )
    } else if (mime.startsWith('audio/')) {
      preview.append(el('audio', { controls: true, src: uri, style: { width: '100%' } }))
    } else if (mime.startsWith('video/')) {
      preview.append(el('video', { controls: true, src: uri, style: { maxWidth: '100%', maxHeight: '240px', borderRadius: '8px' } }))
    } else if (mime === 'application/pdf') {
      preview.append(el('iframe', { src: uri, title: 'PDF 预览', style: { width: '100%', height: '300px', border: '1px solid var(--border)', borderRadius: '8px' } }))
    } else {
      preview.append(el('div', { class: 'hint', text: `${mime || '未知类型'} 不能直接在网页里显示，下载后用对应程序打开。` }))
    }
  }

  function loadBytes(bytes: Uint8Array, name: string, mime: string): void {
    rawB64 = bytesToBase64(bytes)
    fileName = name
    sniffedMime = sniffMime(bytes)
    mimeInput.value = mime || sniffedMime || 'application/octet-stream'
    renderFile()
  }

  const formSeg = seg<FileForm>(
    [
      { label: '纯 Base64', value: 'raw' },
      { label: 'data URI', value: 'uri' },
      { label: 'HTML 片段', value: 'html' },
    ],
    'raw',
    (v) => {
      fileForm = v
      renderFile()
    },
  )

  fileInput.addEventListener('change', async () => {
    const file = fileInput.files?.[0]
    if (!file) return
    const bytes = new Uint8Array(await file.arrayBuffer())
    loadBytes(bytes, file.name, mimeOfName(file.name, file.type))
    toast(`已转成 ${fileForm === 'raw' ? 'Base64' : fileForm === 'uri' ? 'data URI' : 'HTML 片段'}`)
  })

  /* 手动粘/改内容时，同步回状态（不重写 textarea，免得光标乱跳） */
  fileOut.addEventListener('input', () => {
    const parsed = parseAnyBase64(fileOut.value)
    rawB64 = parsed.raw
    let sniffed = ''
    try {
      sniffed = sniffMime(base64ToBytes(parsed.raw))
    } catch {
      sniffed = ''
    }
    sniffedMime = sniffed
    // 换了内容就重新认类型：data URI 里写明的优先，其次看文件头
    mimeInput.value = parsed.mime || sniffed || ''
    fileName = ''
    renderPreview(mimeNow())
    renderFileInfo()
  })

  const filePane = pane('文件 ↔ Base64（带类型）', el('div', { class: 'enc-file-body' },
    el('div', { class: 'enc-file-bar' },
      el('span', { class: 'field-label', text: 'MIME 类型' }),
      mimeInput,
      mimeList,
      el('span', { class: 'hint', text: '选文件自动填 · 粘 Base64 按文件头猜' }),
    ),
    el('div', { class: 'enc-file-bar' },
      el('span', { class: 'field-label', text: '输出形式' }),
      formSeg,
    ),
    fileOut,
    preview,
    el('div', { class: 'enc-file-tip' },
      '纯 Base64 用在 ',
      el('code', { text: '<img src="data:image/png;base64,…">' }),
      ' 这种要自己拼前缀的地方；data URI 是整串，可以直接粘到浏览器地址栏（图片、PDF 会打开，其它类型会下载但文件名可能丢）；要保住文件名、直接给人点，就用「HTML 片段」里的 ',
      el('code', { text: '<a download>' }),
      '。',
    ),
  ), {
    actions: [
      btn('选文件', () => fileInput.click()),
      btn('下载为文件', () => {
        const { raw } = parseAnyBase64(fileOut.value)
        if (!raw) return toast('先选个文件，或粘一段 Base64')
        try {
          const bytes = base64ToBytes(raw)
          const mime = mimeNow()
          download(fileName || `decoded.${effectiveExt()}`, new Blob([bytes as unknown as BlobPart], { type: mime }), mime)
        } catch {
          toast('这段不是合法的 Base64')
        }
      }),
      btn('复制', () => copy(fileOut.value)),
    ],
    foot: foot(fileInfo),
  })

  const inputPane = pane('输入', input, {
    actions: [
      btn('这是什么编码？', () => {
        renderDetect(input.value)
        detectHost.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
      }, 'ghost'),
    ],
    foot: foot(el('span', { text: '文本按 UTF-8 处理' })),
  })
  const outPane = pane('输出', out, {
    actions: [
      btn('复制', () => copy(lastResult || out.textContent || '')),
      btn('拿去当输入', () => {
        input.value = lastResult || out.textContent || ''
        scheduleDetect()
        toast('已填入左侧')
      }),
      btn('存成文件', () => {
        if (!lastBytes) return toast('先解码一次')
        download('decoded.bin', new Blob([lastBytes as unknown as BlobPart]), 'application/octet-stream')
      }, 'ghost'),
    ],
    foot: foot(stat),
  })

  input.addEventListener('input', scheduleDetect)

  root.append(
    msg,
    modeBox,
    modeTip,
    bar,
    noteBox,
    detectHost,
    el('div', { class: 'panes' }, inputPane, outPane),
    el('div', { style: { marginTop: '12px', display: 'flex', flexDirection: 'column', gap: '12px' } }, cmpPane, filePane),
    fileInput,
  )

  const want = ctx.params?.get('mode') as Mode | null
  pickMode(want && MODES.some((m) => m.value === want) ? want : 'base64')
  if (ctx.src) {
    input.value = ctx.src
    apply(ctx.params?.get('dir') === 'dec' ? 'dec' : 'enc')
    renderDetect(ctx.src)
  } else {
    input.focus()
  }
}
