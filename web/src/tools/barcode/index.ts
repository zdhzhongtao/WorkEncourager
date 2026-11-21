import JsBarcode from 'jsbarcode'
import jsQR from 'jsqr'
import { el } from '../../core/dom'
import { copy, download } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'
import { onFiles, readAsDataURL } from '../../core/files'

type Tab = 'gen' | 'scan'

const FORMATS = ['CODE128', 'CODE39', 'EAN13', 'EAN8', 'UPC', 'ITF14', 'MSI', 'pharmacode', 'codabar']

export default function render(root: HTMLElement) {
  let tab: Tab = 'gen'
  let offFiles: (() => void) | null = null
  const tabs = el('div', { class: 'tabs' })
  const host = el('div')
  const msg = el('div', { class: 'msg' })

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'gen', label: '生成条码' },
    { id: 'scan', label: '识别二维码' },
  ]

  function paintTabs(): void {
    tabs.innerHTML = ''
    for (const t of TABS) {
      tabs.append(
        el('button', {
          class: `tab${tab === t.id ? ' on' : ''}`,
          text: t.label,
          onclick: () => {
            tab = t.id
            paintTabs()
            paintBody()
          },
        }),
      )
    }
  }

  /* ---------------- 生成 ---------------- */

  function genView(): void {
    host.innerHTML = ''
    const input = el('input', { class: 'inp mono', style: { flex: '1', minWidth: '240px' }, value: 'WadeToolbox-2026' })
    const fmtSel = el('select', { class: 'inp w-md' })
    for (const f of FORMATS) fmtSel.append(el('option', { value: f, text: f }))
    const showText = el('input', { type: 'checkbox', checked: true })
    const canvas = el('canvas', { style: { maxWidth: '100%', height: 'auto', background: '#fff' } })

    const render = (): void => {
      msg.className = 'msg'
      msg.textContent = ''
      const value = input.value.trim()
      if (!value) {
        const ctx = canvas.getContext('2d')
        ctx?.clearRect(0, 0, canvas.width, canvas.height)
        return
      }
      try {
        JsBarcode(canvas, value, {
          format: fmtSel.value,
          width: 2,
          height: 90,
          displayValue: showText.checked,
          margin: 12,
          fontSize: 14,
          lineColor: '#101828',
          background: '#ffffff',
        })
      } catch (err) {
        const ctx = canvas.getContext('2d')
        ctx?.clearRect(0, 0, canvas.width, canvas.height)
        msg.className = 'msg err'
        msg.textContent = `${fmtSel.value} 不认这段内容：${(err as Error).message}`
      }
    }

    input.addEventListener('input', render)
    fmtSel.addEventListener('change', render)
    showText.addEventListener('change', render)

    host.append(
      msg,
      toolbar(input, fmtSel, el('label', { class: 'check' }, showText, '显示文字'), el('span', { class: 'spacer' }),
        btn('下载 PNG', () => {
          canvas.toBlob((blob) => {
            if (blob) download('barcode.png', blob, 'image/png')
          }, 'image/png')
        }, 'primary')),
      el('div', { class: 'card-box' }, el('div', { class: 'canvas-box' }, canvas)),
      el('div', { class: 'hint', style: { marginTop: '8px' } },
        'CODE128 支持任意 ASCII；EAN13 必须是 12 位数字（第 13 位校验码自动补），EAN8 是 7 位。'),
    )
    render()
    input.focus()
  }

  /* ---------------- 识别 ---------------- */

  function scanView(): void {
    host.innerHTML = ''
    const fileInput = el('input', { type: 'file', accept: 'image/*', style: { display: 'none' } })
    const canvas = el('canvas', { style: { maxWidth: '100%', height: 'auto' } })
    const out = el('pre', { class: 'out', dataset: { ph: '识别结果…' }, style: { minHeight: '80px' } })
    let result = ''

    async function handle(file: File): Promise<void> {
      msg.className = 'msg'
      msg.textContent = ''
      out.textContent = ''
      result = ''
      const url = await readAsDataURL(file)
      const img = new Image()
      img.onload = () => {
        const maxSide = 1400
        const scale = Math.min(1, maxSide / Math.max(img.width, img.height))
        canvas.width = Math.round(img.width * scale)
        canvas.height = Math.round(img.height * scale)
        const ctx = canvas.getContext('2d', { willReadFrequently: true })
        if (!ctx) return
        ctx.drawImage(img, 0, 0, canvas.width, canvas.height)
        const data = ctx.getImageData(0, 0, canvas.width, canvas.height)
        const code = jsQR(data.data, data.width, data.height, { inversionAttempts: 'attemptBoth' })
        if (code) {
          result = code.data
          out.textContent = code.data
          msg.className = 'msg ok'
          msg.textContent = `识别成功，版本 ${code.version}`
        } else {
          msg.className = 'msg warn'
          msg.textContent = '这张图里没找到二维码。试试放大码区、提高对比度，或者别裁掉三个角。'
        }
      }
      img.onerror = () => {
        msg.className = 'msg err'
        msg.textContent = '图片打不开'
      }
      img.src = url
    }

    fileInput.addEventListener('change', () => {
      const file = fileInput.files?.[0]
      if (file) void handle(file)
    })

    offFiles?.()
    offFiles = onFiles((detail) => {
      const image = detail.files.find((f) => f.type.startsWith('image/'))
      if (!image) return
      void handle(image)
    })

    host.append(
      msg,
      toolbar(
        btn('选图片', () => fileInput.click(), 'primary'),
        el('span', { class: 'hint', text: '也可以直接把图片拖进窗口' }),
        el('span', { class: 'spacer' }),
        btn('复制结果', () => copy(result)),
      ),
      el('div', { class: 'panes', style: { marginTop: '12px' } },
        pane('图片', el('div', { class: 'canvas-box' }, canvas), { foot: foot(el('span', { text: '不上传，纯本地解码' })) }),
        pane('识别内容', out),
      ),
      fileInput,
    )
  }

  function paintBody(): void {
    if (tab === 'gen') genView()
    else scanView()
  }

  paintTabs()
  root.append(tabs, host)
  paintBody()
  return () => offFiles?.()
}
