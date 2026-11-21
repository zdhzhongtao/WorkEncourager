import { el } from '../../core/dom'
import { copy, download } from '../../core/kit'
import { btn, foot, pane } from '../../core/ui'

type Tab = 'placeholder' | 'wave' | 'grid'

export default function render(root: HTMLElement): void {
  let tab: Tab = 'placeholder'
  const tabs = el('div', { class: 'tabs' })
  const host = el('div')
  const msg = el('div', { class: 'msg' })

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'placeholder', label: '占位图' },
    { id: 'wave', label: '波浪分隔' },
    { id: 'grid', label: '网格背景' },
  ]

  let currentSvg = ''

  function mount(svg: string, controls: HTMLElement[]): void {
    currentSvg = svg
    const preview = el('div', { class: 'canvas-box', style: { minHeight: '180px' }, html: svg })
    const code = el('pre', { class: 'out', style: { minHeight: '160px' }, text: svg })
    host.append(
      msg,
      el('div', { class: 'card-box stack', style: { marginBottom: '12px' } },
        el('div', { class: 'row' }, ...controls),
      ),
      el('div', { class: 'panes' },
        pane('预览', preview),
        pane('SVG 代码', code, {
          actions: [
            btn('复制', () => copy(currentSvg)),
            btn('下载 .svg', () => download('graphic.svg', currentSvg, 'image/svg+xml;charset=utf-8')),
          ],
          foot: foot(el('span', { text: `${currentSvg.length} 字符` })),
        }),
      ),
    )
  }

  /* ---------------- 占位图 ---------------- */

  function placeholderView(): void {
    host.innerHTML = ''
    const w = el('input', { class: 'inp w-sm', type: 'number', value: '640' })
    const h = el('input', { class: 'inp w-sm', type: 'number', value: '360' })
    const bg = el('input', { type: 'color', value: '#e5e7eb' })
    const fg = el('input', { type: 'color', value: '#6b7280' })
    const text = el('input', { class: 'inp', value: '640 × 360', style: { minWidth: '160px' } })

    const build = (): string => {
      const width = Math.max(20, Number(w.value) || 640)
      const height = Math.max(20, Number(h.value) || 360)
      const label = text.value || `${width} × ${height}`
      const fontSize = Math.max(12, Math.round(Math.min(width, height) / 9))
      return [
        `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">`,
        `  <rect width="${width}" height="${height}" fill="${bg.value}"/>`,
        `  <text x="50%" y="50%" fill="${fg.value}" font-family="-apple-system, Segoe UI, Microsoft YaHei, sans-serif" font-size="${fontSize}" font-weight="600" text-anchor="middle" dominant-baseline="middle">${label}</text>`,
        `</svg>`,
      ].join('\n')
    }

    const update = (): void => {
      host.innerHTML = ''
      mount(build(), [
        el('span', { class: 'hint', text: '宽' }), w,
        el('span', { class: 'hint', text: '高' }), h,
        el('span', { class: 'hint', text: '底色' }), bg,
        el('span', { class: 'hint', text: '字色' }), fg,
        el('span', { class: 'hint', text: '文字' }), text,
        el('span', { class: 'spacer' }),
        btn('刷新', update, 'primary'),
      ])
    }

    ;[w, h, text].forEach((c) => c.addEventListener('input', update))
    ;[bg, fg].forEach((c) => c.addEventListener('input', update))
    update()
  }

  /* ---------------- 波浪 ---------------- */

  function waveView(): void {
    host.innerHTML = ''
    const w = el('input', { class: 'inp w-sm', type: 'number', value: '1200' })
    const h = el('input', { class: 'inp w-sm', type: 'number', value: '120' })
    const color = el('input', { type: 'color', value: '#2563eb' })
    const opacity = el('input', { type: 'range', min: '0.2', max: '1', step: '0.05', value: '1', style: { width: '110px' } })
    const bottom = el('input', { type: 'checkbox', checked: true })

    const build = (): string => {
      const width = Math.max(20, Number(w.value) || 1200)
      const height = Math.max(20, Number(h.value) || 120)
      const d = bottom.checked
        ? `M0 ${height} L0 ${height * 0.55} C ${width * 0.25} ${height * 0.1}, ${width * 0.55} ${height * 1.05}, ${width} ${height * 0.45} L${width} ${height} Z`
        : `M0 0 L0 ${height * 0.45} C ${width * 0.25} ${height * 0.95}, ${width * 0.55} ${height * 0.05}, ${width} ${height * 0.6} L${width} 0 Z`
      return [
        `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none">`,
        `  <path d="${d}" fill="${color.value}" fill-opacity="${opacity.value}"/>`,
        `</svg>`,
      ].join('\n')
    }

    const update = (): void => {
      host.innerHTML = ''
      mount(build(), [
        el('span', { class: 'hint', text: '宽' }), w,
        el('span', { class: 'hint', text: '高' }), h,
        el('span', { class: 'hint', text: '颜色' }), color,
        el('span', { class: 'hint', text: '不透明度' }), opacity,
        el('label', { class: 'check' }, bottom, '贴底'),
        el('span', { class: 'spacer' }),
        btn('刷新', update, 'primary'),
      ])
    }

    ;[w, h].forEach((c) => c.addEventListener('input', update))
    ;[color, opacity, bottom].forEach((c) => c.addEventListener('input', update))
    update()
  }

  /* ---------------- 网格 ---------------- */

  function gridView(): void {
    host.innerHTML = ''
    const size = el('input', { class: 'inp w-sm', type: 'number', value: '480' })
    const cell = el('input', { class: 'inp w-sm', type: 'number', value: '24' })
    const line = el('input', { type: 'color', value: '#d0d5dd' })
    const bg = el('input', { type: 'color', value: '#ffffff' })

    const build = (): string => {
      const s = Math.max(40, Number(size.value) || 480)
      const c = Math.max(4, Number(cell.value) || 24)
      return [
        `<svg xmlns="http://www.w3.org/2000/svg" width="${s}" height="${s}" viewBox="0 0 ${s} ${s}">`,
        `  <defs>`,
        `    <pattern id="grid" width="${c}" height="${c}" patternUnits="userSpaceOnUse">`,
        `      <path d="M ${c} 0 L 0 0 0 ${c}" fill="none" stroke="${line.value}" stroke-width="1"/>`,
        `    </pattern>`,
        `  </defs>`,
        `  <rect width="${s}" height="${s}" fill="${bg.value}"/>`,
        `  <rect width="${s}" height="${s}" fill="url(#grid)"/>`,
        `</svg>`,
      ].join('\n')
    }

    const update = (): void => {
      host.innerHTML = ''
      mount(build(), [
        el('span', { class: 'hint', text: '画布' }), size,
        el('span', { class: 'hint', text: '格子' }), cell,
        el('span', { class: 'hint', text: '线条' }), line,
        el('span', { class: 'hint', text: '背景' }), bg,
        el('span', { class: 'spacer' }),
        btn('刷新', update, 'primary'),
      ])
    }

    ;[size, cell].forEach((c) => c.addEventListener('input', update))
    ;[line, bg].forEach((c) => c.addEventListener('input', update))
    update()
  }

  function paint(): void {
    tabs.innerHTML = ''
    for (const t of TABS) {
      tabs.append(
        el('button', {
          class: `tab${tab === t.id ? ' on' : ''}`,
          text: t.label,
          onclick: () => {
            tab = t.id
            paint()
            paintBody()
          },
        }),
      )
    }
  }

  function paintBody(): void {
    if (tab === 'placeholder') placeholderView()
    else if (tab === 'wave') waveView()
    else gridView()
  }

  paint()
  root.append(tabs, host)
  paintBody()
}
