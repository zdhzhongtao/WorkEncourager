import { el } from '../../core/dom'
import { copy, toast } from '../../core/kit'
import { btn, foot, pane, seg, toolbar } from '../../core/ui'

const ENC = new TextEncoder()
const DEC = new TextDecoder()

function toB64(bytes: Uint8Array): string {
  let bin = ''
  for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return btoa(bin)
}

function fromB64(text: string): Uint8Array {
  const clean = text.replace(/\s/g, '')
  const bin = atob(clean)
  return Uint8Array.from(bin, (c) => c.charCodeAt(0))
}

function toHex(bytes: Uint8Array): string {
  return Array.from(bytes).map((b) => b.toString(16).padStart(2, '0')).join('')
}

async function deriveKey(passphrase: string, salt: Uint8Array, iterations: number, mode: 'GCM' | 'CBC'): Promise<CryptoKey> {
  const base = await crypto.subtle.importKey('raw', ENC.encode(passphrase), 'PBKDF2', false, ['deriveKey'])
  return crypto.subtle.deriveKey(
    { name: 'PBKDF2', salt: salt as unknown as BufferSource, iterations, hash: 'SHA-256' },
    base,
    { name: mode === 'GCM' ? 'AES-GCM' : 'AES-CBC', length: 256 },
    false,
    ['encrypt', 'decrypt'],
  )
}

async function encryptText(
  plain: string,
  passphrase: string,
  mode: 'GCM' | 'CBC',
  iterations: number,
): Promise<string> {
  const salt = crypto.getRandomValues(new Uint8Array(16))
  const iv = crypto.getRandomValues(new Uint8Array(mode === 'GCM' ? 12 : 16))
  const key = await deriveKey(passphrase, salt, iterations, mode)
  const cipher = await crypto.subtle.encrypt(
    { name: mode === 'GCM' ? 'AES-GCM' : 'AES-CBC', iv: iv as unknown as BufferSource },
    key,
    ENC.encode(plain),
  )
  // 结构：salt(16) + iv + 密文
  const all = new Uint8Array(salt.length + iv.length + cipher.byteLength)
  all.set(salt, 0)
  all.set(iv, salt.length)
  all.set(new Uint8Array(cipher), salt.length + iv.length)
  return toB64(all)
}

async function decryptText(payload: string, passphrase: string, mode: 'GCM' | 'CBC', iterations: number): Promise<string> {
  const all = fromB64(payload)
  const ivLen = mode === 'GCM' ? 12 : 16
  if (all.length <= 16 + ivLen) throw new Error('密文太短，格式不对')
  const salt = all.slice(0, 16)
  const iv = all.slice(16, 16 + ivLen)
  const cipher = all.slice(16 + ivLen)
  const key = await deriveKey(passphrase, salt, iterations, mode)
  try {
    const plain = await crypto.subtle.decrypt(
      { name: mode === 'GCM' ? 'AES-GCM' : 'AES-CBC', iv: iv as unknown as BufferSource },
      key,
      cipher as unknown as BufferSource,
    )
    return DEC.decode(plain)
  } catch {
    throw new Error('解密失败：口令不对，或者密文被改过')
  }
}

async function hmac(text: string, secret: string, hash: 'SHA-256' | 'SHA-1' | 'SHA-512'): Promise<string> {
  const key = await crypto.subtle.importKey('raw', ENC.encode(secret), { name: 'HMAC', hash }, false, ['sign'])
  const sig = await crypto.subtle.sign('HMAC', key, ENC.encode(text))
  return toHex(new Uint8Array(sig))
}

const SAMPLE = '账号：wade，密码：不要外传，日期：2026-11-21'

export default function render(root: HTMLElement): void {
  let mode: 'GCM' | 'CBC' = 'GCM'
  let iterations = 150000

  const msg = el('div', { class: 'msg' })
  const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '要加密的明文，或要解密的 Base64 密文…' })
  const out = el('pre', { class: 'out', dataset: { ph: '结果…' } })
  const passInput = el('input', { class: 'inp', type: 'password', placeholder: '口令（自己记住，丢了没法恢复）', style: { flex: '1', minWidth: '220px' } })
  const showBtn = el('button', {
    class: 'btn ghost',
    text: '显示',
    onclick: () => {
      passInput.type = passInput.type === 'password' ? 'text' : 'password'
      showBtn.textContent = passInput.type === 'password' ? '显示' : '隐藏'
    },
  })
  const iterSel = el('select', { class: 'inp w-md' })
  for (const n of [10000, 100000, 150000, 300000, 600000]) {
    iterSel.append(el('option', { value: String(n), text: `${n.toLocaleString('en-US')} 次迭代` }))
  }
  iterSel.value = String(iterations)
  iterSel.addEventListener('change', () => {
    iterations = Number(iterSel.value)
  })

  let last = ''

  async function run(dir: 'enc' | 'dec'): Promise<void> {
    msg.className = 'msg'
    msg.textContent = ''
    const text = input.value
    if (!text.trim()) {
      out.textContent = ''
      return
    }
    if (!passInput.value) {
      msg.className = 'msg err'
      msg.textContent = '口令不能为空'
      return
    }
    try {
      const result = dir === 'enc'
        ? await encryptText(text, passInput.value, mode, iterations)
        : await decryptText(text.trim(), passInput.value, mode, iterations)
      last = result
      out.textContent = result
      msg.className = 'msg ok'
      msg.textContent = dir === 'enc' ? `已用 AES-${mode} 加密，结果含 salt 与 iv，直接整段带走` : '解密成功'
    } catch (err) {
      out.textContent = ''
      last = ''
      msg.className = 'msg err'
      msg.textContent = (err as Error).message
    }
  }

  const bar = toolbar(
    seg<'GCM' | 'CBC'>([{ label: 'AES-GCM', value: 'GCM' }, { label: 'AES-CBC', value: 'CBC' }], mode, (v) => {
      mode = v
    }),
    iterSel,
    el('span', { class: 'spacer' }),
    btn('示例', () => {
      input.value = SAMPLE
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      out.textContent = ''
      msg.textContent = ''
      msg.className = 'msg'
    }, 'ghost'),
  )

  const passRow = el('div', { class: 'card-box row', style: { marginBottom: '12px' } },
    el('span', { class: 'hint', text: '口令' }), passInput, showBtn,
    btn('加密 →', () => void run('enc'), 'primary'),
    btn('← 解密', () => void run('dec')),
  )

  const inputPane = pane('输入', input, { foot: foot(el('span', { text: '口令只在本页内存里参与计算，不会被保存' })) })
  const outPane = pane('输出', out, {
    actions: [
      btn('复制', () => copy(last || out.textContent || '')),
      btn('拿去当输入', () => {
        input.value = last || out.textContent || ''
        toast('已填入左侧')
      }),
    ],
  })

  /* ---------- HMAC ---------- */

  const hInput = el('textarea', { class: 'ta', spellcheck: false, placeholder: '要签名的内容…', style: { minHeight: '90px' } })
  const hKey = el('input', { class: 'inp', placeholder: 'HMAC 密钥', style: { flex: '1' } })
  const hHash = el('select', { class: 'inp w-md' })
  for (const h of ['SHA-256', 'SHA-1', 'SHA-512']) hHash.append(el('option', { value: h, text: h }))
  const hOut = el('pre', { class: 'out', dataset: { ph: '签名结果（Hex）…' }, style: { minHeight: '80px' } })
  let hValue = ''

  const hmacPane = pane('HMAC 签名', hOut, {
    actions: [
      btn('签名', () => {
        if (!hKey.value) return toast('先填密钥')
        void hmac(hInput.value, hKey.value, hHash.value as 'SHA-256').then((sig) => {
          hValue = sig
          hOut.textContent = sig
        })
      }, 'primary'),
      btn('复制', () => copy(hValue)),
    ],
    foot: foot(el('span', { text: 'Hex 小写输出，可直接和网关返回的签名比对' })),
  })

  root.append(
    msg,
    bar,
    passRow,
    el('div', { class: 'panes' }, inputPane, outPane),
    el('div', { class: 'card-box stack', style: { marginTop: '16px' } },
      el('div', { class: 'row' }, hKey, hHash),
      hInput,
      hmacPane,
    ),
  )
  input.focus()
}
