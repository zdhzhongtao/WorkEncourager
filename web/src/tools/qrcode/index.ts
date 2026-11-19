import QRCode from 'qrcode'
import { el } from '../../core/dom'
import { debounce, download } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

type Level = 'L' | 'M' | 'Q' | 'H'

export default function render(root: HTMLElement): void {
  let size = 256
  let level: Level = 'M'
  let dark = '#000000'
  let light = '#ffffff'
  let lastSvg = ''

  const text = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '输入要生成二维码的内容：网址、文本、WiFi 密码、名片信息…',
  })

  const canvas = el('canvas', {
    style: {
      width: '100%',
      maxWidth: '300px',
      height: 'auto',
      display: 'block',
      margin: '0 auto',
      background: '#fff',
      borderRadius: '8px',
      imageRendering: 'pixelated',
    },
  })

  const msg = el('div', { class: 'msg' })
  const info = el('span', { text: '' })

  async function draw(): Promise<void> {
    const value = text.value
    if (!value.trim()) {
      const ctx = canvas.getContext('2d')
      ctx?.clearRect(0, 0, canvas.width, canvas.height)
      msg.className = 'msg'
      msg.textContent = ''
      info.textContent = ''
      lastSvg = ''
      return
    }

    try {
      await QRCode.toCanvas(canvas, value, {
        width: size,
        margin: 2,
        errorCorrectionLevel: level,
        color: { dark, light },
      })
      lastSvg = await QRCode.toString(value, {
        type: 'svg',
        margin: 2,
        errorCorrectionLevel: level,
        color: { dark, light },
      })
      msg.className = 'msg'
      msg.textContent = ''
      const bytes = new TextEncoder().encode(value).length
      info.textContent = `${size}×${size} px · 纠错 ${level} · ${bytes} 字节`
    } catch (err) {
      lastSvg = ''
      msg.className = 'msg err'
      msg.textContent = `生成失败：内容太长或包含不支持的字符。（${(err as Error).message}）`
    }
  }

  const lazy = debounce(() => void draw(), 200)
  text.addEventListener('input', lazy)

  const sizeRange = el('input', { type: 'range', min: '128', max: '1024', step: '32', value: '256', style: { width: '110px' } })
  const sizeLabel = el('span', { class: 'hint mono', text: '256px' })
  sizeRange.addEventListener('input', () => {
    size = Number(sizeRange.value)
    sizeLabel.textContent = `${size}px`
    void draw()
  })

  const levelSelect = el('select', { class: 'inp' })
  for (const l of ['L', 'M', 'Q', 'H'] as Level[]) {
    levelSelect.append(el('option', { value: l, text: `${l}${l === 'M' ? '（推荐）' : ''}` }))
  }
  levelSelect.value = level
  levelSelect.addEventListener('change', () => {
    level = levelSelect.value as Level
    void draw()
  })

  const darkInput = el('input', { type: 'color', value: dark, style: { width: '34px', height: '28px', padding: '0', border: '1px solid var(--border-strong)', borderRadius: '6px', background: 'none' } })
  const lightInput = el('input', { type: 'color', value: light, style: { width: '34px', height: '28px', padding: '0', border: '1px solid var(--border-strong)', borderRadius: '6px', background: 'none' } })
  darkInput.addEventListener('input', () => {
    dark = darkInput.value
    void draw()
  })
  lightInput.addEventListener('input', () => {
    light = lightInput.value
    void draw()
  })

  const bar = toolbar(
    el('span', { class: 'hint', text: '尺寸' }),
    sizeRange,
    sizeLabel,
    el('span', { class: 'hint', text: '纠错' }),
    levelSelect,
    el('span', { class: 'hint', text: '前景' }),
    darkInput,
    el('span', { class: 'hint', text: '背景' }),
    lightInput,
    el('span', { class: 'spacer' }),
    btn('本页链接', () => {
      text.value = location.href
      void draw()
    }, 'ghost'),
    btn('清空', () => {
      text.value = ''
      void draw()
    }, 'ghost'),
  )

  root.append(
    msg,
    bar,
    el(
      'div',
      { class: 'panes' },
      pane('内容', text, { actions: [btn('复制', () => navigator.clipboard.writeText(text.value))] }),
      pane(
        '预览',
        el('div', { style: { padding: '14px', display: 'grid', placeItems: 'center', background: 'var(--surface-2)' } }, canvas),
        {
          actions: [
            btn('PNG', () => {
              canvas.toBlob((blob) => {
                if (blob) download('qrcode.png', blob)
              }, 'image/png')
            }),
            btn('SVG', () => {
              if (lastSvg) download('qrcode.svg', lastSvg, 'image/svg+xml;charset=utf-8')
            }),
          ],
          foot: foot(info),
        },
      ),
    ),
    el('div', { class: 'hint', style: { marginTop: '10px' } }, '纠错级别越高，二维码越密但越耐脏、越容易被残缺扫描。普通用途选 M 就够。'),
  )

  text.value = 'https://example.com'
  void draw()
  text.focus()
}
