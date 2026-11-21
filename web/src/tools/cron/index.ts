import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, toolbar } from '../../core/ui'

interface FieldSpec {
  name: string
  min: number
  max: number
  label: string
}

const FIELDS: FieldSpec[] = [
  { name: '分钟', min: 0, max: 59, label: 'minute' },
  { name: '小时', min: 0, max: 23, label: 'hour' },
  { name: '日', min: 1, max: 31, label: 'day' },
  { name: '月', min: 1, max: 12, label: 'month' },
  { name: '星期', min: 0, max: 7, label: 'weekday' },
]

const PRESETS: Array<{ label: string; expr: string }> = [
  { label: '每分钟', expr: '* * * * *' },
  { label: '每 5 分钟', expr: '*/5 * * * *' },
  { label: '每小时整点', expr: '0 * * * *' },
  { label: '每天 0 点', expr: '0 0 * * *' },
  { label: '每天 9 点', expr: '0 9 * * *' },
  { label: '工作日 9 点', expr: '0 9 * * 1-5' },
  { label: '每周一 9 点', expr: '0 9 * * 1' },
  { label: '每月 1 号', expr: '0 0 1 * *' },
  { label: '每季度首日', expr: '0 0 1 1,4,7,10 *' },
]

function parseField(spec: FieldSpec, raw: string): Set<number> {
  const out = new Set<number>()
  const push = (n: number) => {
    if (n < spec.min || n > spec.max) throw new Error(`${spec.name}字段超出范围：${n}`)
    out.add(n)
  }

  for (const part of raw.split(',')) {
    const stepMatch = /^(.+)\/(\d+)$/.exec(part)
    const step = stepMatch ? Number(stepMatch[2]) : 1
    const body = stepMatch ? stepMatch[1] : part

    if (step <= 0) throw new Error('步长必须是正整数')

    if (body === '*') {
      for (let n = spec.min; n <= spec.max; n += step) push(n)
      continue
    }

    const range = /^(\d+)-(\d+)$/.exec(body)
    if (range) {
      const from = Number(range[1])
      const to = Number(range[2])
      if (from > to) throw new Error(`${spec.name}字段范围写反了：${body}`)
      for (let n = from; n <= to; n += step) push(n)
      continue
    }

    if (/^\d+$/.test(body)) {
      push(Number(body))
      continue
    }

    throw new Error(`${spec.name}字段看不懂：${part}`)
  }

  if (spec.label === 'weekday' && out.has(7)) {
    out.delete(7)
    out.add(0)
  }
  return out
}

function nextRuns(expr: string, count = 10): Date[] {
  const parts = expr.trim().split(/\s+/)
  if (parts.length !== 5) throw new Error('需要 5 段：分 时 日 月 星期')
  const sets = FIELDS.map((f, i) => parseField(f, parts[i]))

  const [minutes, hours, days, months, weekdays] = sets
  const dayRestricted = parts[2] !== '*'
  const weekdayRestricted = parts[4] !== '*'

  const results: Date[] = []
  const cursor = new Date()
  cursor.setSeconds(0, 0)
  cursor.setMinutes(cursor.getMinutes() + 1)

  const limit = new Date(cursor.getTime() + 4 * 365 * 86400e3)

  while (cursor <= limit && results.length < count) {
    if (!months.has(cursor.getMonth() + 1)) {
      cursor.setMonth(cursor.getMonth() + 1, 1)
      cursor.setHours(0, 0, 0, 0)
      continue
    }
    if (!days.has(cursor.getDate())) {
      cursor.setDate(cursor.getDate() + 1)
      cursor.setHours(0, 0, 0, 0)
      continue
    }
    const wd = cursor.getDay()
    // 标准 cron：日与星期同时限定取「或」，只限一个时取「与」
    const dayOk = days.has(cursor.getDate())
    const weekOk = weekdays.has(wd)
    const matched = dayRestricted && weekdayRestricted ? dayOk || weekOk : dayOk && weekOk
    if (!matched) {
      cursor.setDate(cursor.getDate() + 1)
      cursor.setHours(0, 0, 0, 0)
      continue
    }
    if (!hours.has(cursor.getHours())) {
      cursor.setHours(cursor.getHours() + 1, 0, 0, 0)
      continue
    }
    if (!minutes.has(cursor.getMinutes())) {
      cursor.setMinutes(cursor.getMinutes() + 1, 0, 0)
      continue
    }
    results.push(new Date(cursor))
    cursor.setMinutes(cursor.getMinutes() + 1, 0, 0)
  }

  if (!results.length) throw new Error('未来 4 年内都不会触发，检查一下表达式')
  return results
}

function explain(expr: string): string {
  const parts = expr.trim().split(/\s+/)
  if (parts.length !== 5) return ''
  const seg = (raw: string, unit: string, min: number) => {
    if (raw === '*') return `每${unit}`
    if (raw.startsWith('*/')) return `每 ${raw.slice(2)} ${unit}`
    if (raw.includes('-')) return `${raw} 之间每${unit}`
    if (raw.includes(',')) return `第 ${raw} ${unit}`
    return `第 ${Number(raw) - (unit === '分钟' || unit === '小时' ? 0 : min - 1)} ${unit}`
  }
  return `${seg(parts[0], '分钟', 0)}，${seg(parts[1], '小时', 0)}，${seg(parts[2], '日', 1)}，${seg(parts[3], '月', 1)}，星期 ${parts[4] === '*' ? '每天' : parts[4]}`
}

const FMT = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const input = el('input', {
    class: 'inp mono',
    style: { flex: '1', minWidth: '260px', fontSize: '14px' },
    value: ctx.src ?? '0 9 * * 1-5',
    spellcheck: false,
  })
  const msg = el('div', { class: 'msg' })
  const explainBox = el('div', { class: 'card-box', style: { marginBottom: '12px' } })
  const listHost = el('div')
  const chips = el('div', { class: 'chips' })

  function run(): void {
    msg.className = 'msg'
    msg.textContent = ''
    explainBox.innerHTML = ''
    listHost.innerHTML = ''
    const expr = input.value.trim()
    if (!expr) return

    try {
      const text = explain(expr)
      if (text) explainBox.append(el('div', { class: 'hint', text: `通俗解释：${text}` }))

      const runs = nextRuns(expr, 10)
      const table = el('table', { class: 'data' })
      table.append(el('thead', null, el('tr', null, el('th', { text: '#' }), el('th', { text: '执行时间' }), el('th', { text: '距今' }))))
      const tbody = el('tbody')
      const now = Date.now()
      runs.forEach((d, i) => {
        const diffMin = Math.round((d.getTime() - now) / 60000)
        const rel = diffMin < 60
          ? `${diffMin} 分钟后`
          : diffMin < 1440
            ? `${Math.round(diffMin / 60)} 小时后`
            : `${Math.round(diffMin / 1440)} 天后`
        tbody.append(el('tr', null, el('td', { class: 'k', text: String(i + 1) }), el('td', { class: 'v', text: FMT.format(d) }), el('td', { class: 'hint', text: rel })))
      })
      table.append(tbody)
      listHost.append(el('div', { class: 'table-wrap', style: { maxHeight: 'none' } }, table))
    } catch (err) {
      msg.className = 'msg err'
      msg.textContent = (err as Error).message
    }
  }

  function paintChips(): void {
    chips.innerHTML = ''
    for (const p of PRESETS) {
      chips.append(
        el('button', {
          class: 'chip',
          text: p.label,
          onclick: () => {
            input.value = p.expr
            run()
          },
        }),
      )
    }
  }

  input.addEventListener('input', run)
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') run()
  })

  const fieldHint = el(
    'div',
    { class: 'kv', style: { marginBottom: '12px' } },
    ...['分钟 0-59', '小时 0-23', '日 1-31', '月 1-12', '星期 0-7（0 和 7 都是周日）'].flatMap((t) => [
      el('div', { text: t.split(' ')[0] }),
      el('div', { text: t.split(' ').slice(1).join(' ') }),
    ]),
  )

  root.append(
    msg,
    toolbar(input, btn('解析', run, 'primary'), btn('清空', () => { input.value = ''; run(); input.focus() }, 'ghost')),
    el('div', { style: { marginBottom: '12px' } }, chips),
    explainBox,
    el('div', { style: { marginBottom: '12px' } }, fieldHint),
    el('div', { class: 'card-box' },
      el('div', { class: 'row', style: { marginBottom: '8px' } },
        el('strong', { text: '未来 10 次执行' }),
        el('span', { class: 'spacer' }),
        btn('复制表达式', () => copy(input.value.trim())),
      ),
      listHost,
    ),
  )

  paintChips()
  run()
}
