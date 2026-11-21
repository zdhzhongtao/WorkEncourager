/** 全局拖拽文件的收发 */

export interface DropDetail {
  files: File[]
  /** 如果拖的是纯文本类文件，顺手读出来 */
  text?: string
  textName?: string
}

const EVENT = 'wtb:files'

/** 有多少个工具正在监听文件；有监听就认为「有人接管」，不再走文本兜底 */
let listeners = 0

export function emitFiles(detail: DropDetail): boolean {
  const ev = new CustomEvent<DropDetail>(EVENT, { detail, cancelable: true })
  window.dispatchEvent(ev)
  return ev.defaultPrevented || listeners > 0
}

export function onFiles(cb: (detail: DropDetail) => void): () => void {
  const handler = (e: Event) => cb((e as CustomEvent<DropDetail>).detail)
  window.addEventListener(EVENT, handler)
  listeners++
  return () => {
    window.removeEventListener(EVENT, handler)
    listeners = Math.max(0, listeners - 1)
  }
}

export function readAsText(file: File): Promise<string> {
  return file.text()
}

export function readAsDataURL(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('读取失败'))
    reader.readAsDataURL(file)
  })
}

export function readAsArrayBuffer(file: File): Promise<ArrayBuffer> {
  return file.arrayBuffer()
}

/** 判断是否值得当作纯文本塞进输入框 */
export function isTextFile(file: File): boolean {
  if (file.type.startsWith('text/')) return true
  return /\.(txt|json|md|markdown|xml|html|css|js|ts|tsx|jsx|yaml|yml|toml|csv|sql|log|ini|conf|env|sh|ps1|py|go|java|cs)$/i.test(
    file.name,
  )
}
