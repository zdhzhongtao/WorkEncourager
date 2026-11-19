import { el } from '../../core/dom'
import { highlightJSON, locate } from '../../core/highlight'
import { copy, debounce, download, fmtBytes, toast } from '../../core/kit'
import { btn, check, foot, pane, seg, toolbar } from '../../core/ui'

const SAMPLE = JSON.stringify({
  name: 'WadeToolbox',
  version: '0.1.0',
  private: true,
  tools: [
    { id: 'json-format', name: 'JSON 格式化' },
    { id: 'regex', name: '正则测试' },
  ],
  author: { name: 'wade', site: null },
  stars: 1280,
})

function sortDeep(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortDeep)
  if (value && typeof value === 'object') {
    const src = value as Record<string, unknown>
    const out: Record<string, unknown> = {}
    for (const key of Object.keys(src).sort()) out[key] = sortDeep(src[key])
    return out
  }
  return value
}

export default function render(root: HTMLElement): void {
  let indent: 2 | 4 | '\t' = 2
  let sortKeys = false
  let lastGood = ''

  const input = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '在这里粘贴 JSON，或点「示例」…',
  })

  const out = el('pre', { class: 'out', dataset: { ph: '格式化结果会出现在这里…' } })

  const msg = el('div', { class: 'msg' })
  const inFoot = el('span', { text: '0 字符' })
  const outFoot = el('span', { text: '' })

  function fail(text: string): void {
    const match = /position (\d+)/.exec(text)
    let where = ''
    if (match) {
      const { line, col } = locate(input.value, Number(match[1]))
      where = `第 ${line} 行第 ${col} 列 — `
    } else if (/Unexpected end/i.test(text)) {
      where = '输入末尾（JSON 没写完）— '
    }
    msg.className = 'msg err'
    msg.textContent = where + text
    out.innerHTML = ''
    outFoot.textContent = ''
  }

  function run(mode: 'pretty' | 'minify' = 'pretty'): void {
    const text = input.value.trim()
    inFoot.textContent = `${input.value.length} 字符`

    if (!text) {
      out.innerHTML = ''
      msg.className = 'msg'
      msg.textContent = ''
      outFoot.textContent = ''
      lastGood = ''
      return
    }

    try {
      const data: unknown = JSON.parse(text)
      const payload = sortKeys ? sortDeep(data) : data
      const pretty = JSON.stringify(payload, null, indent === '\t' ? '\t' : indent)
      const final = mode === 'minify' ? JSON.stringify(payload) : pretty
      out.innerHTML = highlightJSON(final)
      msg.className = 'msg'
      msg.textContent = ''
      lastGood = final
      outFoot.textContent = `${final.split('\n').length} 行 · ${final.length} 字符 · ${fmtBytes(new Blob([final]).size)}`
    } catch (err) {
      fail((err as Error).message)
    }
  }

  const lazyRun = debounce(() => run('pretty'), 220)

  input.addEventListener('input', lazyRun)
  input.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault()
      run('pretty')
    }
  })

  const indentSeg = seg<2 | 4 | '\t'>(
    [
      { label: '2 空格', value: 2 },
      { label: '4 空格', value: 4 },
      { label: 'Tab', value: '\t' },
    ],
    2,
    (v) => {
      indent = v
      run('pretty')
    },
  )

  const sortBox = check('键名排序', false, (v) => {
    sortKeys = v
    run('pretty')
  })

  const bar = toolbar(
    indentSeg,
    sortBox,
    btn('压缩', () => run('minify')),
    el('span', { class: 'spacer' }),
    btn('示例', () => {
      input.value = SAMPLE
      indent = 2
      sortKeys = false
      run('pretty')
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      run('pretty')
    }, 'ghost'),
  )

  const inputPane = pane('输入', input, { foot: foot(inFoot) })

  const outPane = pane('输出', out, {
    actions: [
      btn('复制', () => copy(lastGood || out.textContent || '')),
      btn('下载', () => {
        if (!lastGood) return toast('还没有合法 JSON')
        download('formatted.json', lastGood, 'application/json;charset=utf-8')
      }),
    ],
    foot: foot(outFoot),
  })

  root.append(msg, bar, el('div', { class: 'panes' }, inputPane, outPane))
  input.focus()
}
