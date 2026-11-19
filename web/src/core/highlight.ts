import { escapeHtml } from './dom'

/** 给已经格式化好的 JSON 字符串上色，返回可安全插入的 HTML */
export function highlightJSON(raw: string): string {
  const re = /("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*")(\s*:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g
  let out = ''
  let last = 0
  let m: RegExpExecArray | null

  while ((m = re.exec(raw)) !== null) {
    out += escapeHtml(raw.slice(last, m.index))
    const [full, str, colon, lit, num] = m
    if (str !== undefined) {
      out += `<span class="tok-${colon ? 'key' : 'str'}">${escapeHtml(str)}</span>`
      if (colon) out += escapeHtml(colon)
    } else if (lit !== undefined) {
      out += `<span class="tok-bool">${lit}</span>`
    } else if (num !== undefined) {
      out += `<span class="tok-num">${escapeHtml(num)}</span>`
    }
    last = m.index + full.length
  }
  out += escapeHtml(raw.slice(last))
  return out
}

/** 把字节偏移换算成行列号（1 起） */
export function locate(text: string, pos: number): { line: number; col: number } {
  const clamped = Math.max(0, Math.min(pos, text.length))
  const before = text.slice(0, clamped)
  const line = before.split('\n').length
  const col = clamped - before.lastIndexOf('\n')
  return { line, col }
}
