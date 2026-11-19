import { el } from '../../core/dom'
import { debounce } from '../../core/kit'
import { btn, check, foot, pane, toolbar } from '../../core/ui'

interface Op {
  type: 'same' | 'add' | 'del'
  text: string
  ln: number
}

const MAX_CELLS = 4_000_000

function diffLines(a: string[], b: string[]): Op[] {
  const n = a.length
  const m = b.length
  const w = m + 1
  const dp = new Uint32Array((n + 1) * w)

  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i * w + j] =
        a[i] === b[j] ? dp[(i + 1) * w + j + 1] + 1 : Math.max(dp[(i + 1) * w + j], dp[i * w + j + 1])
    }
  }

  const ops: Op[] = []
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      ops.push({ type: 'same', text: a[i] as string, ln: j + 1 })
      i++
      j++
    } else if (dp[(i + 1) * w + j] >= dp[i * w + j + 1]) {
      ops.push({ type: 'del', text: a[i] as string, ln: i + 1 })
      i++
    } else {
      ops.push({ type: 'add', text: b[j] as string, ln: j + 1 })
      j++
    }
  }
  while (i < n) {
    ops.push({ type: 'del', text: a[i++] as string, ln: i })
  }
  while (j < m) {
    ops.push({ type: 'add', text: b[j++] as string, ln: j })
  }
  return ops
}

export default function render(root: HTMLElement): void {
  let ignoreCase = false
  let trimLine = false

  const left = el('textarea', { class: 'ta', spellcheck: false, placeholder: '原始文本（旧）…' })
  const right = el('textarea', { class: 'ta', spellcheck: false, placeholder: '对照文本（新）…' })
  const view = el('div', { class: 'diff-view' })
  const msg = el('div', { class: 'msg' })
  const stat = el('span', { text: '' })

  function norm(line: string): string {
    let s = line
    if (trimLine) s = s.trim()
    if (ignoreCase) s = s.toLowerCase()
    return s
  }

  function run(): void {
    view.innerHTML = ''
    msg.className = 'msg'
    msg.textContent = ''
    stat.textContent = ''

    const aRaw = left.value
    const bRaw = right.value
    if (!aRaw && !bRaw) return

    const a = aRaw.split('\n')
    const b = bRaw.split('\n')
    const na = a.map(norm)
    const nb = b.map(norm)

    if (na.length * nb.length > MAX_CELLS) {
      msg.className = 'msg warn'
      msg.textContent = `两边行数太多（${na.length} × ${nb.length}），逐行比对会卡住浏览器，先拆分一下再比。`
      return
    }

    const ops = diffLines(na, nb)

    let added = 0
    let removed = 0
    let same = 0

    const frag = document.createDocumentFragment()
    for (const op of ops) {
      if (op.type === 'add') added++
      else if (op.type === 'del') removed++
      else same++

      const row = el(
        'div',
        { class: `diff-row${op.type === 'same' ? '' : op.type === 'add' ? ' ins' : ' del'}` },
        el('span', { class: 'sign', text: op.type === 'add' ? '+' : op.type === 'del' ? '−' : '' }),
        el('span', { class: 'ln', text: String(op.ln) }),
        el('span', { text: op.text === '' ? '\u00A0' : op.text }),
      )
      frag.append(row)
    }
    view.append(frag)

    stat.textContent = `+${added} 行 / −${removed} 行 / 相同 ${same} 行`
    if (added === 0 && removed === 0) {
      msg.className = 'msg ok'
      msg.textContent = '两边内容一致'
    }
  }

  const lazy = debounce(run, 260)
  left.addEventListener('input', lazy)
  right.addEventListener('input', lazy)

  const bar = toolbar(
    check('忽略大小写', false, (v) => {
      ignoreCase = v
      run()
    }),
    check('忽略行首尾空白', false, (v) => {
      trimLine = v
      run()
    }),
    el('span', { class: 'spacer' }),
    btn('交换两侧', () => {
      const tmp = left.value
      left.value = right.value
      right.value = tmp
      run()
    }, 'ghost'),
    btn('清空', () => {
      left.value = ''
      right.value = ''
      run()
    }, 'ghost'),
  )

  root.append(
    msg,
    bar,
    el(
      'div',
      { class: 'panes' },
      pane('原始文本', left, { foot: foot(el('span', { text: '旧版本' })) }),
      pane('对照文本', right, { foot: foot(el('span', { text: '新版本' })) }),
    ),
    el('div', { class: 'card-box', style: { marginTop: '12px', padding: '0', overflow: 'hidden' } }, view),
    el('div', { class: 'row', style: { marginTop: '8px' } }, stat),
  )
  left.focus()
}
