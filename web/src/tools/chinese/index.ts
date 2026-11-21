import { Converter } from 'opencc-js'
import { pinyin } from 'pinyin-pro'
import { el } from '../../core/dom'
import { workbench } from '../../core/workbench'

type Mode = 'cn2tw' | 'cn2hk' | 't2cn' | 'pinyin' | 'pinyinPlain' | 'initials' | 'toHalf' | 'toFull'

const MODES: Array<{ value: Mode; label: string }> = [
  { value: 'cn2tw', label: '简体 → 繁体（台湾）' },
  { value: 'cn2hk', label: '简体 → 繁体（香港）' },
  { value: 't2cn', label: '繁体 → 简体' },
  { value: 'pinyin', label: '拼音（带声调）' },
  { value: 'pinyinPlain', label: '拼音（无声调）' },
  { value: 'initials', label: '拼音首字母' },
  { value: 'toHalf', label: '全角 → 半角' },
  { value: 'toFull', label: '半角 → 全角' },
]

const cn2tw = Converter({ from: 'cn', to: 'tw' })
const cn2hk = Converter({ from: 'cn', to: 'hk' })
const t2cn = Converter({ from: 'tw', to: 'cn' })

function toHalf(text: string): string {
  return text.replace(/[\uFF01-\uFF5E]/g, (c) => String.fromCharCode(c.charCodeAt(0) - 0xfee0)).replace(/\u3000/g, ' ')
}

function toFull(text: string): string {
  return text.replace(/[\u0021-\u007E]/g, (c) => String.fromCharCode(c.charCodeAt(0) + 0xfee0)).replace(/ /g, '\u3000')
}

function convert(text: string, mode: Mode): string {
  switch (mode) {
    case 'cn2tw':
      return cn2tw(text)
    case 'cn2hk':
      return cn2hk(text)
    case 't2cn':
      return t2cn(text)
    case 'pinyin':
      return pinyin(text, { toneType: 'symbol', type: 'string', nonZh: 'consecutive' })
    case 'pinyinPlain':
      return pinyin(text, { toneType: 'none', type: 'string', nonZh: 'consecutive' })
    case 'initials':
      return pinyin(text, { pattern: 'first', toneType: 'none', type: 'string', nonZh: 'consecutive' })
    case 'toHalf':
      return toHalf(text)
    case 'toFull':
      return toFull(text)
  }
}

const SAMPLE = '我们在维护一个关账平台，支持简体与繁體互转、拼音标注，还有全角ＡＢＣ和半角abc。'

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let mode: Mode = 'cn2tw'
  const chips = el('div', { class: 'chips' })
  let wb: ReturnType<typeof workbench>

  function paint(): void {
    chips.innerHTML = ''
    for (const m of MODES) {
      chips.append(
        el('button', {
          class: `chip${mode === m.value ? ' on' : ''}`,
          text: m.label,
          onclick: () => {
            mode = m.value
            paint()
            wb.run()
          },
        }),
      )
    }
  }

  wb = workbench(root, {
    toolId: 'chinese',
    placeholder: '粘贴中文…',
    outPh: '转换结果…',
    sample: SAMPLE,
    initial: ctx.src ?? undefined,
    produces: ['text'],
    downloadName: 'chinese.txt',
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      w.setOut(convert(text, mode))
      w.ok()
    },
  })

  paint()
  root.insertBefore(el('div', { class: 'card-box row', style: { marginBottom: '12px' } }, chips), root.firstChild)
}
