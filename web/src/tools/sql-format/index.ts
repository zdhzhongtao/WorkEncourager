import { format } from 'sql-formatter'
import { el } from '../../core/dom'
import { workbench } from '../../core/workbench'

type Dialect = 'sql' | 'mysql' | 'postgresql' | 'tsql' | 'hive' | 'sqlite'

const DIALECTS: Array<{ value: Dialect; label: string }> = [
  { value: 'sql', label: '标准 SQL' },
  { value: 'mysql', label: 'MySQL' },
  { value: 'postgresql', label: 'PostgreSQL' },
  { value: 'tsql', label: 'T-SQL' },
  { value: 'hive', label: 'Hive' },
  { value: 'sqlite', label: 'SQLite' },
]

/** 轻量压缩：去掉注释和多余空白，字符串与标识符原样保留 */
function minifySql(sql: string): string {
  let out = ''
  let i = 0
  while (i < sql.length) {
    const ch = sql[i]
    const next = sql[i + 1]

    if (ch === '-' && next === '-') {
      const end = sql.indexOf('\n', i)
      i = end < 0 ? sql.length : end
      out += ' '
      continue
    }
    if (ch === '/' && next === '*') {
      const end = sql.indexOf('*/', i + 2)
      i = end < 0 ? sql.length : end + 2
      out += ' '
      continue
    }
    if (ch === "'" || ch === '"' || ch === '`') {
      let j = i + 1
      while (j < sql.length) {
        if (sql[j] === '\\') j += 2
        else if (sql[j] === ch) {
          if (sql[j + 1] === ch) j += 2
          else break
        } else j++
      }
      out += sql.slice(i, j + 1)
      i = j + 1
      continue
    }
    if (/\s/.test(ch)) {
      if (!out.endsWith(' ')) out += ' '
      i++
      continue
    }
    out += ch
    i++
  }
  return out
    .replace(/\s*([,()=<>+\-*/;])\s*/g, '$1 ')
    .replace(/\s+/g, ' ')
    .replace(/\s*;\s*$/, ';')
    .trim()
}

const SAMPLE = `select u.id,u.name,count(o.id) as orders,sum(o.amount) total
from users u left join orders o on o.user_id=u.id
where u.created_at >= '2026-01-01' and u.status in ('active','trial')
group by u.id,u.name having count(o.id)>3 order by total desc limit 50;`

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let dialect: Dialect = 'mysql'
  let upper = true

  const select = el('select', { class: 'inp w-md' })
  for (const d of DIALECTS) select.append(el('option', { value: d.value, text: d.label }))
  select.value = dialect
  select.addEventListener('change', () => {
    dialect = select.value as Dialect
    wb.run()
  })

  const caseBtn = el('button', {
    class: 'chip on',
    text: '关键字大写',
    onclick: () => {
      upper = !upper
      caseBtn.className = `chip${upper ? ' on' : ''}`
      caseBtn.textContent = upper ? '关键字大写' : '关键字小写'
      wb.run()
    },
  })

  let wb: ReturnType<typeof workbench>

  wb = workbench(root, {
    toolId: 'sql-format',
    placeholder: '粘贴 SQL…',
    outPh: '格式化结果…',
    sample: SAMPLE,
    initial: ctx.src ?? undefined,
    accepts: ['sql', 'text'],
    produces: ['sql'],
    downloadName: 'query.sql',
    controls: [
      el('span', { class: 'hint', text: '方言' }),
      select,
      caseBtn,
    ],
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      const out = format(text, {
        language: dialect,
        keywordCase: upper ? 'upper' : 'lower',
        tabWidth: 2,
        linesBetweenQueries: 1,
      })
      w.setOut(out)
      w.ok()
    },
  })

  const minifyBtn = el('button', {
    class: 'btn',
    text: '压缩',
    onclick: () => {
      if (!wb.input.value.trim()) return
      wb.setOut(minifySql(wb.input.value))
    },
  })
  wb.toolbar.insertBefore(minifyBtn, wb.toolbar.querySelector('.spacer'))
}
