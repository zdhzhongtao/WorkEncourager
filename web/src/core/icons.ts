/**
 * 内联 SVG 图标。
 * 不用 emoji / 特殊字符，避免不同系统字体缺字变成豆腐块，
 * 也不引图标库，省下一个网络请求。
 */

const P: Record<string, string> = {
  braces:
    '<path d="M9 3H7.5A2.5 2.5 0 0 0 5 5.5V9a3 3 0 0 1-3 3 3 3 0 0 1 3 3v3.5A2.5 2.5 0 0 0 7.5 21H9"/><path d="M15 3h1.5A2.5 2.5 0 0 1 19 5.5V9a3 3 0 0 0 3 3 3 3 0 0 0-3 3v3.5a2.5 2.5 0 0 1-2.5 2.5H15"/>',
  diff:
    '<path d="M3 8h13"/><path d="m13 5 3 3-3 3"/><path d="M21 16H8"/><path d="m11 13-3 3 3 3"/>',
  codec: '<path d="m9 7-5 5 5 5"/><path d="m15 7 5 5-5 5"/>',
  lock:
    '<rect x="4" y="10" width="16" height="10" rx="2.5"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/><path d="M12 14v2"/>',
  hash: '<path d="M4 9h16"/><path d="M4 15h16"/><path d="M10 3 8 21"/><path d="M16 3l-2 18"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5.2l3.2 2"/>',
  asterisk: '<path d="M12 4v16"/><path d="m5 7.8 14 8.4"/><path d="m19 7.8-14 8.4"/>',
  card: '<rect x="3" y="5" width="18" height="14" rx="2.5"/><circle cx="8.5" cy="11" r="1.6"/><path d="M6 16c.6-1.4 1.5-2 2.5-2s1.9.6 2.5 2"/><path d="M14 10h4"/><path d="M14 14h4"/>',
  droplet: '<path d="M12 3.2c3.2 3.4 6 6.9 6 9.8a6 6 0 0 1-12 0c0-2.9 2.8-6.4 6-9.8Z"/>',
  qr: '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><path d="M14 14h3v3h-3z"/><path d="M20.5 14v.01"/><path d="M14 20.5v.01"/><path d="M20.5 20.5v.01"/>',
  grid:
    '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>',
  star: '<path d="m12 3.6 2.7 5.5 6 .9-4.35 4.25 1.03 6-5.38-2.83-5.38 2.83 1.03-6L3.3 10l6-.9Z"/>',
  history: '<path d="M3.6 12a8.4 8.4 0 1 0 2.5-6"/><path d="M3.4 5v4h4"/><path d="M12 8v4.4l3 1.8"/>',
}

export type IconKey = keyof typeof P

/** 返回一个 span 包裹的 SVG，尺寸由外层 CSS 决定 */
export function icon(key: string, extraClass = ''): HTMLElement {
  const body = P[key] ?? P.grid
  const span = document.createElement('span')
  span.className = `ico ${extraClass}`.trim()
  span.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`
  return span
}
