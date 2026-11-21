import { CATEGORIES, TOOLS, countBy, findTool } from './core/registry'
import type { ToolCtx, ToolEntry } from './core/types'
import { el } from './core/dom'
import { icon } from './core/icons'
import { copy, toast } from './core/kit'
import { btn } from './core/ui'
import { emitFiles, isTextFile, readAsText } from './core/files'
import { listSnippets, removeSnippet, renameSnippet, saveSnippet } from './core/store'
import { sendTo } from './core/transfer'
import { SHORTCUTS, openShortcuts } from './core/shortcuts'

const FAV_KEY = 'wtb.favs.v1'
const RECENT_KEY = 'wtb.recent.v1'

type Filter = 'all' | 'fav' | 'recent' | string

let filter: Filter = 'all'
let query = ''
let dispose: (() => void) | null = null
let loadToken = 0

let view: HTMLElement
let searchInput: HTMLInputElement
let gridHost: HTMLElement

/* ---------------- 本地存储 ---------------- */

function readIds(key: string): string[] {
  try {
    const raw = localStorage.getItem(key)
    const arr = raw ? JSON.parse(raw) : []
    return Array.isArray(arr) ? arr.filter((x): x is string => typeof x === 'string') : []
  } catch {
    return []
  }
}

function writeIds(key: string, ids: string[]): void {
  try {
    localStorage.setItem(key, JSON.stringify(ids))
  } catch {
    /* 隐私模式 / 存储被禁用时静默忽略，不影响功能 */
  }
}

function toggleFav(id: string): void {
  const favs = readIds(FAV_KEY)
  const next = favs.includes(id) ? favs.filter((x) => x !== id) : [id, ...favs]
  writeIds(FAV_KEY, next)
  renderGrid()
}

function pushRecent(id: string): void {
  const list = readIds(RECENT_KEY).filter((x) => x !== id)
  writeIds(RECENT_KEY, [id, ...list].slice(0, 8))
}

/* ---------------- 卡片 ---------------- */

function makeCard(tool: ToolEntry): HTMLElement {
  const favs = readIds(FAV_KEY)
  const on = favs.includes(tool.id)

  const favBtn = el('button', {
    class: `card-fav${on ? ' on' : ''}`,
    title: on ? '取消收藏' : '收藏',
    text: on ? '★' : '☆',
    onclick: (e: Event) => {
      e.preventDefault()
      e.stopPropagation()
      toggleFav(tool.id)
    },
  })

  return el(
    'a',
    { class: 'card', href: `#/t/${tool.id}` },
    el('span', { class: 'card-ico' }, icon(tool.icon)),
    el(
      'div',
      { class: 'card-main' },
      el('div', { class: 'card-name', text: tool.name }),
      el('div', { class: 'card-desc', text: tool.desc }),
    ),
    favBtn,
  )
}

/* ---------------- 过滤 ---------------- */

function filtered(): ToolEntry[] {
  const favs = readIds(FAV_KEY)
  const recents = readIds(RECENT_KEY)
  let list = TOOLS

  if (filter === 'fav') list = list.filter((t) => favs.includes(t.id))
  else if (filter === 'recent') {
    list = recents.map((id) => findTool(id)).filter((t): t is ToolEntry => Boolean(t))
  } else if (filter !== 'all') list = list.filter((t) => t.category === filter)

  if (query) {
    const q = query.toLowerCase()
    list = list.filter((t) =>
      `${t.name} ${t.desc} ${t.keywords.join(' ')}`.toLowerCase().includes(q),
    )
  }
  return list
}

/* ---------------- 网格 ---------------- */

function renderGrid(): void {
  if (!gridHost) return
  gridHost.innerHTML = ''
  const items = filtered()

  if (!items.length) {
    const hint =
      filter === 'fav'
        ? '还没有收藏。把鼠标移到卡片上，点右上角 ☆ 收藏常用工具。'
        : filter === 'recent'
          ? '还没有使用记录。打开任意工具后会出现在这里。'
          : '没有匹配的工具，换个关键词试试。'
    gridHost.append(el('div', { class: 'empty', text: hint }))
    return
  }

  const g = el('div', { class: 'grid' })
  items.forEach((t) => g.append(makeCard(t)))
  gridHost.append(g)
}

/* ---------------- 侧边栏 ---------------- */

function buildSide(): HTMLElement {
  const side = el('aside', { class: 'side' })
  const recents = readIds(RECENT_KEY)

  const addItem = (id: Filter, label: string, icoKey: string, count: number) => {
    const btn = el(
      'button',
      {
        class: `side-item${filter === id ? ' active' : ''}`,
        onclick: () => {
          filter = id
          renderHome()
        },
      },
      el('span', { class: 'side-ico' }, icon(icoKey)),
      el('span', { text: label }),
      count > 0 ? el('span', { class: 'count', text: String(count) }) : null,
    )
    side.append(btn)
  }

  side.append(el('div', { class: 'side-label', text: '快速' }))
  addItem('all', '全部工具', 'grid', TOOLS.length)
  addItem('fav', '我的收藏', 'star', readIds(FAV_KEY).length)
  addItem('recent', '最近使用', 'history', recents.length)
  side.append(
    el(
      'button',
      { class: 'side-item', onclick: () => { location.hash = '#/snippets' } },
      el('span', { class: 'side-ico' }, icon('file')),
      el('span', { text: '片段库' }),
      listSnippets().length ? el('span', { class: 'count', text: String(listSnippets().length) }) : null,
    ),
  )

  side.append(el('div', { class: 'side-label', text: '分类' }))
  for (const c of CATEGORIES) addItem(c.id, c.label, c.icon, countBy(c.id))

  return side
}

/* ---------------- 首页 ---------------- */

function renderHome(): void {
  cleanup()
  loadToken++
  view.innerHTML = ''
  document.title = 'WadeToolbox · 前端开发者工具箱'
  searchInput.value = query
  searchInput.blur()

  gridHost = el('div', { class: 'content' })
  view.append(el('div', { class: 'home' }, buildSide(), gridHost))
  renderGrid()
}

/* ---------------- 片段库 ---------------- */

function renderSnippets(): void {
  cleanup()
  loadToken++
  view.innerHTML = ''
  document.title = '片段库 · WadeToolbox'

  const nameInput = el('input', { class: 'inp w-md', placeholder: '片段名称（比如「生产 JWT 公钥」）' })
  const textInput = el('textarea', {
    class: 'ta',
    spellcheck: false,
    placeholder: '粘贴要长期留存的 JSON / 正则 / Token / 命令…存在本机浏览器里，不上传。',
    style: { minHeight: '110px' },
  })

  const listHost = el('div', { class: 'stack', style: { marginTop: '18px' } })

  function renderList(): void {
    listHost.innerHTML = ''
    const items = listSnippets()
    if (!items.length) {
      listHost.append(el('div', { class: 'empty', text: '还没有片段。上面粘点东西，保存下来就会出现在这里。' }))
      return
    }
    for (const s of items) {
      const select = el('select', { class: 'inp w-md' })
      select.append(el('option', { value: '', text: '打开到…' }))
      for (const t of TOOLS) select.append(el('option', { value: t.id, text: t.name }))
      select.addEventListener('change', () => {
        if (select.value) sendTo(select.value, s.text)
      })

      const card = el(
        'div',
        { class: 'card-box' },
        el(
          'div',
          { class: 'row' },
          el('strong', { text: s.name }),
          el('span', { class: 'hint', text: new Date(s.at).toLocaleString('zh-CN') }),
          el('span', { class: 'spacer' }),
          btn('复制', () => copy(s.text)),
          select,
          btn(
            '重命名',
            () => {
              const next = window.prompt('新名字', s.name)
              if (next) {
                renameSnippet(s.id, next)
                renderList()
              }
            },
            'ghost',
          ),
          btn(
            '删除',
            () => {
              if (!window.confirm(`删除片段「${s.name}」？`)) return
              removeSnippet(s.id)
              renderList()
            },
            'ghost',
          ),
        ),
        el('pre', { class: 'plain', style: { marginTop: '8px', color: 'var(--text-dim)' }, text: s.text.slice(0, 320) + (s.text.length > 320 ? '…' : '') }),
      )
      listHost.append(card)
    }
  }

  view.append(
    el(
      'div',
      { class: 'tool-wrap' },
      el('a', { class: 'back', href: '#/' }, '← 全部工具'),
      el('h1', { class: 'tool-title', text: '片段库' }),
      el('p', { class: 'tool-desc', text: '常用的长文本存在本机浏览器，随时复制或一键发到任意工具。' }),
      el(
        'div',
        { class: 'card-box stack' },
        el('div', { class: 'row' }, nameInput),
        textInput,
        el(
          'div',
          { class: 'row' },
          btn(
            '保存片段',
            () => {
              if (!textInput.value.trim()) return toast('内容是空的')
              saveSnippet(nameInput.value, textInput.value)
              nameInput.value = ''
              textInput.value = ''
              renderList()
              toast('已保存到本机')
            },
            'primary',
          ),
          el('span', { class: 'hint', text: '只存在这台浏览器的 localStorage，换设备不同步。' }),
        ),
      ),
      listHost,
    ),
  )
  renderList()
  textInput.focus()
}

/* ---------------- 工具页 ---------------- */

function cleanup(): void {
  if (dispose) {
    try {
      dispose()
    } catch {
      /* 清理失败不影响后续 */
    }
    dispose = null
  }
}

async function openTool(id: string, src: string | null): Promise<void> {
  const tool = findTool(id)
  if (!tool) {
    location.hash = '#/'
    return
  }

  cleanup()
  const token = ++loadToken
  query = ''
  view.innerHTML = ''
  searchInput.value = ''

  const body = el('div', { class: 'tool-body' })
  view.append(
    el(
      'div',
      { class: 'tool-wrap' },
      el('a', { class: 'back', href: '#/' }, '← 全部工具'),
      el(
        'div',
        { class: 'tool-head' },
        el('h1', { class: 'tool-title', text: tool.name }),
        el('p', { class: 'tool-desc', text: tool.desc }),
      ),
      body,
    ),
  )
  body.append(el('div', { class: 'loading', text: '加载中…' }))
  document.title = `${tool.name} · WadeToolbox`
  pushRecent(id)

  const ctx: ToolCtx = { src, files: [] }

  try {
    const mod = await tool.load()
    if (token !== loadToken) return
    body.innerHTML = ''
    const d = mod.default(body, ctx)
    if (typeof d === 'function') dispose = d
    if (src) toast('已填入传入的内容')
  } catch (err) {
    if (token !== loadToken) return
    body.innerHTML = ''
    body.append(el('div', { class: 'msg err', text: `工具加载失败：${String(err)}` }))
  }
  window.scrollTo({ top: 0, behavior: 'auto' })
}

/* ---------------- 路由 ---------------- */

function onRoute(): void {
  const raw = location.hash.replace(/^#\/?/, '')
  const qIndex = raw.indexOf('?')
  const path = qIndex >= 0 ? raw.slice(0, qIndex) : raw
  const search = qIndex >= 0 ? raw.slice(qIndex + 1) : ''

  if (path === 'snippets') {
    renderSnippets()
    return
  }
  if (path.startsWith('t/')) {
    const params = new URLSearchParams(search)
    let src: string | null = null
    const encoded = params.get('src')
    if (encoded) {
      try {
        src = decodeBase64Url(encoded)
      } catch {
        src = null
      }
    } else if (params.get('pending')) {
      try {
        src = sessionStorage.getItem('wtb.pending.v1')
        sessionStorage.removeItem('wtb.pending.v1')
      } catch {
        src = null
      }
    }
    void openTool(decodeURIComponent(path.slice(2)), src)
    return
  }
  renderHome()
}

function decodeBase64Url(raw: string): string {
  const clean = raw.replace(/-/g, '+').replace(/_/g, '/')
  const pad = (4 - (clean.length % 4)) % 4
  const bin = atob(clean + '='.repeat(pad))
  return new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0)))
}

/* ---------------- 搜索 ---------------- */

function onSearchInput(e: Event): void {
  query = (e.target as HTMLInputElement).value
  if (!document.querySelector('.home')) {
    location.hash = '#/'
    return
  }
  renderGrid()
}

/* ---------------- 全局拖拽 ---------------- */

function setupDrop(): void {
  let overlay: HTMLElement | null = null
  let depth = 0

  const show = () => {
    if (overlay) return
    overlay = el('div', { class: 'drop-overlay' }, el('div', { class: 'drop-card' }, '松手就丢给当前工具'))
    document.body.appendChild(overlay)
  }
  const hide = () => {
    overlay?.remove()
    overlay = null
    depth = 0
  }

  window.addEventListener('dragenter', (e) => {
    if (!e.dataTransfer?.types.includes('Files')) return
    depth++
    show()
  })
  window.addEventListener('dragover', (e) => {
    if (!e.dataTransfer?.types.includes('Files')) return
    e.preventDefault()
  })
  window.addEventListener('dragleave', () => {
    depth--
    if (depth <= 0) hide()
  })
  window.addEventListener('drop', (e) => {
    if (!e.dataTransfer?.files?.length) return
    e.preventDefault()
    hide()
    void handleDrop(Array.from(e.dataTransfer.files))
  })
}

async function handleDrop(files: File[]): Promise<void> {
  const first = files.find(isTextFile)
  let text: string | undefined
  if (first && first.size < 8 * 1024 * 1024) {
    try {
      text = await readAsText(first)
    } catch {
      text = undefined
    }
  }
  const handled = emitFiles({ files, text, textName: first?.name })
  if (handled) return

  const body = document.querySelector('.tool-body')
  if (text !== undefined && body) {
    const ta = body.querySelector('textarea')
    if (ta) {
      ta.value = text
      ta.dispatchEvent(new Event('input', { bubbles: true }))
      toast(`已填入 ${first?.name ?? '文件内容'}`)
      return
    }
  }
  toast(files.length > 1 ? '这个工具一次只吃一个文件' : '当前工具不支持这种文件')
}

/* ---------------- 启动 ---------------- */

export function renderApp(root: HTMLElement): void {
  const brand = el(
    'a',
    { class: 'brand', href: '#/' },
    el('span', { class: 'logo', text: 'W' }),
    el('span', { class: 'brand-text' }, 'WadeToolbox', el('em', null, '前端工具箱')),
  )

  searchInput = el('input', {
    type: 'search',
    placeholder: '搜索工具…（Ctrl / ⌘ + K）',
    autocomplete: 'off',
    spellcheck: false,
    oninput: onSearchInput,
    onkeydown: (e: Event) => {
      const ev = e as KeyboardEvent
      if (ev.key === 'Escape') {
        searchInput.value = ''
        query = ''
        searchInput.blur()
        if (document.querySelector('.home')) renderGrid()
        else location.hash = '#/'
      }
    },
  })

  const bar = el(
    'header',
    { class: 'topbar' },
    brand,
    el('div', { class: 'search' }, searchInput),
    el(
      'button',
      { class: 'btn ghost kb-btn', title: `快捷键（${SHORTCUTS.length} 条）`, text: '?', onclick: () => openShortcuts() },
    ),
    el('div', { class: 'topmeta', text: `${TOOLS.length} 个工具 · 全部本地运行 · 不上传数据` }),
  )

  view = el('main', { class: 'view' })
  root.append(bar, view)

  window.addEventListener('hashchange', onRoute)
  window.addEventListener('keydown', (e) => {
    const target = e.target as HTMLElement | null
    const typing = target && /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault()
      searchInput.focus()
      searchInput.select()
      return
    }
    if (!typing && (e.key === '?' || (e.shiftKey && e.key === '/'))) {
      e.preventDefault()
      openShortcuts()
    }
  })

  setupDrop()
  onRoute()
}
