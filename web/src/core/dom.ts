type Child = Node | string | number | null | undefined | false

/** 极简 DOM 构造器，替代模板字符串，避免到处 innerHTML 拼接 */
export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props?: Record<string, unknown> | null,
  ...children: Child[]
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag)
  if (props) {
    for (const [key, value] of Object.entries(props)) {
      if (value === null || value === undefined || value === false) continue
      if (key === 'class') node.className = String(value)
      else if (key === 'html') node.innerHTML = String(value)
      else if (key === 'text') node.textContent = String(value)
      else if (key.startsWith('on') && typeof value === 'function') {
        node.addEventListener(key.slice(2).toLowerCase(), value as EventListener)
      } else if (key === 'style' && typeof value === 'object') {
        Object.assign(node.style, value as Partial<CSSStyleDeclaration>)
      } else if (key === 'dataset' && typeof value === 'object') {
        Object.assign(node.dataset, value as Record<string, string>)
      } else if (key in node) {
        ;(node as unknown as Record<string, unknown>)[key] = value
      } else {
        node.setAttribute(key, String(value))
      }
    }
  }
  appendAll(node, children)
  return node
}

export function appendAll(parent: Node, children: Child[]): void {
  for (const c of children) {
    if (c === null || c === undefined || c === false) continue
    parent.appendChild(typeof c === 'string' || typeof c === 'number' ? document.createTextNode(String(c)) : c)
  }
}

export function clear(node: Node): void {
  while (node.firstChild) node.removeChild(node.firstChild)
}

const ESC: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
}

export function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ESC[c] ?? c)
}

/** 数据面板：左键右键的键值表 */
export function kv(pairs: Array<[string, string]>): HTMLElement {
  const box = el('div', { class: 'kv' })
  for (const [k, v] of pairs) {
    box.append(el('div', { text: k }), el('div', { text: v }))
  }
  return box
}
