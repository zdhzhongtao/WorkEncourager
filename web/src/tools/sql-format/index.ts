import { format } from 'sql-formatter'
import { el } from '../../core/dom'
import { seg } from '../../core/ui'
import { workbench } from '../../core/workbench'

type Dialect = 'sql' | 'mysql' | 'postgresql' | 'tsql' | 'hive' | 'sqlite'
type Mode = 'pretty' | 'minify'

const DIALECTS: Array<{ value: Dialect; label: string }> = [
  { value: 'sql', label: '标准 SQL' },
  { value: 'mysql', label: 'MySQL' },
  { value: 'postgresql', label: 'PostgreSQL' },
  { value: 'tsql', label: 'T-SQL' },
  { value: 'hive', label: 'Hive' },
  { value: 'sqlite', label: 'SQLite' },
]

const DIALECT_LABEL: Record<Dialect, string> = {
  sql: '标准 SQL',
  mysql: 'MySQL',
  postgresql: 'PostgreSQL',
  tsql: 'T-SQL',
  hive: 'Hive',
  sqlite: 'SQLite',
}

interface Tok {
  t: 'str' | 'word' | 'punct'
  v: string
}

/**
 * 轻量压缩：去掉注释和多余空白。
 * 先切成 token 再拼，字符串 / 引号标识符整段原样保留——
 * 直接对全文做正则替换会把 'a  =  b' 这种字面量里的空格也改掉。
 */
function minifySql(sql: string): string {
  const toks: Tok[] = []
  let i = 0

  while (i < sql.length) {
    const ch = sql[i]

    if (ch === '-' && sql[i + 1] === '-') {
      const end = sql.indexOf('\n', i)
      i = end < 0 ? sql.length : end
      continue
    }
    if (ch === '/' && sql[i + 1] === '*') {
      const end = sql.indexOf('*/', i + 2)
      i = end < 0 ? sql.length : end + 2
      continue
    }

    if (ch === "'" || ch === '"' || ch === '`' || ch === '[') {
      const close = ch === '[' ? ']' : ch
      let j = i + 1
      while (j < sql.length) {
        if (ch !== '[' && sql[j] === '\\') {
          j += 2
          continue
        }
        if (sql[j] === close) {
          // '' 这种转义的双写引号要跳过
          if (ch !== '[' && sql[j + 1] === close) {
            j += 2
            continue
          }
          break
        }
        j++
      }
      toks.push({ t: 'str', v: sql.slice(i, Math.min(j + 1, sql.length)) })
      i = j + 1
      continue
    }

    if (/\s/.test(ch)) {
      i++
      continue
    }

    const word = /^[A-Za-z0-9_@#$:\u4e00-\u9fa5]+/.exec(sql.slice(i))
    if (word) {
      toks.push({ t: 'word', v: word[0] })
      i += word[0].length
      continue
    }

    toks.push({ t: 'punct', v: ch })
    i++
  }

  /**
   * 关键字和左括号之间留一个空格（IN (、VALUES (、EXISTS (），
   * 函数调用则贴紧（count(o.id)）。纯压缩视角 `IN(` 也合法，但读起来别扭。
   */
  const KEYWORD_BEFORE_PAREN = new Set([
    'in', 'exists', 'values', 'and', 'or', 'not', 'on', 'using', 'as', 'then', 'else',
    'when', 'select', 'from', 'where', 'group', 'order', 'having', 'join', 'left',
    'right', 'inner', 'outer', 'full', 'union', 'all', 'by', 'into', 'set', 'with',
  ])

  function needSpace(a: Tok, b: Tok): boolean {
    if (b.t === 'punct' && b.v === '(' && a.t === 'word' && KEYWORD_BEFORE_PAREN.has(a.v.toLowerCase())) {
      return true
    }
    if (a.t === 'punct' && (a.v === ',' || a.v === ';')) return true
    if (a.t === 'punct' && a.v === ')' && b.t !== 'punct') return true
    if (a.t !== 'punct' && b.t !== 'punct') return true
    if (a.t === 'str' && b.t === 'word') return true
    if (a.t === 'word' && b.t === 'str') return true
    return false
  }

  let out = ''
  for (let k = 0; k < toks.length; k++) {
    if (k && needSpace(toks[k - 1], toks[k])) out += ' '
    out += toks[k].v
  }
  return out
}

const SAMPLE = `select u.id,u.name,count(o.id) as orders,sum(o.amount) total
from users u left join orders o on o.user_id=u.id
where u.created_at >= '2026-01-01' and u.status in ('active','trial')
group by u.id,u.name having count(o.id)>3 order by total desc limit 50;`

export default function render(
  root: HTMLElement,
  ctx?: { src: string | null; params?: URLSearchParams },
): void {
  let dialect: Dialect = 'mysql'
  let upper = true
  /** 输出形态要记住：压缩后改方言 / 改输入都不能自己跳回格式化 */
  let mode: Mode = 'pretty'

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

  const modeSeg = seg<Mode>(
    [
      { label: '格式化', value: 'pretty' },
      { label: '压缩', value: 'minify' },
    ],
    mode,
    (v) => setMode(v),
  )

  const dialectHint = el('span', { class: 'hint', text: '方言' })

  /** 压缩只去空白和注释，方言与关键字大小写都不参与，灰掉免得误解 */
  function syncControls(): void {
    const min = mode === 'minify'
    select.disabled = min
    caseBtn.disabled = min
    dialectHint.style.opacity = min ? '0.45' : ''
    caseBtn.title = min ? '压缩模式不改关键字大小写' : ''
    select.title = min ? '压缩不依赖方言' : ''
  }

  function setMode(next: Mode): void {
    mode = next
    modeSeg.set(next)
    syncControls()
    wb.run()
  }

  let wb: ReturnType<typeof workbench>

  wb = workbench(root, {
    toolId: 'sql-format',
    placeholder: '粘贴 SQL…',
    outPh: '格式化结果…',
    sample: SAMPLE,
    initial: ctx?.src ?? undefined,
    accepts: ['sql', 'text'],
    produces: ['sql'],
    downloadName: 'query.sql',
    controls: [modeSeg, dialectHint, select, caseBtn],
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      if (mode === 'minify') {
        w.setOut(minifySql(text))
        if (w.outFoot.textContent) w.outFoot.textContent = `已压缩（去注释、折叠空白）· ${w.outFoot.textContent}`
        w.ok()
        return
      }
      const out = format(text, {
        language: dialect,
        keywordCase: upper ? 'upper' : 'lower',
        tabWidth: 2,
        linesBetweenQueries: 1,
      })
      w.setOut(out)
      if (w.outFoot.textContent) {
        w.outFoot.textContent = `已格式化 · ${DIALECT_LABEL[dialect]} · 关键字${upper ? '大写' : '小写'} · ${w.outFoot.textContent}`
      }
      w.ok()
    },
  })

  syncControls()

  if (ctx?.params?.get('mode') === 'minify') {
    setMode('minify')
    return
  }
  // 带 src 进来时 workbench 已经跑过一次，不用重复
  if (!ctx?.src) wb.input.focus()
}
