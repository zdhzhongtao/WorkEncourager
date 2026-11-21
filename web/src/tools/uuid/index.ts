import { el } from '../../core/dom'
import { copy, download } from '../../core/kit'
import { btn, check, foot, pane, seg, toolbar } from '../../core/ui'

type Mode = 'uuid' | 'nanoid' | 'ulid' | 'snowflake'

const ALPHABETS: Array<{ value: string; label: string; chars: string }> = [
  { value: 'url', label: 'URL 安全（默认）', chars: 'useandom-26T198340PX75pxJACKVERYMINDBUSHWOLF_GQZbfghjklqvwyzrict' },
  { value: 'alnum', label: '字母 + 数字', chars: '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz' },
  { value: 'hex', label: '十六进制', chars: '0123456789abcdef' },
  { value: 'digits', label: '纯数字', chars: '0123456789' },
]

function uuidv4(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  const b = new Uint8Array(16)
  crypto.getRandomValues(b)
  b[6] = ((b[6] as number) & 0x0f) | 0x40
  b[8] = ((b[8] as number) & 0x3f) | 0x80
  const hex = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

/** 无模偏差的随机串生成（拒绝采样） */
function randomId(size: number, alphabet: string): string {
  const len = alphabet.length
  const limit = 256 - (256 % len)
  const buf = new Uint8Array(size * 2)
  let out = ''
  while (out.length < size) {
    crypto.getRandomValues(buf)
    for (const b of buf) {
      if (b >= limit) continue
      out += alphabet[b % len]
      if (out.length === size) break
    }
  }
  return out
}

const clamp = (n: number, min: number, max: number): number => Math.min(max, Math.max(min, n))

/* ULID：10 位时间戳 + 16 位随机，Crockford Base32 */
const CROCKFORD = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'

function ulid(): string {
  let t = Date.now()
  let time = ''
  for (let i = 0; i < 10; i++) {
    time = CROCKFORD[t % 32] + time
    t = Math.floor(t / 32)
  }
  return time + randomId(16, CROCKFORD)
}

/* Snowflake：41 位毫秒时间戳 + 10 位机器号 + 12 位序列 */
const EPOCH = 1700000000000n
let seq = 0
let lastMs = 0

function snowflake(worker = 1): string {
  let now = Date.now()
  if (now === lastMs) {
    seq = (seq + 1) & 0xfff
    if (seq === 0) {
      while (Date.now() === now) {
        /* 同毫秒序列用尽，等下一毫秒 */
      }
      now = Date.now()
    }
  } else {
    seq = 0
  }
  lastMs = now
  const value = (BigInt(now) - EPOCH) << 22n | (BigInt(worker & 0x3ff) << 12n) | BigInt(seq)
  return value.toString()
}

export default function render(root: HTMLElement): void {
  let mode: Mode = 'uuid'
  let upper = false
  let items: string[] = []

  const countInput = el('input', { class: 'inp w-sm', type: 'number', min: '1', max: '200', value: '5' })
  const lenInput = el('input', { class: 'inp w-sm', type: 'number', min: '1', max: '128', value: '21' })
  const alphaSelect = el('select', { class: 'inp w-md' })
  for (const a of ALPHABETS) alphaSelect.append(el('option', { value: a.value, text: a.label }))

  const nanoOpts = el(
    'div',
    { class: 'row' },
    el('span', { class: 'hint', text: '长度' }),
    lenInput,
    el('span', { class: 'hint', text: '字母表' }),
    alphaSelect,
  )

  const listHost = el('div', { class: 'stack', style: { maxHeight: '460px', overflow: 'auto' } })
  const stat = el('span', { text: '' })

  function currentAlphabet(): string {
    const hit = ALPHABETS.find((a) => a.value === alphaSelect.value)
    return hit?.chars ?? ALPHABETS[0]!.chars
  }

  function generate(): void {
    const count = clamp(Math.floor(Number(countInput.value) || 1), 1, 200)
    countInput.value = String(count)
    const size = clamp(Math.floor(Number(lenInput.value) || 21), 1, 128)

    items = []
    for (let i = 0; i < count; i++) {
      if (mode === 'uuid') items.push(uuidv4())
      else if (mode === 'nanoid') items.push(randomId(size, currentAlphabet()))
      else if (mode === 'ulid') items.push(ulid())
      else items.push(snowflake())
    }
    if (upper && mode !== 'ulid') items = items.map((s) => s.toUpperCase())

    listHost.innerHTML = ''
    const body = el('tbody', null)
    items.forEach((value, i) => {
      body.append(
        el(
          'tr',
          null,
          el('td', { class: 'k', text: String(i + 1) }),
          el('td', { class: 'v', text: value, title: '点击复制', style: { cursor: 'pointer' }, onclick: () => copy(value) }),
          el('td', { text: value.length + ' 位' }),
        ),
      )
    })
    listHost.append(
      el(
        'table',
        { class: 'data' },
        el(
          'thead',
          null,
          el('tr', null, el('th', { text: '#' }), el('th', { text: '生成结果（点击复制）' }), el('th', { text: '长度' })),
        ),
        body,
      ),
    )
    stat.textContent = `已生成 ${items.length} 个`
  }

  const modeSeg = seg<Mode>(
    [
      { label: 'UUID v4', value: 'uuid' },
      { label: 'NanoID', value: 'nanoid' },
      { label: 'ULID', value: 'ulid' },
      { label: 'Snowflake', value: 'snowflake' },
    ],
    'uuid',
    (v) => {
      mode = v
      nanoOpts.style.display = v === 'nanoid' ? '' : 'none'
      generate()
    },
  )

  nanoOpts.style.display = 'none'
  lenInput.addEventListener('change', generate)
  alphaSelect.addEventListener('change', generate)

  const bar = toolbar(
    modeSeg,
    el('span', { class: 'hint', text: '数量' }),
    countInput,
    nanoOpts,
    el('span', { class: 'spacer' }),
    check('大写', false, (v) => {
      upper = v
      generate()
    }),
    btn('重新生成', generate, 'primary'),
  )

  countInput.addEventListener('change', generate)

  root.append(
    bar,
    pane('结果', listHost, {
      actions: [
        btn('复制全部', () => copy(items.join('\n'), `已复制 ${items.length} 个`)),
        btn('下载 txt', () => download('ids.txt', items.join('\n'))),
      ],
      foot: foot(stat),
    }),
    el(
      'div',
      { class: 'hint', style: { marginTop: '10px' } },
      'UUID v4 / NanoID / ULID 都由浏览器 crypto.getRandomValues 生成；Snowflake 用本地时钟，只是时间有序的 64 位整数，不作分布式唯一性保证。',
    ),
  )

  generate()
}
