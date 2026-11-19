import { el, kv } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, pane, seg, toolbar } from '../../core/ui'

type Unit = 'auto' | 's' | 'ms'

const COMMON_ZONES = [
  'Asia/Shanghai',
  'UTC',
  'Asia/Tokyo',
  'Asia/Singapore',
  'Asia/Kolkata',
  'Europe/London',
  'Europe/Berlin',
  'America/New_York',
  'America/Los_Angeles',
  'Australia/Sydney',
]

const WEEK = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']
const p2 = (n: number): string => String(n).padStart(2, '0')

function fmtLocal(d: Date): string {
  return `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())} ${p2(d.getHours())}:${p2(d.getMinutes())}:${p2(d.getSeconds())}`
}

function relText(ms: number): string {
  const diff = ms - Date.now()
  const abs = Math.abs(diff)
  if (abs < 1000) return '就是现在'
  const units: Array<[number, string]> = [
    [31536000000, '年'],
    [2592000000, '个月'],
    [86400000, '天'],
    [3600000, '小时'],
    [60000, '分钟'],
    [1000, '秒'],
  ]
  for (const [size, name] of units) {
    if (abs >= size) return `${Math.round(abs / size)} ${name}${diff < 0 ? '前' : '后'}`
  }
  return '就是现在'
}

function zoneTime(d: Date, zone: string): string {
  try {
    return new Intl.DateTimeFormat('zh-CN', {
      timeZone: zone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    }).format(d)
  } catch {
    return '时区无效'
  }
}

function parseInput(raw: string, unit: Unit): number | null {
  const s = raw.trim()
  if (!s) return null

  if (/^-?\d+(\.\d+)?$/.test(s)) {
    const n = Number(s)
    const digits = s.replace(/[-.]/g, '').split('.')[0]?.length ?? 0
    if (unit === 's') return n * 1000
    if (unit === 'ms') return n
    return digits <= 10 ? n * 1000 : n
  }

  const t = Date.parse(s.replace(' ', 'T'))
  return Number.isNaN(t) ? null : t
}

export default function render(root: HTMLElement) {
  let unit: Unit = 'auto'
  let zone = 'Asia/Shanghai'

  /* ---------- 当前时间 ---------- */
  const nowBig = el('div', {
    class: 'mono',
    style: { fontSize: '24px', fontWeight: '600', letterSpacing: '-0.02em', lineHeight: '1.3' },
    text: '—',
  })
  const nowSec = el('span', { class: 'mono', text: '' })
  const nowMs = el('span', { class: 'mono', text: '' })

  const nowCard = el(
    'div',
    { class: 'card-box', style: { marginBottom: '12px' } },
    el(
      'div',
      { class: 'row', style: { marginBottom: '6px' } },
      nowBig,
      el('span', { class: 'hint', text: '本地时间 · 每秒刷新' }),
    ),
    el(
      'div',
      { class: 'row' },
      el('span', { class: 'hint', text: '秒' }),
      nowSec,
      btn('复制', () => copy(nowSec.textContent ?? ''), 'ghost'),
      el('span', { style: { width: '14px' } }),
      el('span', { class: 'hint', text: '毫秒' }),
      nowMs,
      btn('复制', () => copy(nowMs.textContent ?? ''), 'ghost'),
    ),
  )

  /* ---------- 输入 / 结果 ---------- */
  const input = el('input', {
    class: 'inp',
    type: 'text',
    style: { width: '100%', height: '32px' },
    placeholder: '时间戳或日期字符串，例：1763600000 / 2026-11-19 11:00:00',
  })

  const result = el('div', { class: 'stack' })
  const msg = el('div', { class: 'msg' })

  const zoneSelect = el('select', { class: 'inp' })
  for (const z of COMMON_ZONES) zoneSelect.append(el('option', { value: z, text: z }))
  const all = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone')
  if (all?.length) {
    const group = el('optgroup', { label: '全部 IANA 时区' })
    for (const z of all) {
      if (COMMON_ZONES.includes(z)) continue
      group.append(el('option', { value: z, text: z }))
    }
    zoneSelect.append(group)
  }
  zoneSelect.value = zone
  zoneSelect.addEventListener('change', () => {
    zone = zoneSelect.value
    update()
  })

  function update(): void {
    const ms = parseInput(input.value, unit)
    result.innerHTML = ''

    if (input.value.trim() && ms === null) {
      msg.className = 'msg err'
      msg.textContent = '看不懂这个输入：既不是数字时间戳，也不是能识别的日期'
      return
    }
    msg.className = 'msg'
    msg.textContent = ''
    if (ms === null) return

    const d = new Date(ms)
    if (Number.isNaN(d.getTime())) {
      msg.className = 'msg err'
      msg.textContent = '日期超出可表示范围'
      return
    }

    result.append(
      kv([
        ['本地时间', fmtLocal(d)],
        [zone, zoneTime(d, zone)],
        ['ISO 8601', d.toISOString()],
        ['UTC', d.toUTCString()],
        ['星期', WEEK[d.getDay()]],
        ['相对现在', relText(ms)],
        ['Unix 秒', String(Math.floor(ms / 1000))],
        ['Unix 毫秒', String(ms)],
        ['时区偏移', `UTC${-d.getTimezoneOffset() >= 0 ? '+' : '-'}${Math.abs(d.getTimezoneOffset() / 60)}`],
      ]),
      el(
        'div',
        { class: 'row' },
        btn('复制秒', () => copy(String(Math.floor(ms / 1000)))),
        btn('复制毫秒', () => copy(String(ms))),
        btn('复制 ISO', () => copy(d.toISOString())),
      ),
    )
  }

  input.addEventListener('input', update)
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') update()
  })

  const bar = toolbar(
    seg<Unit>(
      [
        { label: '自动识别', value: 'auto' },
        { label: '按秒', value: 's' },
        { label: '按毫秒', value: 'ms' },
      ],
      'auto',
      (v) => {
        unit = v
        update()
      },
    ),
    el('span', { class: 'hint', text: '时区' }),
    zoneSelect,
    el('span', { class: 'spacer' }),
    btn('填入当前时间', () => {
      input.value = String(Math.floor(Date.now() / 1000))
      unit = 'auto'
      update()
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      update()
    }, 'ghost'),
  )

  /* ---------- 日期 → 时间戳 ---------- */
  const dtPicker = el('input', { class: 'inp', type: 'datetime-local', step: '1' })
  const dtOut = el('div', { class: 'mono', style: { fontSize: '12.5px' }, text: '' })

  dtPicker.addEventListener('input', () => {
    if (!dtPicker.value) {
      dtOut.textContent = ''
      return
    }
    const ms = new Date(dtPicker.value).getTime()
    if (Number.isNaN(ms)) {
      dtOut.textContent = '无效时间'
      return
    }
    dtOut.textContent = `秒 ${Math.floor(ms / 1000)}　·　毫秒 ${ms}　·　ISO ${new Date(ms).toISOString()}`
  })

  const reverseCard = el(
    'div',
    { class: 'card-box', style: { marginTop: '12px' } },
    el('div', { class: 'hint', style: { marginBottom: '8px' }, text: '反查：选一个日期时间，得到时间戳' }),
    el('div', { class: 'row' }, dtPicker, dtOut),
  )

  /** 常用时间点快捷入口，省得自己算 */
  const quickBox = el('div', { class: 'row', style: { marginTop: '12px', gap: '6px' } })
  const quickPoints: Array<[string, () => number]> = [
    ['现在', () => Date.now()],
    ['1 小时前', () => Date.now() - 3600_000],
    ['今天 00:00', () => new Date(new Date().setHours(0, 0, 0, 0)).getTime()],
    ['明天此刻', () => Date.now() + 86400_000],
    ['7 天前', () => Date.now() - 7 * 86400_000],
  ]
  quickBox.append(el('span', { class: 'hint', text: '快捷：' }))
  for (const [label, get] of quickPoints) {
    quickBox.append(
      btn(label, () => {
        input.value = String(Math.floor(get() / 1000))
        update()
      }, 'ghost'),
    )
  }

  root.append(
    nowCard,
    msg,
    bar,
    el(
      'div',
      { class: 'panes' },
      pane('输入', el('div', { style: { padding: '12px' } }, input, quickBox), {
        foot: el('div', { class: 'pane-foot' }, el('span', { text: '数字默认按位数判断秒 / 毫秒' })),
      }),
      pane('结果', el('div', { style: { padding: '12px', overflow: 'auto' } }, result)),
    ),
    reverseCard,
  )

  const tick = (): void => {
    const d = new Date()
    nowBig.textContent = fmtLocal(d)
    nowSec.textContent = String(Math.floor(d.getTime() / 1000))
    nowMs.textContent = String(d.getTime())
  }
  tick()
  const timer = window.setInterval(tick, 1000)

  input.value = String(Math.floor(Date.now() / 1000))
  update()
  input.focus()

  return () => window.clearInterval(timer)
}
