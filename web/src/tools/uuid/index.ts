import { el } from '../../core/dom'
import { copy, download } from '../../core/kit'
import { btn, check, foot, pane, seg, toolbar } from '../../core/ui'

type Mode = 'uuid' | 'nanoid'

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
      items.push(mode === 'uuid' ? uuidv4() : randomId(size, currentAlphabet()))
    }
    if (upper) items = items.map((s) => s.toUpperCase())

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
      'UUID v4 与 NanoID 都由浏览器 crypto.getRandomValues 生成，不经过任何网络请求。',
    ),
  )

  generate()
}
