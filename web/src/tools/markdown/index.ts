import { marked } from 'marked'
import DOMPurify from 'dompurify'
import { el } from '../../core/dom'
import { copy, debounce, download, toast } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'
import { sendMenu, shareLink } from '../../core/transfer'

const SAMPLE = `# WadeToolbox

纯浏览器运行的开发者工具箱，**不上传任何数据**。

## 现在支持

| 分类 | 工具 | 说明 |
| --- | --- | --- |
| 数据 | JSON 格式化 | 高亮 + 报错定位 |
| 编码 | Base64 | 支持文件 |

> 渲染走 marked，输出前用 DOMPurify 清洗，粘进来带脚本的 HTML 也不会执行。

\`\`\`ts
export const answer = 42
\`\`\`

- [x] 离线可用
- [ ] 云同步（有意不做）
`

marked.setOptions({ gfm: true, breaks: false })

function renderHtml(md: string): string {
  const raw = marked.parse(md, { async: false })
  return DOMPurify.sanitize(raw, { USE_PROFILES: { html: true } })
}

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  const input = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '在这里写 Markdown，右边实时渲染…',
    style: { minHeight: '60vh' },
  })
  const preview = el('div', { class: 'md-preview', dataset: { ph: '渲染结果…' } })
  const stat = el('span', { text: '' })
  const msg = el('div', { class: 'msg' })

  let lastHtml = ''

  function run(): void {
    const md = input.value
    if (!md.trim()) {
      preview.innerHTML = ''
      stat.textContent = ''
      lastHtml = ''
      return
    }
    try {
      lastHtml = renderHtml(md)
      preview.innerHTML = lastHtml
      msg.className = 'msg'
      msg.textContent = ''
      const chars = md.replace(/\s/g, '').length
      stat.textContent = `${md.split('\n').length} 行 · ${chars} 字 · ${preview.querySelectorAll('*').length} 个节点`
    } catch (err) {
      msg.className = 'msg err'
      msg.textContent = (err as Error).message
    }
  }

  const lazy = debounce(run, 160)
  input.addEventListener('input', lazy)
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Tab') {
      e.preventDefault()
      const start = input.selectionStart
      input.setRangeText('  ', start, input.selectionEnd, 'end')
      lazy()
    }
  })

  const bar = toolbar(
    btn('示例', () => {
      input.value = SAMPLE
      run()
    }, 'ghost'),
    btn('清空', () => {
      input.value = ''
      run()
      input.focus()
    }, 'ghost'),
    el('span', { class: 'spacer' }),
    btn('复制 HTML', () => copy(lastHtml, 'HTML 已复制')),
    btn('复制 Markdown', () => copy(input.value, 'Markdown 已复制')),
    btn('分享链接', () => shareLink('markdown', input.value)),
    btn('下载 .html', () => {
      if (!lastHtml) return toast('先写点东西')
      const page = `<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>Markdown</title><style>body{max-width:760px;margin:40px auto;padding:0 20px;font-family:-apple-system,"Segoe UI","Microsoft YaHei",sans-serif;line-height:1.7;color:#101828}pre{background:#f6f7f9;padding:12px;border-radius:8px;overflow:auto}code{font-family:Consolas,monospace}table{border-collapse:collapse}td,th{border:1px solid #e4e7ec;padding:6px 10px}</style></head><body>${lastHtml}</body></html>`
      download('markdown.html', page, 'text/html;charset=utf-8')
    }),
    sendMenu(['html', 'markdown'], () => lastHtml || input.value),
  )

  const inputPane = pane('Markdown', input, { foot: foot(stat) })
  const previewPane = pane('预览', preview, {
    actions: [btn('打印 / 存 PDF', () => window.print(), 'ghost')],
  })

  root.append(msg, bar, el('div', { class: 'panes' }, inputPane, previewPane))

  if (ctx.src) {
    input.value = ctx.src
    run()
  } else {
    input.focus()
  }
}
