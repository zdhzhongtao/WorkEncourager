/**
 * 双栏工作台骨架。
 * 大部分工具都是「左边输入、右边输出」，把重复的工具栏、字数统计、
 * 复制 / 下载 / 发送到 逻辑收在这里，单个工具只写核心的一行转换。
 */
import { el } from './dom'
import { copy, debounce, download, fmtBytes, toast } from './kit'
import { btn, foot, pane, toolbar } from './ui'
import { sendMenu, shareLink } from './transfer'

export interface Workbench {
  root: HTMLElement
  input: HTMLTextAreaElement
  out: HTMLElement
  msg: HTMLElement
  inFoot: HTMLElement
  outFoot: HTMLElement
  toolbar: HTMLElement
  /** 最近一次成功输出 */
  current: string
  setOut(text: string, html?: boolean): void
  fail(text: string): void
  ok(text?: string): void
  clearMsg(): void
  run(): void
}

export interface WorkbenchOpts {
  toolId: string
  placeholder?: string
  outPh?: string
  sample?: string
  inputTitle?: string
  outTitle?: string
  accepts?: string[]
  produces?: string[]
  /** 追加到工具栏（spacer 之前）的控件 */
  controls?: HTMLElement[]
  /** 追加到输出面板头部的按钮 */
  outActions?: HTMLElement[]
  run: (text: string, wb: Workbench) => void | Promise<void>
  /** 输入变化自动跑，默认 true */
  auto?: boolean
  debounceMs?: number
  downloadName?: string
  downloadMime?: string
  /** 已经有自己的下载按钮时隐藏默认的 */
  hideDownload?: boolean
  /** 隐藏「示例」按钮 */
  noSample?: boolean
  /** 单栏模式（没有输入框） */
  noInput?: boolean
  initial?: string
  onInput?: (text: string) => void
}

export function workbench(root: HTMLElement, opts: WorkbenchOpts): Workbench {
  const msg = el('div', { class: 'msg' })
  const input = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: opts.placeholder ?? '在这里输入或粘贴…',
  })
  const out = el('pre', { class: 'out', dataset: { ph: opts.outPh ?? '结果会出现在这里…' } })
  const inFoot = el('span', { text: '' })
  const outFoot = el('span', { text: '' })

  const wb: Workbench = {
    root,
    input,
    out,
    msg,
    inFoot,
    outFoot,
    toolbar: el('div'),
    current: '',
    setOut(text, html = false) {
      wb.current = text
      if (html) out.innerHTML = text
      else out.textContent = text
      outFoot.textContent = text ? `${text.split('\n').length} 行 · ${text.length} 字符 · ${fmtBytes(new Blob([text]).size)}` : ''
    },
    fail(text) {
      msg.className = 'msg err'
      msg.textContent = text
    },
    ok(text = '') {
      msg.className = text ? 'msg ok' : 'msg'
      msg.textContent = text
    },
    clearMsg() {
      msg.className = 'msg'
      msg.textContent = ''
    },
    run() {
      const text = input.value
      inFoot.textContent = text ? `${text.length} 字符 · ${fmtBytes(new Blob([text]).size)}` : ''
      wb.clearMsg()
      try {
        void opts.run(text, wb)
      } catch (err) {
        wb.current = ''
        out.textContent = ''
        wb.fail((err as Error).message || String(err))
      }
    },
  }

  const doRun = opts.auto === false ? wb.run : debounce(wb.run, opts.debounceMs ?? 200)
  input.addEventListener('input', () => {
    opts.onInput?.(input.value)
    doRun()
  })
  input.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault()
      wb.run()
    }
  })

  const bar = toolbar(...(opts.controls ?? []))
  if (opts.controls?.length) bar.append(el('span', { class: 'spacer' }))
  if (!opts.noInput && opts.sample && !opts.noSample) {
    bar.append(
      btn('示例', () => {
        input.value = opts.sample ?? ''
        wb.run()
        input.focus()
      }, 'ghost'),
    )
  }
  if (!opts.noInput) {
    bar.append(
      btn('清空', () => {
        input.value = ''
        wb.setOut('')
        wb.clearMsg()
        input.focus()
      }, 'ghost'),
    )
  }
  wb.toolbar = bar

  const outActions = [
    btn('复制', () => copy(wb.current || out.textContent || '')),
    btn('分享链接', () => shareLink(opts.toolId, wb.current || out.textContent || '')),
    ...(opts.outActions ?? []),
  ]
  if (opts.produces?.length) {
    outActions.push(sendMenu(opts.produces, () => wb.current || out.textContent || ''))
  }
  if (!opts.hideDownload) {
    outActions.push(
      btn('下载', () => {
        const text = wb.current || out.textContent || ''
        if (!text) return toast('还没有内容')
        download(opts.downloadName ?? `${opts.toolId}.txt`, text, opts.downloadMime ?? 'text/plain;charset=utf-8')
      }, 'ghost'),
    )
  }

  const outPane = pane(opts.outTitle ?? '输出', out, { actions: outActions, foot: foot(outFoot) })

  if (opts.noInput) {
    root.append(msg, bar, el('div', { class: 'panes single' }, outPane))
  } else {
    const inputActions: HTMLElement[] = []
    const inputPane = pane(opts.inputTitle ?? '输入', input, {
      foot: foot(inFoot, el('span', { class: 'spacer' }), ...inputActions),
    })
    root.append(msg, bar, el('div', { class: 'panes' }, inputPane, outPane))
  }

  if (opts.initial) {
    input.value = opts.initial
    wb.run()
  } else {
    input.focus()
  }

  return wb
}

/** 常用：解析 JSON 并给出人话错误 */
export function parseJSON(text: string): unknown {
  if (!text.trim()) return undefined
  try {
    return JSON.parse(text)
  } catch (err) {
    const raw = (err as Error).message
    const pos = /position (\d+)/.exec(raw)
    if (pos) {
      const idx = Number(pos[1])
      const before = text.slice(0, idx)
      const line = before.split('\n').length
      const col = idx - before.lastIndexOf('\n')
      throw new Error(`JSON 不合法：第 ${line} 行第 ${col} 列附近 —— ${raw}`)
    }
    throw new Error(`JSON 不合法：${raw}`)
  }
}

/** 一行小提示 */
export function hint(text: string): HTMLElement {
  return el('div', { class: 'hint', text })
}

/** 字段行：label + 控件 */
export function field(label: string, control: HTMLElement): HTMLElement {
  return el('label', { class: 'field' }, el('span', { class: 'field-label', text: label }), control)
}
