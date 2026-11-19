import { el } from '../../core/dom'
import { copy, fmtBytes } from '../../core/kit'
import { md5 } from '../../core/md5'
import { btn, check, pane, toolbar } from '../../core/ui'

type Algo = 'MD5' | 'SHA-1' | 'SHA-256' | 'SHA-384' | 'SHA-512'

const ALGOS: Algo[] = ['MD5', 'SHA-1', 'SHA-256', 'SHA-384', 'SHA-512']
const ENC = new TextEncoder()

const toHex = (buf: ArrayBuffer): string =>
  Array.from(new Uint8Array(buf))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('')

export default function render(root: HTMLElement) {
  let disposed = false
  let token = 0
  let upper = false
  let timer = 0
  let fileBytes: Uint8Array<ArrayBuffer> | null = null
  let fileName = ''
  const selected = new Set<Algo>(['MD5', 'SHA-256'])

  const input = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '输入要计算哈希的文本，或点「选择文件」/ 把文件拖进来…',
  })

  const resultHost = el('div', { class: 'stack' })
  const msg = el('div', { class: 'msg' })
  const fileTag = el('span', { class: 'hint' })
  const hasSubtle = typeof crypto !== 'undefined' && Boolean(crypto.subtle)

  async function compute(): Promise<void> {
    const my = ++token
    const data = fileBytes ?? ENC.encode(input.value)

    if (!data.length || selected.size === 0) {
      resultHost.innerHTML = ''
      msg.className = 'msg'
      msg.textContent = selected.size === 0 ? '至少选一个算法' : ''
      return
    }

    const rows: Array<[Algo, string]> = []
    for (const algo of ALGOS) {
      if (!selected.has(algo)) continue
      let hex = ''
      if (algo === 'MD5') {
        hex = md5(data)
      } else if (!hasSubtle) {
        hex = '当前环境不支持 WebCrypto（需 https 或 localhost）'
      } else {
        hex = toHex(await crypto.subtle.digest(algo, data))
      }
      if (disposed || my !== token) return
      rows.push([algo, upper ? hex.toUpperCase() : hex])
    }
    if (disposed || my !== token) return

    resultHost.innerHTML = ''
    const body = el('tbody', null)
    for (const [algo, hex] of rows) {
      body.append(
        el(
          'tr',
          null,
          el('td', { class: 'k', text: algo }),
          el('td', {
            class: 'v',
            text: hex,
            title: '点击复制',
            style: { cursor: 'pointer' },
            onclick: () => copy(hex, `${algo} 已复制`),
          }),
          el('td', { text: `${Math.round(hex.length / 2)} 字节` }),
        ),
      )
    }

    resultHost.append(
      el(
        'table',
        { class: 'data' },
        el(
          'thead',
          null,
          el('tr', null, el('th', { text: '算法' }), el('th', { text: '摘要（点击复制）' }), el('th', { text: '长度' })),
        ),
        body,
      ),
    )

    msg.className = 'msg info'
    msg.textContent = fileBytes
      ? `按文件「${fileName}」计算，${fmtBytes(fileBytes.length)}`
      : `按文本计算，${data.length} 字节（UTF-8）`
  }

  function loadFile(f: File): void {
    void f.arrayBuffer().then((buf) => {
      fileBytes = new Uint8Array(buf)
      fileName = f.name
      fileTag.textContent = f.name
      void compute()
    })
  }

  const picker = el('input', { type: 'file', style: { display: 'none' } })
  picker.addEventListener('change', () => {
    const f = picker.files?.[0]
    if (f) loadFile(f)
  })

  const inputPane = pane('输入', input, {
    label: fileTag,
    actions: [
      btn('选择文件', () => picker.click()),
      btn('清空', () => {
        input.value = ''
        fileBytes = null
        fileName = ''
        fileTag.textContent = ''
        picker.value = ''
        void compute()
      }, 'ghost'),
    ],
  })

  for (const type of ['dragover', 'drop']) {
    inputPane.addEventListener(type, (e) => e.preventDefault())
  }
  inputPane.addEventListener('drop', (e) => {
    const f = (e as DragEvent).dataTransfer?.files?.[0]
    if (f) loadFile(f)
  })

  input.addEventListener('input', () => {
    window.clearTimeout(timer)
    timer = window.setTimeout(() => void compute(), 200)
  })

  const algoBoxes = el('div', { class: 'row' })
  for (const algo of ALGOS) {
    algoBoxes.append(
      check(algo, selected.has(algo), (v) => {
        if (v) selected.add(algo)
        else selected.delete(algo)
        void compute()
      }),
    )
  }

  const bar = toolbar(
    algoBoxes,
    el('span', { class: 'spacer' }),
    check('大写', false, (v) => {
      upper = v
      void compute()
    }),
    btn('全部复制', () => {
      const lines = Array.from(resultHost.querySelectorAll('tbody tr')).map((tr) => {
        const tds = tr.querySelectorAll('td')
        return `${tds[0]?.textContent ?? ''}: ${tds[1]?.textContent ?? ''}`
      })
      copy(lines.join('\n'), '已复制全部摘要')
    }),
  )

  root.append(msg, bar, inputPane, el('div', { class: 'card-box', style: { marginTop: '12px' } }, resultHost), picker)
  input.focus()

  return () => {
    disposed = true
    window.clearTimeout(timer)
  }
}
