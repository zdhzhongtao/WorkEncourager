import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, toolbar } from '../../core/ui'

const DAY = 86400e3

function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function fmt(d: Date): string {
  return d.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

function addMonths(d: Date, months: number): Date {
  const out = new Date(d)
  const targetMonth = out.getMonth() + months
  const dayOfMonth = out.getDate()
  out.setDate(1)
  out.setMonth(targetMonth)
  const lastDay = new Date(out.getFullYear(), out.getMonth() + 1, 0).getDate()
  out.setDate(Math.min(dayOfMonth, lastDay))
  return out
}

function diffParts(a: Date, b: Date): { days: number; weeks: number; months: number; years: number; hours: number } {
  const ms = b.getTime() - a.getTime()
  const days = Math.round(ms / DAY)
  let months = (b.getFullYear() - a.getFullYear()) * 12 + (b.getMonth() - a.getMonth())
  if (b.getDate() < a.getDate()) months--
  return {
    days,
    weeks: Math.floor(days / 7),
    months,
    years: Math.floor(months / 12),
    hours: Math.round(ms / 3600e3),
  }
}

export default function render(root: HTMLElement) {
  /* ---------------- 加减 ---------------- */

  const base = el('input', { class: 'inp w-md', type: 'datetime-local', value: toLocalInput(new Date()) })
  const amount = el('input', { class: 'inp w-sm', type: 'number', value: '7' })
  const unit = el('select', { class: 'inp w-md' })
  for (const u of ['分钟', '小时', '天', '周', '月', '年']) unit.append(el('option', { value: u, text: u }))
  unit.value = '天'
  const resultBox = el('div', { class: 'card-box', style: { marginTop: '10px' } })

  function calcAdd(): void {
    resultBox.innerHTML = ''
    const start = new Date(base.value)
    if (Number.isNaN(start.getTime())) {
      resultBox.append(el('div', { class: 'msg err', text: '基准时间不合法' }))
      return
    }
    const n = Number(amount.value) || 0
    const out = new Date(start)
    switch (unit.value) {
      case '分钟':
        out.setMinutes(out.getMinutes() + n)
        break
      case '小时':
        out.setHours(out.getHours() + n)
        break
      case '天':
        out.setDate(out.getDate() + n)
        break
      case '周':
        out.setDate(out.getDate() + n * 7)
        break
      case '月':
        out.setTime(addMonths(start, n).getTime())
        break
      case '年':
        out.setTime(addMonths(start, n * 12).getTime())
        break
    }
    resultBox.append(
      el('div', { class: 'row' },
        el('span', { class: 'hint', text: `${n >= 0 ? '之后' : '之前'} ${Math.abs(n)} ${unit.value}` }),
        el('strong', { class: 'mono', text: fmt(out) }),
        el('span', { class: 'spacer' }),
        btn('复制 ISO', () => copy(out.toISOString()), 'ghost'),
        btn('复制时间戳', () => copy(String(Math.floor(out.getTime() / 1000))), 'ghost'),
      ),
    )
  }

  base.addEventListener('change', calcAdd)
  amount.addEventListener('input', calcAdd)
  unit.addEventListener('change', calcAdd)

  /* ---------------- 间隔 ---------------- */

  const fromInput = el('input', { class: 'inp w-md', type: 'datetime-local', value: toLocalInput(new Date()) })
  const toInput = el('input', { class: 'inp w-md', type: 'datetime-local', value: toLocalInput(new Date(Date.now() + 30 * DAY)) })
  const gapBox = el('div', { class: 'stat-grid', style: { marginTop: '10px' } })

  function calcGap(): void {
    gapBox.innerHTML = ''
    const a = new Date(fromInput.value)
    const b = new Date(toInput.value)
    if (Number.isNaN(a.getTime()) || Number.isNaN(b.getTime())) return
    const p = diffParts(a, b)
    const items: Array<[string, string]> = [
      ['总天数', `${p.days}`],
      ['整周', `${p.weeks}`],
      ['整月', `${p.months}`],
      ['整年', `${p.years}`],
      ['小时', `${p.hours}`],
      ['秒', `${Math.round((b.getTime() - a.getTime()) / 1000).toLocaleString('en-US')}`],
    ]
    for (const [k, v] of items) gapBox.append(el('div', { class: 'stat' }, el('b', { text: v }), el('span', { text: k })))
  }

  fromInput.addEventListener('change', calcGap)
  toInput.addEventListener('change', calcGap)

  /* ---------------- 倒计时 ---------------- */

  const targetInput = el('input', { class: 'inp w-md', type: 'datetime-local', value: toLocalInput(new Date(Date.now() + 7 * DAY)) })
  const countdown = el('div', { class: 'stat-grid', style: { marginTop: '10px' } })
  let timer = 0

  function tick(): void {
    countdown.innerHTML = ''
    const target = new Date(targetInput.value)
    if (Number.isNaN(target.getTime())) return
    const ms = target.getTime() - Date.now()
    const abs = Math.abs(ms)
    const d = Math.floor(abs / DAY)
    const h = Math.floor((abs % DAY) / 3600e3)
    const m = Math.floor((abs % 3600e3) / 60e3)
    const s = Math.floor((abs % 60e3) / 1000)
    const prefix = ms >= 0 ? '还剩' : '已过去'
    for (const [v, k] of [[`${d}`, '天'], [`${h}`, '小时'], [`${m}`, '分'], [`${s}`, '秒']] as Array<[string, string]>) {
      countdown.append(el('div', { class: 'stat' }, el('b', { text: v }), el('span', { text: `${prefix}${k}` })))
    }
  }

  targetInput.addEventListener('change', tick)
  timer = window.setInterval(tick, 1000)

  /* ---------------- 布局 ---------------- */

  const card = (title: string, desc: string, ...children: HTMLElement[]) =>
    el('div', { class: 'card-box', style: { marginBottom: '12px' } },
      el('div', { class: 'row', style: { marginBottom: '8px' } }, el('strong', { text: title })),
      el('div', { class: 'hint', style: { marginBottom: '8px' } }, desc),
      ...children,
    )

  root.append(
    card('日期加减', '按天 / 周 / 月 / 年推进，跨月末自动贴合（1 月 31 日 + 1 月 = 2 月 28 日）',
      toolbar(el('span', { class: 'hint', text: '基准' }), base, amount, unit, btn('计算', calcAdd, 'primary')),
      resultBox,
    ),
    card('日期间隔', '两个时间点之间差多少天、周、月、年',
      toolbar(el('span', { class: 'hint', text: '从' }), fromInput, el('span', { class: 'hint', text: '到' }), toInput, btn('计算', calcGap, 'primary'),
        btn('交换', () => {
          const t = fromInput.value
          fromInput.value = toInput.value
          toInput.value = t
          calcGap()
        }, 'ghost')),
      gapBox,
    ),
    card('倒计时', '每秒刷新，正数倒数、负数算已经过去多久',
      toolbar(el('span', { class: 'hint', text: '目标' }), targetInput),
      countdown,
    ),
  )

  calcAdd()
  calcGap()
  tick()

  return () => window.clearInterval(timer)
}
