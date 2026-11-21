import { el } from '../../core/dom'
import { copy, download, fmtBytes, toast } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'
import { onFiles, readAsDataURL } from '../../core/files'

type OutFormat = 'image/jpeg' | 'image/png' | 'image/webp'

export default function render(root: HTMLElement) {
  let img: HTMLImageElement | null = null
  let fileName = 'image'
  let blob: Blob | null = null
  let originalSize = 0
  let offFiles: (() => void) | null = null

  const msg = el('div', { class: 'msg' })
  const canvas = el('canvas', { style: { maxWidth: '100%', height: 'auto', display: 'block', margin: '0 auto' } })
  const fileInput = el('input', { type: 'file', accept: 'image/*', style: { display: 'none' } })
  const info = el('span', { class: 'hint', text: '还没选图片' })

  const widthInput = el('input', { class: 'inp w-md', type: 'number', min: '0', max: '8000', value: '0', placeholder: '0 = 原尺寸' })
  const formatSel = el('select', { class: 'inp w-md' })
  formatSel.append(
    el('option', { value: 'image/jpeg', text: 'JPEG' }),
    el('option', { value: 'image/webp', text: 'WebP' }),
    el('option', { value: 'image/png', text: 'PNG（无损）' }),
  )
  formatSel.value = 'image/jpeg'
  const quality = el('input', { type: 'range', min: '0.3', max: '1', step: '0.02', value: '0.82', style: { width: '130px' } })
  const qualityLabel = el('span', { class: 'hint mono', text: '82%' })
  const markInput = el('input', { class: 'inp w-md', placeholder: '水印文字（可空）' })
  const markOpacity = el('input', { type: 'range', min: '0.1', max: '1', step: '0.05', value: '0.55', style: { width: '100px' } })

  quality.addEventListener('input', () => {
    qualityLabel.textContent = `${Math.round(Number(quality.value) * 100)}%`
  })

  function draw(): void {
    if (!img) return
    const maxWidth = Number(widthInput.value) || 0
    const scale = maxWidth > 0 && maxWidth < img.width ? maxWidth / img.width : 1
    canvas.width = Math.round(img.width * scale)
    canvas.height = Math.round(img.height * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const format = formatSel.value as OutFormat
    if (format === 'image/jpeg') {
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, canvas.width, canvas.height)
    } else {
      ctx.clearRect(0, 0, canvas.width, canvas.height)
    }
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height)

    const text = markInput.value.trim()
    if (text) {
      const fontSize = Math.max(12, Math.round(canvas.width / 26))
      ctx.font = `600 ${fontSize}px -apple-system, "Segoe UI", "Microsoft YaHei", sans-serif`
      ctx.globalAlpha = Number(markOpacity.value)
      ctx.fillStyle = '#ffffff'
      ctx.shadowColor = 'rgba(0,0,0,.45)'
      ctx.shadowBlur = 6
      const padding = fontSize
      const metrics = ctx.measureText(text)
      ctx.textAlign = 'left'
      ctx.textBaseline = 'bottom'
      ctx.fillText(text, padding, canvas.height - padding)
      if (metrics.width > canvas.width * 0.9) {
        msg.className = 'msg warn'
        msg.textContent = '水印文字可能超出版面，缩短一点更安全'
      }
      ctx.globalAlpha = 1
      ctx.shadowBlur = 0
    }

    // 同步生成 blob（同步 API 拿不到，先挂个异步任务）
    canvas.toBlob((b) => {
      if (!b) return
      blob = b
      const saved = originalSize ? ` · 原图 ${fmtBytes(originalSize)}（${Math.round((1 - b.size / originalSize) * 100)}%）` : ''
      info.textContent = `${canvas.width}×${canvas.height} · ${fmtBytes(b.size)}${saved}`
    }, format, Number(quality.value))
  }

  function loadImage(src: string, name: string): void {
    msg.className = 'msg'
    msg.textContent = ''
    fileName = name.replace(/\.[^.]+$/, '') || 'image'
    const image = new Image()
    image.onload = () => {
      img = image
      info.textContent = `${image.width}×${image.height} · ${originalSize ? fmtBytes(originalSize) : '—'}`
      draw()
    }
    image.onerror = () => {
      msg.className = 'msg err'
      msg.textContent = '这张图打不开，换一张试试'
    }
    image.src = src
  }

  fileInput.addEventListener('change', async () => {
    const file = fileInput.files?.[0]
    if (!file) return
    originalSize = file.size
    const url = await readAsDataURL(file)
    loadImage(url, file.name)
  })

  offFiles = onFiles((detail) => {
    const image = detail.files.find((f) => f.type.startsWith('image/'))
    if (!image) return
    originalSize = image.size
    void readAsDataURL(image).then((url) => loadImage(url, image.name))
  })

  canvas.addEventListener('click', (e) => {
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    if (!ctx) return
    const rect = canvas.getBoundingClientRect()
    const x = Math.round(((e.clientX - rect.left) / rect.width) * canvas.width)
    const y = Math.round(((e.clientY - rect.top) / rect.height) * canvas.height)
    const data = ctx.getImageData(x, y, 1, 1).data
    const hex = `#${[data[0], data[1], data[2]].map((n) => n.toString(16).padStart(2, '0')).join('')}`
    void copy(hex.toUpperCase(), `取到 ${hex.toUpperCase()}`)
  })

  ;[widthInput, formatSel, quality, markInput, markOpacity].forEach((control) => {
    control.addEventListener('input', () => {
      if (!img) return
      msg.className = 'msg'
      msg.textContent = ''
      draw()
    })
    control.addEventListener('change', () => {
      if (!img) return
      draw()
    })
  })

  const bar = toolbar(
    btn('选图片', () => fileInput.click(), 'primary'),
    el('span', { class: 'hint', text: '最大宽度' }), widthInput,
    formatSel,
    el('span', { class: 'hint', text: '质量' }), quality, qualityLabel,
    el('span', { class: 'spacer' }),
    btn('下载', () => {
      if (!blob) return toast('先生成一次')
      const ext = formatSel.value === 'image/png' ? 'png' : formatSel.value === 'image/webp' ? 'webp' : 'jpg'
      download(`${fileName}-out.${ext}`, blob, formatSel.value)
    }),
    btn('九宫格切图', () => {
      if (!img) return toast('先选图片')
      draw()
      const size = Math.floor(Math.min(canvas.width, canvas.height) / 3)
      if (size < 10) return toast('图太小了，切出来看不清')
      const ctx = canvas.getContext('2d')
      if (!ctx) return
      for (let r = 0; r < 3; r++) {
        for (let c = 0; c < 3; c++) {
          const tile = document.createElement('canvas')
          tile.width = size
          tile.height = size
          tile.getContext('2d')?.drawImage(canvas, c * size, r * size, size, size, 0, 0, size, size)
          tile.toBlob((b) => {
            if (b) download(`${fileName}-${r + 1}${c + 1}.png`, b, 'image/png')
          }, 'image/png')
        }
      }
    }, 'ghost'),
  )

  const previewPane = pane('预览', el('div', { class: 'canvas-box', style: { minHeight: '200px' } }, canvas), {
    foot: foot(info, el('span', { class: 'spacer' }), el('span', { class: 'hint', text: '点图片任意位置可取色' })),
  })

  const settings = el(
    'div',
    { class: 'card-box stack', style: { marginBottom: '12px' } },
    el('div', { class: 'row' },
      el('span', { class: 'hint', text: '水印' }), markInput,
      el('span', { class: 'hint', text: '不透明度' }), markOpacity,
    ),
    el('div', { class: 'hint', text: '压缩、缩放、格式转换都在浏览器里完成，图片不会离开这台机器。' }),
  )

  root.append(msg, bar, settings, previewPane, fileInput)

  return () => offFiles?.()
}
