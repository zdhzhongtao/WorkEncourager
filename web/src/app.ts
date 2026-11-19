import { CATEGORIES, TOOLS, countBy, findTool } from './core/registry'
import type { ToolEntry } from './core/types'
import { el } from './core/dom'
import { icon } from './core/icons'

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

async function openTool(id: string): Promise<void> {
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

  try {
    const mod = await tool.load()
    if (token !== loadToken) return
    body.innerHTML = ''
    const d = mod.default(body)
    if (typeof d === 'function') dispose = d
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
  if (raw.startsWith('t/')) {
    void openTool(decodeURIComponent(raw.slice(2)))
  } else {
    renderHome()
  }
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
    placeholder: '搜索工具…',
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
    el('div', { class: 'topmeta', text: `${TOOLS.length} 个工具 · 全部本地运行 · 不上传数据` }),
  )

  view = el('main', { class: 'view' })
  root.append(bar, view)

  window.addEventListener('hashchange', onRoute)
  window.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault()
      searchInput.focus()
      searchInput.select()
    }
  })

  onRoute()
}
