import { el } from './dom'

/** 小按钮 */
export function btn(label: string, onclick: (e: MouseEvent) => void, cls = ''): HTMLButtonElement {
  return el('button', { class: `btn ${cls}`.trim(), text: label, onclick })
}

/** 分段选择器 */
export function seg<T extends string | number>(
  items: Array<{ label: string; value: T }>,
  current: T,
  onPick: (v: T) => void,
): HTMLElement {
  const box = el('div', { class: 'seg' })
  const buttons: HTMLElement[] = []
  for (const item of items) {
    const b = el('button', {
      class: item.value === current ? 'on' : '',
      text: item.label,
      onclick: () => {
        buttons.forEach((x) => x.classList.remove('on'))
        b.classList.add('on')
        onPick(item.value)
      },
    })
    buttons.push(b)
    box.append(b)
  }
  return box
}

/** 复选框 */
export function check(label: string, checked: boolean, onChange: (v: boolean) => void): HTMLLabelElement {
  const input = el('input', {
    type: 'checkbox',
    checked: checked || undefined,
    onchange: (e: Event) => onChange((e.target as HTMLInputElement).checked),
  })
  return el('label', { class: 'check' }, input, label)
}

/** 输入/输出面板 */
export function pane(
  title: string,
  body: HTMLElement,
  opts?: { actions?: HTMLElement[]; foot?: HTMLElement; label?: HTMLElement },
): HTMLElement {
  const head = el(
    'div',
    { class: 'pane-head' },
    el('span', { text: title }),
    opts?.label ?? null,
    el('span', { class: 'spacer' }),
  )
  for (const a of opts?.actions ?? []) head.append(a)

  const node = el('div', { class: 'pane' }, head, body)
  if (opts?.foot) node.append(opts.foot)
  return node
}

/** 面板底部信息条 */
export function foot(...children: Array<HTMLElement | string>): HTMLElement {
  return el('div', { class: 'pane-foot' }, ...children)
}

/** 横向工具条 */
export function toolbar(...children: Array<HTMLElement | string>): HTMLElement {
  return el('div', { class: 'toolbar' }, ...children)
}
