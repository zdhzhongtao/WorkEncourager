import { el, escapeHtml } from '../../core/dom'
import { highlightJSON } from '../../core/highlight'
import { copy, debounce } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

const SAMPLE =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IldhZGUgWmhvbmciLCJyb2xlIjoiZGV2ZWxvcGVyIiwiaWF0IjoxNzAwMDAwMDAwLCJleHAiOjE5MDAwMDAwMDB9.3Vq7m0dYk9pQ2sJ8xYb1nR4tL6cF5wZ7hG0aE2uK9oM'

function b64urlDecode(part: string): string {
  const clean = part.replace(/-/g, '+').replace(/_/g, '/')
  const pad = (4 - (clean.length % 4)) % 4
  const bin = atob(clean + '='.repeat(pad))
  return new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0)))
}

function relTime(sec: number): string {
  const diff = sec * 1000 - Date.now()
  const abs = Math.abs(diff)
  const units: Array<[number, string]> = [
    [86400000, '天'],
    [3600000, '小时'],
    [60000, '分钟'],
    [1000, '秒'],
  ]
  for (const [ms, name] of units) {
    if (abs >= ms) return `${Math.round(abs / ms)} ${name}${diff < 0 ? '前' : '后'}`
  }
  return diff < 0 ? '刚刚' : '即将'
}

function fmtTime(sec: number): string {
  const d = new Date(sec * 1000)
  return d.toLocaleString('zh-CN', { hour12: false })
}

const TIME_CLAIMS = new Set(['exp', 'iat', 'nbf', 'auth_time', 'updated_at'])

export default function render(root: HTMLElement): void {
  const input = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '粘贴 JWT（形如 xxx.yyy.zzz）…',
  })

  const headerOut = el('pre', { class: 'out', dataset: { ph: 'Header 解析结果…' } })
  const payloadOut = el('pre', { class: 'out', dataset: { ph: 'Payload 解析结果…' } })
  const status = el('div', { class: 'msg' })
  const claimsHost = el('div', { class: 'stack' })
  const algTag = el('span', { class: 'hint' })

  function clearAll(): void {
    headerOut.innerHTML = ''
    payloadOut.innerHTML = ''
    claimsHost.innerHTML = ''
    status.className = 'msg'
    status.textContent = ''
    algTag.textContent = ''
  }

  function run(): void {
    const token = input.value.trim()
    if (!token) {
      clearAll()
      return
    }

    const parts = token.split('.')
    if (parts.length < 2) {
      clearAll()
      status.className = 'msg err'
      status.textContent = '不是一个 JWT：至少要有「Header.Payload」两段，用点号分隔'
      return
    }

    let header: Record<string, unknown> = {}
    let payload: Record<string, unknown> = {}

    try {
      header = JSON.parse(b64urlDecode(parts[0])) as Record<string, unknown>
    } catch {
      headerOut.innerHTML = `<span class="tok-null">Header 不是合法 JSON，原始内容：</span>\n${escapeHtml(
        b64urlDecode(parts[0]),
      )}`
    }
    try {
      payload = JSON.parse(b64urlDecode(parts[1])) as Record<string, unknown>
    } catch {
      payloadOut.innerHTML = `<span class="tok-null">Payload 不是合法 JSON，原始内容：</span>\n${escapeHtml(
        b64urlDecode(parts[1]),
      )}`
    }

    if (Object.keys(header).length) {
      headerOut.innerHTML = highlightJSON(JSON.stringify(header, null, 2))
    }
    if (Object.keys(payload).length) {
      payloadOut.innerHTML = highlightJSON(JSON.stringify(payload, null, 2))
    }

    const alg = typeof header.alg === 'string' ? header.alg : ''
    const typ = typeof header.typ === 'string' ? header.typ : 'JWT'
    algTag.textContent = alg ? `${typ} · ${alg}` : typ

    // ---- 时间状态 ----
    const now = Math.floor(Date.now() / 1000)
    const exp = typeof payload.exp === 'number' ? payload.exp : null
    const nbf = typeof payload.nbf === 'number' ? payload.nbf : null

    status.className = 'msg'
    if (exp === null) {
      status.className = 'msg warn'
      status.textContent = '这个令牌没有 exp 声明，也就是不会过期（长期有效令牌要留意）'
    } else if (exp < now) {
      status.className = 'msg err'
      status.textContent = `令牌已过期 ${relTime(exp)}（过期时间 ${fmtTime(exp)}）`
    } else if (nbf !== null && nbf > now) {
      status.className = 'msg warn'
      status.textContent = `令牌还没生效，${relTime(nbf)}才可用`
    } else {
      status.className = 'msg ok'
      status.textContent = `令牌有效，${relTime(exp)}过期（剩余 ${Math.floor((exp - now) / 60)} 分钟）`
    }

    // ---- 声明明细 ----
    const rows = Object.entries(payload).map(([k, v]) => {
      const valueText = typeof v === 'object' && v !== null ? JSON.stringify(v) : String(v)
      const isTime = TIME_CLAIMS.has(k) && typeof v === 'number'
      const tr = el(
        'tr',
        null,
        el('td', { class: 'k' }, el('span', { text: k }), isTime ? el('span', { class: 'hint', text: ' · 时间' }) : null),
        el('td', { class: 'v', text: valueText }),
      )
      if (isTime) {
        tr.append(el('td', { class: 'hint', text: `${fmtTime(v as number)}（${relTime(v as number)}）` }))
      }
      return tr
    })

    claimsHost.innerHTML = ''
    if (rows.length) {
      const head = el(
        'tr',
        null,
        el('th', { text: '声明' }),
        el('th', { text: '值' }),
        el('th', { text: '说明' }),
      )
      claimsHost.append(el('table', { class: 'data' }, el('thead', null, head), el('tbody', null, ...rows)))
    }

    if (parts[2]) {
      claimsHost.append(
        el('div', {
          class: 'hint',
          text: `签名段长度 ${parts[2].length} 字符，本工具只解码、不校验签名。要验签得用密钥在服务端或脚本里算 HMAC。`,
        }),
      )
    }
  }

  const lazy = debounce(run, 200)
  input.addEventListener('input', lazy)

  const bar = toolbar(
    el('span', { class: 'hint', text: '支持标准 JWT / JWS，Base64URL 解码' }),
    el('span', { class: 'spacer' }),
    btn('示例', () => {
      input.value = SAMPLE
      run()
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      clearAll()
    }, 'ghost'),
  )

  const inputPane = pane('令牌', input, {
    label: algTag,
    actions: [btn('复制', () => copy(input.value))],
    foot: foot(el('span', { text: '解析全程在本地，令牌不会离开浏览器' })),
  })

  root.append(
    status,
    bar,
    inputPane,
    el(
      'div',
      { class: 'panes', style: { marginTop: '12px' } },
      pane('Header', headerOut, { actions: [btn('复制', () => copy(headerOut.textContent ?? ''))] }),
      pane('Payload', payloadOut, { actions: [btn('复制', () => copy(payloadOut.textContent ?? ''))] }),
    ),
    el('div', { style: { marginTop: '12px' } }, claimsHost),
  )

  input.focus()
}
