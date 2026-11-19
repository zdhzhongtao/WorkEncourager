/* 各工具共用的小能力：复制、下载、提示、防抖 */

export function toast(msg: string): void {
  let host = document.getElementById('toast-host')
  if (!host) {
    host = document.createElement('div')
    host.id = 'toast-host'
    document.body.appendChild(host)
  }
  const node = document.createElement('div')
  node.className = 'toast'
  node.textContent = msg
  host.appendChild(node)
  setTimeout(() => node.remove(), 1600)
}

export async function copy(text: string, label = '已复制'): Promise<void> {
  if (!text) {
    toast('没有可复制的内容')
    return
  }
  try {
    await navigator.clipboard.writeText(text)
    toast(label)
  } catch {
    // 老浏览器 / 非 https 环境兜底
    const ta = document.createElement('textarea')
    ta.value = text
    ta.style.position = 'fixed'
    ta.style.opacity = '0'
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    ta.remove()
    toast(label)
  }
}

export function download(filename: string, content: string | Blob, mime = 'text/plain;charset=utf-8'): void {
  const blob = typeof content === 'string' ? new Blob([content], { type: mime }) : content
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 3000)
}

export function debounce<A extends unknown[]>(fn: (...args: A) => void, ms = 180): (...args: A) => void {
  let timer = 0
  return (...args: A) => {
    window.clearTimeout(timer)
    timer = window.setTimeout(() => fn(...args), ms)
  }
}

export function fmtBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(2)} MB`
}
