import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, seg } from '../../core/ui'

type Tab = 'gradient' | 'shadow' | 'radius' | 'easing'

export default function render(root: HTMLElement): void {
  let tab: Tab = 'gradient'
  const tabs = el('div', { class: 'tabs' })
  const host = el('div')
  const msg = el('div', { class: 'msg' })

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'gradient', label: '渐变' },
    { id: 'shadow', label: '阴影' },
    { id: 'radius', label: '圆角' },
    { id: 'easing', label: '缓动曲线' },
  ]

  const fieldRow = (label: string, control: HTMLElement, hint = ''): HTMLElement =>
    el('div', { class: 'field', style: { minWidth: '110px' } },
      el('span', { class: 'field-label', text: label }),
      control,
      hint ? el('span', { class: 'hint', style: { fontSize: '11px' } }, hint) : null,
    )

  /* ---------------- 渐变 ---------------- */

  function gradientView(): void {
    host.innerHTML = ''
    let type: 'linear' | 'radial' = 'linear'
    let angle = 135
    const c1 = el('input', { type: 'color', value: '#2563eb' })
    const c2 = el('input', { type: 'color', value: '#a855f7' })
    const c3 = el('input', { type: 'color', value: '#22d3ee' })
    let useThird = false

    const angleInput = el('input', { class: 'inp w-sm', type: 'number', min: '0', max: '360', value: '135' })
    const preview = el('div', { style: { height: '140px', borderRadius: '10px', border: '1px solid var(--border)' } })
    const code = el('pre', { class: 'out', style: { minHeight: 'auto', padding: '10px 12px' } })

    const paint = (): void => {
      const stops = useThird ? `${c1.value} 0%, ${c2.value} 55%, ${c3.value} 100%` : `${c1.value} 0%, ${c2.value} 100%`
      const value = type === 'linear' ? `linear-gradient(${angle}deg, ${stops})` : `radial-gradient(circle at 50% 50%, ${stops})`
      preview.style.background = value
      code.textContent = `background: ${value};`
    }

    const typeSeg = seg<'linear' | 'radial'>([{ label: '线性', value: 'linear' }, { label: '径向', value: 'radial' }], 'linear', (v) => {
      type = v
      paint()
    })

    angleInput.addEventListener('input', () => {
      angle = Number(angleInput.value) || 0
      paint()
    })
    ;[c1, c2, c3].forEach((c) => c.addEventListener('input', paint))

    host.append(
      el('div', { class: 'row', style: { marginBottom: '10px' } }, typeSeg,
        el('label', { class: 'check' },
          el('input', {
            type: 'checkbox',
            onchange: (e: Event) => {
              useThird = (e.target as HTMLInputElement).checked
              paint()
            },
          }),
          '加一个中间色'),
      ),
      el('div', { class: 'card-box stack', style: { marginBottom: '12px' } },
        el('div', { class: 'row' },
          fieldRow('角度', angleInput),
          fieldRow('起始色', c1),
          fieldRow('结束色', c2),
          fieldRow('中间色', c3),
          el('span', { class: 'spacer' }),
          btn('复制 CSS', () => copy(code.textContent ?? ''), 'primary'),
        ),
        preview,
        code,
      ),
    )
    paint()
  }

  /* ---------------- 阴影 ---------------- */

  function shadowView(): void {
    host.innerHTML = ''
    let inset = false
    const sliders: Record<string, HTMLInputElement> = {}
    const labels: Record<string, HTMLElement> = {}
    const SPECS: Array<[string, number, number, number]> = [
      ['x', -40, 40, 0],
      ['y', -40, 40, 12],
      ['blur', 0, 80, 28],
      ['spread', -20, 40, -6],
    ]
    const color = el('input', { type: 'color', value: '#101828' })
    const alpha = el('input', { type: 'range', min: '0.05', max: '1', step: '0.05', value: '0.25', style: { width: '110px' } })
    const preview = el('div', { style: { height: '140px', display: 'grid', placeItems: 'center' } })
    const box = el('div', { style: { width: '120px', height: '80px', borderRadius: '12px', background: 'var(--surface)', border: '1px solid var(--border)' } })
    const code = el('pre', { class: 'out', style: { minHeight: 'auto', padding: '10px 12px' } })
    preview.append(box)

    const hexToRgb = (hex: string): [number, number, number] => {
      const v = hex.replace('#', '')
      return [parseInt(v.slice(0, 2), 16), parseInt(v.slice(2, 4), 16), parseInt(v.slice(4, 6), 16)]
    }

    const paint = (): void => {
      const [r, g, b] = hexToRgb(color.value)
      const value = `${inset ? 'inset ' : ''}${sliders.x.value}px ${sliders.y.value}px ${sliders.blur.value}px ${sliders.spread.value}px rgba(${r}, ${g}, ${b}, ${alpha.value})`
      box.style.boxShadow = value
      code.textContent = `box-shadow: ${value};`
    }

    const rows = SPECS.map(([name, min, max, def]) => {
      const input = el('input', { type: 'range', min: String(min), max: String(max), value: String(def), style: { width: '110px' } })
      sliders[name] = input
      const label = el('span', { class: 'hint mono', text: `${def}px`, style: { minWidth: '42px' } })
      labels[name] = label
      input.addEventListener('input', () => {
        label.textContent = `${input.value}px`
        paint()
      })
      return el('div', { class: 'row' }, el('span', { class: 'hint', style: { width: '46px' } }, name), input, label)
    })

    color.addEventListener('input', paint)
    alpha.addEventListener('input', paint)

    host.append(
      el('div', { class: 'card-box stack', style: { marginBottom: '12px' } },
        el('div', { class: 'row', style: { alignItems: 'flex-start' } },
          el('div', { class: 'stack', style: { flex: '1' } }, ...rows),
          el('div', { class: 'stack' },
            fieldRow('颜色', color),
            fieldRow('不透明度', alpha),
            el('label', { class: 'check' },
              el('input', {
                type: 'checkbox',
                onchange: (e: Event) => {
                  inset = (e.target as HTMLInputElement).checked
                  paint()
                },
              }),
              '内阴影 inset'),
          ),
          el('span', { class: 'spacer' }),
          btn('复制 CSS', () => copy(code.textContent ?? ''), 'primary'),
        ),
        preview,
        code,
      ),
    )
    paint()
    void labels
  }

  /* ---------------- 圆角 ---------------- */

  function radiusView(): void {
    host.innerHTML = ''
    const inputs: HTMLInputElement[] = []
    const unit = el('select', { class: 'inp w-sm' })
    unit.append(el('option', { value: 'px', text: 'px' }), el('option', { value: '%', text: '%' }))
    const preview = el('div', { style: { height: '160px', display: 'grid', placeItems: 'center' } })
    const box = el('div', { style: { width: '160px', height: '110px', background: 'linear-gradient(135deg,#2563eb,#a855f7)' } })
    const code = el('pre', { class: 'out', style: { minHeight: 'auto', padding: '10px 12px' } })
    preview.append(box)

    const paint = (): void => {
      const values = inputs.map((i) => `${i.value}${unit.value}`)
      const value = `${values[0]} ${values[1]} ${values[2]} ${values[3]}`
      box.style.borderRadius = value
      code.textContent = `border-radius: ${value};`
    }

    const rows = ['左上', '右上', '右下', '左下'].map((label, i) => {
      const input = el('input', { type: 'range', min: '0', max: '150', value: i === 0 ? '24' : '24', style: { width: '120px' } })
      inputs.push(input)
      const out = el('span', { class: 'hint mono', text: '24', style: { minWidth: '34px' } })
      input.addEventListener('input', () => {
        out.textContent = input.value
        paint()
      })
      return el('div', { class: 'row' }, el('span', { class: 'hint', style: { width: '46px' } }, label), input, out)
    })

    unit.addEventListener('change', paint)

    host.append(
      el('div', { class: 'card-box stack', style: { marginBottom: '12px' } },
        el('div', { class: 'row', style: { alignItems: 'flex-start' } },
          el('div', { class: 'stack', style: { flex: '1' } }, ...rows),
          fieldRow('单位', unit),
          el('span', { class: 'spacer' }),
          btn('复制 CSS', () => copy(code.textContent ?? ''), 'primary'),
        ),
        preview,
        code,
      ),
    )
    paint()
  }

  /* ---------------- 缓动 ---------------- */

  function easingView(): void {
    host.innerHTML = ''
    const vals = [0.34, 1.2, 0.64, 1]
    const inputs = vals.map((v, i) => {
      const input = el('input', { type: 'number', step: '0.01', value: String(v), class: 'inp w-sm' })
      input.addEventListener('input', () => {
        vals[i] = Number(input.value) || 0
        paint()
      })
      return input
    })

    const NS = 'http://www.w3.org/2000/svg'
    const svg = document.createElementNS(NS, 'svg')
    svg.setAttribute('viewBox', '0 0 200 200')
    svg.setAttribute('width', '100%')
    svg.setAttribute('style', 'max-width:260px;height:auto;background:var(--surface-2);border:1px solid var(--border);border-radius:10px')
    const path = document.createElementNS(NS, 'path')
    path.setAttribute('fill', 'none')
    path.setAttribute('stroke', '#2563eb')
    path.setAttribute('stroke-width', '2')
    svg.append(path)
    for (const [x, y] of [[0, 200], [200, 0]]) {
      const dot = document.createElementNS(NS, 'circle')
      dot.setAttribute('cx', String(x))
      dot.setAttribute('cy', String(y))
      dot.setAttribute('r', '2.5')
      dot.setAttribute('fill', '#98a2b3')
      svg.append(dot)
    }
    const handles: SVGCircleElement[] = [0, 1].map(() => {
      const c = document.createElementNS(NS, 'circle')
      c.setAttribute('r', '4')
      c.setAttribute('fill', '#f59e0b')
      svg.append(c)
      return c
    })
    const code = el('pre', { class: 'out', style: { minHeight: 'auto', padding: '10px 12px' } })
    const demo = el('div', {
      style: {
        width: '46px',
        height: '46px',
        borderRadius: '10px',
        background: 'linear-gradient(135deg,#2563eb,#a855f7)',
        transitionProperty: 'transform',
        transitionDuration: '900ms',
        marginTop: '10px',
      },
    })
    const demoWrap = el('div', {
      style: { display: 'grid', placeItems: 'center', height: '90px', border: '1px dashed var(--border)', borderRadius: '10px' },
      onmouseenter: () => {
        demo.style.transform = 'translateX(120px)'
      },
      onmouseleave: () => {
        demo.style.transform = 'translateX(0)'
      },
    }, demo)

    function paint(): void {
      const [x1, y1, x2, y2] = vals
      const fn = `cubic-bezier(${x1}, ${y1}, ${x2}, ${y2})`
      const px1 = x1 * 200
      const py1 = 200 - y1 * 200
      const px2 = x2 * 200
      const py2 = 200 - y2 * 200
      path.setAttribute('d', `M0,200 C${px1},${py1} ${px2},${py2} 200,0`)
      handles[0].setAttribute('cx', String(px1))
      handles[0].setAttribute('cy', String(py1))
      handles[1].setAttribute('cx', String(px2))
      handles[1].setAttribute('cy', String(py2))
      code.textContent = `transition-timing-function: ${fn};`
      demo.style.transitionTimingFunction = fn
    }

    host.append(
      el('div', { class: 'card-box stack' },
        el('div', { class: 'row' },
          el('span', { class: 'hint', text: 'x1' }), inputs[0],
          el('span', { class: 'hint', text: 'y1' }), inputs[1],
          el('span', { class: 'hint', text: 'x2' }), inputs[2],
          el('span', { class: 'hint', text: 'y2' }), inputs[3],
          el('span', { class: 'spacer' }),
          btn('复制 CSS', () => copy(code.textContent ?? ''), 'primary'),
        ),
        el('div', { class: 'grid-2' },
          el('div', { class: 'canvas-box' }, svg),
          el('div', { class: 'stack' }, demoWrap, el('div', { class: 'hint' }, '鼠标移上去看效果')),
        ),
        code,
        el('div', { class: 'hint' }, 'y1 / y2 超过 1 就是「回弹」，小于 0 是「先倒退再走」，两端 x 必须在 0–1 之间。'),
      ),
    )
    paint()
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
    if (tab === 'gradient') gradientView()
    else if (tab === 'shadow') shadowView()
    else if (tab === 'radius') radiusView()
    else easingView()
  }

  paint()
  root.append(msg, tabs, host)
  paintBody()
}
