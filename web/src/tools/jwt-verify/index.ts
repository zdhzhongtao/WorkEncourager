import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn } from '../../core/ui'

type Alg = 'HS256' | 'HS384' | 'HS512' | 'RS256' | 'RS384' | 'RS512'

const ALGS: Alg[] = ['HS256', 'HS384', 'HS512', 'RS256', 'RS384', 'RS512']
const HMAC_HASH: Record<string, string> = { HS256: 'SHA-256', HS384: 'SHA-384', HS512: 'SHA-512' }
const RSA_HASH: Record<string, string> = { RS256: 'SHA-256', RS384: 'SHA-384', RS512: 'SHA-512' }

const ENC = new TextEncoder()

function b64urlToBytes(raw: string): Uint8Array {
  const clean = raw.replace(/-/g, '+').replace(/_/g, '/')
  const pad = (4 - (clean.length % 4)) % 4
  const bin = atob(clean + '='.repeat(pad))
  return Uint8Array.from(bin, (c) => c.charCodeAt(0))
}

function bytesToB64url(bytes: Uint8Array): string {
  let bin = ''
  for (const b of bytes) bin += String.fromCharCode(b)
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function trimBearer(raw: string): string {
  return raw.trim().replace(/^Bearer\s+/i, '')
}

async function verifyToken(token: string, alg: Alg, secret: string, pem: string): Promise<boolean> {
  const [h, p, s] = token.split('.')
  if (!h || !p) throw new Error('这不是 JWT：至少要 header.payload 两段')
  if (!s) throw new Error('这个令牌没有签名段，无法验签')
  const data = ENC.encode(`${h}.${p}`)
  const sig = b64urlToBytes(s)

  if (alg.startsWith('HS')) {
    if (!secret) throw new Error('HMAC 验签需要填密钥')
    const key = await crypto.subtle.importKey('raw', ENC.encode(secret), { name: 'HMAC', hash: HMAC_HASH[alg] }, false, ['verify'])
    return crypto.subtle.verify('HMAC', key, sig as unknown as BufferSource, data as unknown as BufferSource)
  }

  if (!pem.trim()) throw new Error('RSA 验签需要公钥（PEM 或裸 Base64）')
  const clean = pem.replace(/-----(BEGIN|END) PUBLIC KEY-----/g, '').replace(/\s/g, '')
  const der = Uint8Array.from(atob(clean), (c) => c.charCodeAt(0))
  const key = await crypto.subtle.importKey(
    'spki',
    der as unknown as BufferSource,
    { name: 'RSASSA-PKCS1-v1_5', hash: RSA_HASH[alg] },
    false,
    ['verify'],
  )
  return crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, sig as unknown as BufferSource, data as unknown as BufferSource)
}

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const msg = el('div', { class: 'msg' })

  /* ---------------- 验签 ---------------- */

  const tokenInput = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '粘贴要验签的 JWT…',
    style: { minHeight: '110px' },
  })
  const algSel = el('select', { class: 'inp w-md' })
  for (const a of ALGS) algSel.append(el('option', { value: a, text: a }))
  const secretInput = el('input', { class: 'inp', placeholder: 'HMAC 密钥（HS 系列用）', style: { flex: '1' } })
  const pemInput = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '-----BEGIN PUBLIC KEY-----\n…\n-----END PUBLIC KEY-----',
    style: { minHeight: '110px' },
  })
  const result = el('div', { class: 'msg' })

  tokenInput.addEventListener('input', () => {
    const header = tokenInput.value.trim().split('.')[0]
    if (!header) return
    try {
      const raw = new TextDecoder().decode(b64urlToBytes(header))
      const alg = (JSON.parse(raw) as { alg?: string }).alg
      if (alg && ALGS.includes(alg as Alg)) algSel.value = alg
    } catch {
      /* header 坏了就保持原样 */
    }
  })

  const verifyPane = el(
    'div',
    { class: 'stack' },
    el('div', { class: 'row' }, el('span', { class: 'hint', text: '算法' }), algSel, el('span', { class: 'spacer' }),
      btn('验证签名', () => {
        result.className = 'msg'
        result.textContent = '验签中…'
        void verifyToken(trimBearer(tokenInput.value), algSel.value as Alg, secretInput.value, pemInput.value)
          .then((ok) => {
            result.className = ok ? 'msg ok' : 'msg err'
            result.textContent = ok ? '签名有效，内容没被改过' : '签名不匹配：密钥不对，或者令牌被篡改'
          })
          .catch((err: Error) => {
            result.className = 'msg err'
            result.textContent = err.message
          })
      }, 'primary'),
    ),
    result,
    tokenInput,
    el('div', { class: 'row' }, el('span', { class: 'hint', text: 'HMAC 密钥' }), secretInput),
    pemInput,
    el('div', { class: 'hint', text: 'HS 系列填上面的密钥；RS 系列贴公钥 PEM。全程在本地计算，密钥不出浏览器。' }),
  )

  /* ---------------- 签发 ---------------- */

  const payloadInput = el('textarea', {
    class: 'ta',
    spellcheck: false,
    style: { minHeight: '150px' },
    value: `{\n  "sub": "1234567890",\n  "name": "wade",\n  "iat": ${Math.floor(Date.now() / 1000)}\n}`,
  })
  const signAlgSel = el('select', { class: 'inp w-md' })
  for (const a of ['HS256', 'HS384', 'HS512']) signAlgSel.append(el('option', { value: a, text: a }))
  const signSecret = el('input', { class: 'inp', placeholder: 'HMAC 密钥', style: { flex: '1' } })
  const tokenOut = el('pre', { class: 'out', dataset: { ph: '生成的令牌…' }, style: { minHeight: '100px' } })
  let signed = ''

  const signPane = el(
    'div',
    { class: 'stack' },
    el('div', { class: 'row' }, el('span', { class: 'hint', text: '算法' }), signAlgSel,
      el('span', { class: 'hint', text: '密钥' }), signSecret,
      el('span', { class: 'spacer' }),
      btn('生成令牌', () => {
        msg.className = 'msg'
        msg.textContent = ''
        try {
          const payload = JSON.parse(payloadInput.value) as Record<string, unknown>
          if (!signSecret.value) throw new Error('先填密钥')
          const alg = signAlgSel.value as Alg
          const h = bytesToB64url(ENC.encode(JSON.stringify({ alg, typ: 'JWT' })))
          const p = bytesToB64url(ENC.encode(JSON.stringify(payload)))
          void crypto.subtle
            .importKey('raw', ENC.encode(signSecret.value), { name: 'HMAC', hash: HMAC_HASH[alg] }, false, ['sign'])
            .then((key) => crypto.subtle.sign('HMAC', key, ENC.encode(`${h}.${p}`)))
            .then((sig) => {
              signed = `${h}.${p}.${bytesToB64url(new Uint8Array(sig))}`
              tokenOut.textContent = signed
              msg.className = 'msg ok'
              msg.textContent = '已生成，拿去调接口吧'
            })
            .catch((err: Error) => {
              msg.className = 'msg err'
              msg.textContent = err.message
            })
        } catch (err) {
          msg.className = 'msg err'
          msg.textContent = (err as Error).message
        }
      }, 'primary'),
      btn('复制', () => copy(signed)),
    ),
    payloadInput,
    tokenOut,
    el('div', { class: 'hint', text: '只支持 HMAC 签发（HS256/384/512）。RSA 签发需要私钥，不该在浏览器里放，建议交给服务端。' }),
  )

  const tabs = el('div', { class: 'tabs' })
  let tab: 'verify' | 'sign' = 'verify'

  function paint(): void {
    tabs.innerHTML = ''
    for (const item of [{ id: 'verify' as const, label: '验签' }, { id: 'sign' as const, label: '签发' }]) {
      tabs.append(
        el('button', {
          class: `tab${tab === item.id ? ' on' : ''}`,
          text: item.label,
          onclick: () => {
            tab = item.id
            paint()
          },
        }),
      )
    }
    body.innerHTML = ''
    body.append(tab === 'verify' ? verifyPane : signPane)
  }

  const body = el('div')
  root.append(msg, tabs, body)
  paint()

  if (ctx.src) {
    tokenInput.value = ctx.src
    if (ctx.src.split('.').length === 3) {
      tokenInput.dispatchEvent(new Event('input'))
    }
  }
}
