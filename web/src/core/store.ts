/**
 * 本地持久化：片段库 + 通用读写。
 * 全部走 localStorage，不联网。隐私模式下写入失败就静默降级。
 */

export interface Snippet {
  id: string
  name: string
  text: string
  at: number
}

const SNIPPET_KEY = 'wtb.snippets.v1'

export function lsGet<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as T) : fallback
  } catch {
    return fallback
  }
}

export function lsSet(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    /* 配额满 / 隐私模式：忽略 */
  }
}

export function listSnippets(): Snippet[] {
  const arr = lsGet<Snippet[]>(SNIPPET_KEY, [])
  return Array.isArray(arr) ? arr.filter((s) => s && typeof s.text === 'string') : []
}

export function saveSnippet(name: string, text: string): Snippet {
  const list = listSnippets()
  const snippet: Snippet = {
    id: Math.random().toString(36).slice(2, 10),
    name: name.trim() || '未命名片段',
    text,
    at: Date.now(),
  }
  lsSet(SNIPPET_KEY, [snippet, ...list].slice(0, 80))
  return snippet
}

export function removeSnippet(id: string): void {
  lsSet(SNIPPET_KEY, listSnippets().filter((s) => s.id !== id))
}

export function renameSnippet(id: string, name: string): void {
  lsSet(
    SNIPPET_KEY,
    listSnippets().map((s) => (s.id === id ? { ...s, name: name.trim() || s.name } : s)),
  )
}
