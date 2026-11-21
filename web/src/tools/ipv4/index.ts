import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

function ipToInt(ip: string): number {
  const parts = ip.trim().split('.')
  if (parts.length !== 4) throw new Error('IPv4 需要四段，比如 192.168.1.10')
  let n = 0
  for (const part of parts) {
    const v = Number(part)
    if (!/^\d+$/.test(part) || v < 0 || v > 255) throw new Error(`每段必须是 0–255：${part}`)
    n = (n << 8) + v
  }
  return n >>> 0
}

function intToIp(n: number): string {
  return [(n >>> 24) & 255, (n >>> 16) & 255, (n >>> 8) & 255, n & 255].join('.')
}

function toBin(n: number): string {
  return [(n >>> 24) & 255, (n >>> 16) & 255, (n >>> 8) & 255, n & 255]
    .map((b) => b.toString(2).padStart(8, '0'))
    .join('.')
}

function maskFromCidr(cidr: number): number {
  return cidr === 0 ? 0 : (0xffffffff << (32 - cidr)) >>> 0
}

const CIDR_TABLE = [8, 16, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32]

function isPrivate(n: number): boolean {
  const a = n >>> 24
  const b = (n >>> 16) & 255
  if (a === 10) return true
  if (a === 172 && b >= 16 && b <= 31) return true
  if (a === 192 && b === 168) return true
  if (a === 127) return true
  if (a === 169 && b === 254) return true
  if (a === 100 && b >= 64 && b <= 127) return true
  return false
}

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const input = el('input', {
    class: 'inp mono',
    style: { flex: '1', minWidth: '240px', fontSize: '14px' },
    value: ctx.src ?? '192.168.1.10/24',
    spellcheck: false,
  })
  const msg = el('div', { class: 'msg' })
  const kvBox = el('div', { class: 'kv', style: { marginBottom: '12px' } })
  const tableHost = el('div')
  const ulaOut = el('pre', { class: 'out', dataset: { ph: '点「生成」得到一段 fd00::/8 的 ULA…' }, style: { minHeight: '70px' } })

  function run(): void {
    msg.className = 'msg'
    msg.textContent = ''
    kvBox.innerHTML = ''
    tableHost.innerHTML = ''
    const raw = input.value.trim()
    if (!raw) return

    const [ipPart, cidrPart] = raw.split('/')
    let ip: number
    let cidr = 24
    try {
      ip = ipToInt(ipPart)
      if (cidrPart !== undefined) {
        cidr = Number(cidrPart)
        if (!Number.isInteger(cidr) || cidr < 0 || cidr > 32) throw new Error('CIDR 前缀要在 0–32 之间')
      }
    } catch (err) {
      msg.className = 'msg err'
      msg.textContent = (err as Error).message
      return
    }

    const mask = maskFromCidr(cidr)
    const network = (ip & mask) >>> 0
    const broadcast = (network | (~mask >>> 0)) >>> 0
    const total = 2 ** (32 - cidr)
    const usable = cidr >= 31 ? total : Math.max(0, total - 2)
    const first = cidr >= 31 ? network : network + 1
    const last = cidr >= 31 ? broadcast : broadcast - 1

    const pairs: Array<[string, string]> = [
      ['网络地址', intToIp(network)],
      ['广播地址', intToIp(broadcast)],
      ['子网掩码', intToIp(mask)],
      ['反掩码（通配符）', intToIp(~mask >>> 0)],
      ['前缀长度', `/${cidr}`],
      ['可用地址段', usable ? `${intToIp(first)} – ${intToIp(last)}` : '（无）'],
      ['地址总数', total.toLocaleString('en-US')],
      ['可用主机数', usable.toLocaleString('en-US')],
      ['类型', isPrivate(ip) ? '私有 / 内网地址' : '公网地址'],
      ['整数表示', String(ip)],
      ['二进制', toBin(ip)],
    ]

    for (const [k, v] of pairs) {
      kvBox.append(el('div', { text: k }), el('div', { class: 'copyable', text: v, title: '点击复制', onclick: () => copy(v) }))
    }

    // 常见前缀表
    const table = el('table', { class: 'data' })
    table.append(
      el('thead', null, el('tr', null, el('th', { text: '前缀' }), el('th', { text: '掩码' }), el('th', { text: '地址数' }), el('th', { text: '可用主机' }))),
    )
    table.append(
      el('tbody', null, ...CIDR_TABLE.map((c) => {
        const m = maskFromCidr(c)
        const count = 2 ** (32 - c)
        return el('tr', null,
          el('td', { class: 'k', text: `/${c}` }),
          el('td', { class: 'v', text: intToIp(m) }),
          el('td', { class: 'v', text: count.toLocaleString('en-US') }),
          el('td', { class: 'v', text: Math.max(0, count - 2).toLocaleString('en-US') }),
        )
      })),
    )
    tableHost.append(el('div', { class: 'group-title', text: '常见前缀对照' }), el('div', { class: 'table-wrap' }, table))
  }

  function genUla(): void {
    const bytes = crypto.getRandomValues(new Uint8Array(5))
    bytes[0] = 0xfd
    const hex = Array.from(bytes).map((b) => b.toString(16).padStart(2, '0')).join('')
    const groups = hex.match(/.{4}/g) ?? []
    const addr = `${groups[0]}:${groups[1]}:${groups[2]}:${groups[3]}:${groups[4]}::1`
    ulaOut.textContent = `${addr}\n\n# 可用范围 fd00::/8，本地唯一地址，不经过公网`
  }

  input.addEventListener('input', run)
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') run()
  })

  root.append(
    msg,
    toolbar(input, btn('计算', run, 'primary'), btn('清空', () => { input.value = ''; run(); input.focus() }, 'ghost')),

    kvBox,
    tableHost,
    el('div', { style: { marginTop: '16px' } },
      pane('IPv6 ULA 生成', ulaOut, {
        actions: [btn('生成', genUla), btn('复制', () => copy(ulaOut.textContent ?? ''))],
        foot: foot(el('span', { text: '纯本地随机生成，不需要联网' })),
      }),
    ),
  )
  run()
}
