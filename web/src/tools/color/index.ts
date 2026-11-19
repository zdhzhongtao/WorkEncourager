import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn, pane, toolbar } from '../../core/ui'

interface Rgb {
  r: number
  g: number
  b: number
  a: number
}

const clamp255 = (n: number): number => Math.min(255, Math.max(0, Math.round(n)))

/** 借 canvas 把任意合法 CSS 颜色规范化成 rgb / rgba，省掉一堆解析分支 */
function parseColor(raw: string): Rgb | null {
  const input = raw.trim()
  if (!input) return null
  const ctx = document.createElement('canvas').getContext('2d')
  if (!ctx) return null

  const probe = '#010203'
  ctx.fillStyle = probe
  ctx.fillStyle = input
  const out = String(ctx.fillStyle)
  if (out === probe && input.toLowerCase().replace(/\s/g, '') !== probe) return null

  const hexMatch = /^#([0-9a-f]{6})$/i.exec(out)
  if (hexMatch) {
    const v = hexMatch[1] as string
    return { r: parseInt(v.slice(0, 2), 16), g: parseInt(v.slice(2, 4), 16), b: parseInt(v.slice(4, 6), 16), a: 1 }
  }

  const rgbaMatch = /^rgba?\(([^)]+)\)$/i.exec(out)
  if (rgbaMatch) {
    const parts = (rgbaMatch[1] as string).split(',').map((s) => Number(s.trim()))
    return {
      r: clamp255(parts[0] ?? 0),
      g: clamp255(parts[1] ?? 0),
      b: clamp255(parts[2] ?? 0),
      a: parts.length > 3 ? (parts[3] as number) : 1,
    }
  }
  return null
}

const hex2 = (n: number): string => clamp255(n).toString(16).padStart(2, '0')

function toHex(c: Rgb): string {
  return `#${hex2(c.r)}${hex2(c.g)}${hex2(c.b)}`
}

function toHsl(c: Rgb): { h: number; s: number; l: number } {
  const r = c.r / 255
  const g = c.g / 255
  const b = c.b / 255
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const l = (max + min) / 2
  let h = 0
  let s = 0
  if (max !== min) {
    const d = max - min
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6
    else if (max === g) h = ((b - r) / d + 2) / 6
    else h = ((r - g) / d + 4) / 6
  }
  return { h: Math.round(h * 360), s: Math.round(s * 100), l: Math.round(l * 100) }
}

function luminance(c: Rgb): number {
  const f = (v: number): number => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b)
}

function contrast(a: Rgb, b: Rgb): number {
  const l1 = luminance(a)
  const l2 = luminance(b)
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
}

function mix(c: Rgb, target: number, amount: number): Rgb {
  return {
    r: clamp255(c.r + (target - c.r) * amount),
    g: clamp255(c.g + (target - c.g) * amount),
    b: clamp255(c.b + (target - c.b) * amount),
    a: 1,
  }
}

const STEPS = [-0.9, -0.72, -0.54, -0.36, -0.18, 0, 0.18, 0.36, 0.54, 0.72, 0.9]

function badge(ok: boolean, label: string): HTMLElement {
  return el('span', {
    class: 'msg ' + (ok ? 'ok' : 'err'),
    style: { padding: '1px 7px', fontSize: '11px', borderRadius: '999px', display: 'inline-block' },
    text: label,
  })
}

export default function render(root: HTMLElement): void {
  const textInput = el('input', {
    class: 'inp',
    type: 'text',
    style: { flex: '1', minWidth: '180px' },
    placeholder: '#2563eb / rgb(37 99 235) / hsl(221 83% 53%) / tomato',
    value: '#2563eb',
  })
  const picker = el('input', { type: 'color', value: '#2563eb', style: { width: '42px', height: '30px', padding: '0', border: '1px solid var(--border-strong)', borderRadius: '7px', background: 'none' } })

  const swatch = el('div', { class: 'swatch', style: { height: '96px' } })
  const swatchText = el('div', {
    style: { position: 'absolute', inset: '0', display: 'grid', placeItems: 'center', fontFamily: 'var(--mono)', fontSize: '18px', fontWeight: '600' },
  })
  const swatchWrap = el('div', { style: { position: 'relative' } }, swatch, swatchText)

  const tableHost = el('div')
  const rampHost = el('div')
  const contrastHost = el('div', { class: 'stack' })
  const alphaHost = el('div', { class: 'stack' })
  const msg = el('div', { class: 'msg' })

  function renderAll(raw: string): void {
    const c = parseColor(raw)
    msg.className = 'msg'
    msg.textContent = ''
    if (!c) {
      msg.className = 'msg err'
      msg.textContent = '认不出这个颜色。支持 HEX / rgb() / rgba() / hsl() / CSS 颜色名。'
      return
    }

    const hex = toHex(c)
    const hsl = toHsl(c)
    const white: Rgb = { r: 255, g: 255, b: 255, a: 1 }
    const black: Rgb = { r: 0, g: 0, b: 0, a: 1 }
    const lum = luminance(c)

    // 色块
    swatch.style.background = hex
    swatchText.textContent = hex.toUpperCase()
    swatchText.style.color = lum > 0.45 ? '#101828' : '#ffffff'
    if (!picker.matches(':focus')) picker.value = hex

    // 格式表
    const formats: Array<[string, string]> = [
      ['HEX', hex.toUpperCase()],
      ['HEX（8 位含透明度）', `${hex.toUpperCase()}${hex2(c.a * 255)}`],
      ['RGB', `rgb(${c.r}, ${c.g}, ${c.b})`],
      ['RGBA', `rgba(${c.r}, ${c.g}, ${c.b}, ${Number(c.a.toFixed(2))})`],
      ['HSL', `hsl(${hsl.h}, ${hsl.s}%, ${hsl.l}%)`],
      ['HSLA', `hsla(${hsl.h}, ${hsl.s}%, ${hsl.l}%, ${Number(c.a.toFixed(2))})`],
      ['CSS 变量', `--color: ${hex.toUpperCase()};`],
    ]

    tableHost.innerHTML = ''
    const body = el('tbody', null)
    for (const [k, v] of formats) {
      body.append(
        el(
          'tr',
          null,
          el('td', { class: 'k', text: k }),
          el('td', { class: 'v', text: v }),
          el('td', { style: { width: '64px' } }, btn('复制', () => copy(v), 'ghost')),
        ),
      )
    }
    tableHost.append(el('table', { class: 'data' }, el('thead', null, el('tr', null, el('th', { text: '格式' }), el('th', { text: '值' }), el('th'))), body))

    // 色阶
    rampHost.innerHTML = ''
    const ramp = el('div', { class: 'ramp' })
    for (const step of STEPS) {
      const color = step < 0 ? mix(c, 255, -step) : mix(c, 0, step)
      const value = toHex(color)
      ramp.append(
        el('div', {
          style: { background: value },
          dataset: { hex: value.toUpperCase() },
          title: value.toUpperCase(),
          onclick: () => copy(value.toUpperCase()),
        }),
      )
    }
    rampHost.append(ramp)

    // 对比度
    const ratioWhite = contrast(white, c)
    const ratioBlack = contrast(black, c)
    const pickBest = ratioWhite >= ratioBlack ? '白色文字' : '黑色文字'
    contrastHost.innerHTML = ''

    const row = (label: string, ratio: number, fg: string, bg: string): HTMLElement =>
      el(
        'div',
        { class: 'row', style: { padding: '8px 10px', border: '1px solid var(--border)', borderRadius: '8px', background: 'var(--surface)' } },
        el('span', { style: { background: bg, color: fg, padding: '3px 10px', borderRadius: '6px', fontSize: '12px' }, text: '示例文字 Aa' }),
        el('span', { class: 'mono', text: ratio.toFixed(2) + ':1' }),
        badge(ratio >= 4.5, ratio >= 7 ? 'AAA' : ratio >= 4.5 ? 'AA' : '不达标'),
        el('span', { class: 'hint', text: label }),
      )

    contrastHost.append(
      row('白色文字放在这个底色上', ratioWhite, '#ffffff', hex),
      row('黑色文字放在这个底色上', ratioBlack, '#101828', hex),
      el('div', { class: 'hint', text: `建议用${pickBest}。正文要 4.5:1 以上（AA），大号文字 3:1 即可。` }),
    )

    // 明暗（用于选 hover / active 色）
    alphaHost.innerHTML = ''
    const variants: Array<[string, Rgb]> = [
      ['hover 加深 12%', mix(c, 0, 0.12)],
      ['active 加深 24%', mix(c, 0, 0.24)],
      ['浅色背景 8%', mix(c, 255, 0.88)],
      ['边框 40%', mix(c, 255, 0.6)],
    ]
    const vRow = el('div', { class: 'row' })
    for (const [label, color] of variants) {
      const value = toHex(color)
      vRow.append(
        el(
          'div',
          { class: 'row', style: { gap: '6px', cursor: 'pointer' }, onclick: () => copy(value.toUpperCase()) },
          el('span', { style: { width: '22px', height: '22px', borderRadius: '6px', background: value, border: '1px solid var(--border)' } }),
          el('span', { class: 'hint', text: label }),
          el('span', { class: 'mono', style: { fontSize: '11.5px' }, text: value.toUpperCase() }),
        ),
      )
    }
    alphaHost.append(vRow)
  }

  textInput.addEventListener('input', () => renderAll(textInput.value))
  picker.addEventListener('input', () => {
    textInput.value = picker.value
    renderAll(picker.value)
  })

  const bar = toolbar(
    textInput,
    picker,
    el('span', { class: 'spacer' }),
    btn('随机取色', () => {
      const b = new Uint8Array(3)
      crypto.getRandomValues(b)
      const hex = `#${Array.from(b, (x) => hex2(x)).join('')}`
      textInput.value = hex
      renderAll(hex)
    }, 'ghost'),
  )

  root.append(
    msg,
    bar,
    el(
      'div',
      { class: 'panes', style: { marginBottom: '12px' } },
      pane('预览', el('div', { style: { padding: '12px' } }, swatchWrap)),
      pane('格式', el('div', { style: { padding: '12px', overflow: 'auto' } }, tableHost)),
    ),
    el(
      'div',
      { class: 'card-box', style: { marginBottom: '12px' } },
      el('div', { class: 'hint', style: { marginBottom: '8px' }, text: '明暗色阶（点击复制）' }),
      rampHost,
    ),
    el('div', { class: 'card-box', style: { marginBottom: '12px' } }, el('div', { class: 'hint', style: { marginBottom: '8px' }, text: '可读性 / WCAG 对比度' }), contrastHost),
    el('div', { class: 'card-box' }, el('div', { class: 'hint', style: { marginBottom: '8px' }, text: '派生色（点击复制）' }), alphaHost),
  )

  renderAll(textInput.value)
}
