import { el, escapeHtml } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

const FLAGS = ['g', 'i', 'm', 's', 'u', 'y']

const PRESETS: Array<{ label: string; pattern: string; flags: string }> = [
  { label: '邮箱', pattern: '[\\w.%+-]+@[\\w.-]+\\.[A-Za-z]{2,}', flags: 'gi' },
  { label: '手机号（中国）', pattern: '1[3-9]\\d{9}', flags: 'g' },
  { label: '网址', pattern: 'https?://[\\w.-]+(?:/[\\w./?%&=#-]*)?', flags: 'gi' },
  { label: 'IPv4', pattern: '\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b', flags: 'g' },
  { label: '日期 YYYY-MM-DD', pattern: '\\d{4}-\\d{2}-\\d{2}', flags: 'g' },
  { label: 'HTML 标签', pattern: '</?[a-zA-Z][^>]*>', flags: 'g' },
  { label: '中文字符', pattern: '[\\u4e00-\\u9fa5]', flags: 'g' },
  { label: '重复空白', pattern: '\\s+', flags: 'g' },
]

const FLAG_TITLES: Record<string, string> = {
  g: 'g 全局匹配',
  i: 'i 忽略大小写',
  m: 'm 多行模式',
  s: 's 让 . 匹配换行',
  u: 'u Unicode 模式',
  y: 'y 粘性匹配',
}

export default function render(root: HTMLElement): void {
  const activeFlags = new Set<string>(['g'])

  const patternInput = el('input', {
    class: 'inp',
    type: 'text',
    spellcheck: false,
    style: { flex: '1', minWidth: '220px' },
    placeholder: '正则表达式，不用写两侧的斜杠。例：\\d{3}-\\d{4}',
  })

  const text = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '在这里放入要被匹配的文本…',
  })

  const highlightBox = el('div', { class: 'mark-box', dataset: { ph: '填好正则和文本，匹配处会在这里高亮…' } })
  const groupHost = el('div')
  const groupCard = el('div', { class: 'card-box', style: { marginTop: '12px', display: 'none' } }, groupHost)
  const replaceIn = el('input', { class: 'inp', type: 'text', style: { flex: '1' }, placeholder: '替换成什么？可以用 $1 / $& / $<name>' })
  const replaceOut = el('div', { class: 'mark-box', style: { display: 'none' } })
  const msg = el('div', { class: 'msg' })
  const stat = el('span', { text: '' })

  function flags(): string {
    return FLAGS.filter((f) => activeFlags.has(f)).join('')
  }

  function run(): void {
    highlightBox.innerHTML = ''
    groupHost.innerHTML = ''
    groupCard.style.display = 'none'
    stat.textContent = ''
    msg.className = 'msg'
    msg.textContent = ''

    const source = patternInput.value
    const body = text.value

    if (!source) {
      highlightBox.innerHTML = ''
      groupHost.innerHTML = ''
      replaceOut.style.display = 'none'
      if (text.value.trim()) highlightBox.innerHTML = escapeHtml(text.value)
      return
    }

    let re: RegExp
    try {
      re = new RegExp(source, flags())
    } catch (err) {
      msg.className = 'msg err'
      msg.textContent = `正则写法有误：${(err as Error).message}`
      return
    }

    const globalRe = new RegExp(re.source, re.flags.includes('g') ? re.flags : `${re.flags}g`)
    const matches: Array<{ value: string; index: number; groups: string[] }> = []
    let m: RegExpExecArray | null
    let guard = 0

    while ((m = globalRe.exec(body)) !== null) {
      matches.push({ value: m[0], index: m.index, groups: m.slice(1).map((g) => g ?? '') })
      if (m[0] === '') globalRe.lastIndex++
      if (++guard > 3000) break
    }

    // ---- 高亮 ----
    let html = ''
    let last = 0
    for (const hit of matches) {
      html += escapeHtml(body.slice(last, hit.index))
      const shown = hit.value === '' ? '∅' : escapeHtml(hit.value)
      html += `<mark class="hit">${shown}</mark>`
      last = hit.index + hit.value.length
    }
    html += escapeHtml(body.slice(last))
    highlightBox.innerHTML = html || '<span class="hint">（没有内容）</span>'

    stat.textContent = matches.length
      ? `${matches.length} 处匹配${guard > 3000 ? '（已截断到 3000）' : ''}`
      : '没有匹配'

    // ---- 捕获组 ----
    if (matches.length) {
      groupCard.style.display = ''
      const rows = matches.slice(0, 200).map((hit, i) => {
        const groupText = hit.groups.length
          ? hit.groups.map((g, gi) => `$${gi + 1}=${g}`).join('  ')
          : '—'
        return el(
          'tr',
          null,
          el('td', { class: 'k', text: `#${i + 1}` }),
          el('td', { class: 'v', text: hit.value || '（空）' }),
          el('td', { class: 'v', text: String(hit.index) }),
          el('td', { class: 'v', text: groupText }),
        )
      })
      groupHost.append(
        el(
          'table',
          { class: 'data' },
          el(
            'thead',
            null,
            el('tr', null, el('th', { text: '序' }), el('th', { text: '匹配内容' }), el('th', { text: '起始位置' }), el('th', { text: '分组' })),
          ),
          el('tbody', null, ...rows),
        ),
      )
    }

    // ---- 替换 ----
    if (replaceIn.value) {
      try {
        const replaced = body.replace(re, replaceIn.value)
        replaceOut.style.display = ''
        replaceOut.textContent = replaced
      } catch (err) {
        replaceOut.style.display = ''
        replaceOut.textContent = `替换出错：${(err as Error).message}`
      }
    } else {
      replaceOut.style.display = 'none'
    }
  }

  patternInput.addEventListener('input', run)
  text.addEventListener('input', run)
  replaceIn.addEventListener('input', run)

  const flagBtns = new Map<string, HTMLButtonElement>()
  const flagBoxes = el('div', { class: 'seg' })
  for (const f of FLAGS) {
    const b = el('button', {
      class: activeFlags.has(f) ? 'on' : '',
      text: f,
      title: FLAG_TITLES[f] ?? f,
      onclick: () => {
        if (activeFlags.has(f)) {
          activeFlags.delete(f)
          b.classList.remove('on')
        } else {
          activeFlags.add(f)
          b.classList.add('on')
        }
        run()
      },
    })
    flagBtns.set(f, b)
    flagBoxes.append(b)
  }

  const presetBox = el('div', { class: 'row', style: { marginBottom: '12px', gap: '6px' } })
  presetBox.append(el('span', { class: 'hint', text: '常用：' }))
  for (const p of PRESETS) {
    presetBox.append(
      btn(p.label, () => {
        patternInput.value = p.pattern
        activeFlags.clear()
        for (const f of p.flags) activeFlags.add(f)
        // 同步开关状态
        for (const [f, b] of flagBtns) b.classList.toggle('on', activeFlags.has(f))
        run()
      }, 'ghost'),
    )
  }

  const bar = toolbar(
    el('span', { class: 'hint', text: '/' }),
    patternInput,
    el('span', { class: 'hint', text: '/' }),
    flagBoxes,
    el('span', { class: 'spacer' }),
    btn('清空', () => {
      patternInput.value = ''
      text.value = ''
      replaceIn.value = ''
      run()
    }, 'ghost'),
  )

  root.append(
    msg,
    bar,
    presetBox,
    el(
      'div',
      { class: 'panes' },
      pane('测试文本', text, { actions: [btn('复制', () => copy(text.value))] }),
      pane('匹配高亮', highlightBox, { foot: foot(stat) }),
    ),
    el('div', { class: 'card-box', style: { marginTop: '12px' } }, el('div', { class: 'hint', style: { marginBottom: '8px' }, text: '替换预览' }), el('div', { class: 'row', style: { marginBottom: '10px' } }, replaceIn), replaceOut),
    groupCard,
  )
  patternInput.focus()
}
