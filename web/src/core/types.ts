export type CategoryId = 'data' | 'codec' | 'crypto' | 'time' | 'text' | 'design' | 'gen'

export interface ToolMeta {
  /** 路由 id，同时也是 URL 片段：#/t/<id> */
  id: string
  name: string
  /** 一句话说明，卡片上显示 */
  desc: string
  keywords: string[]
  category: CategoryId
  /** 卡片上的小图标，用字符即可，避免图片依赖 */
  icon: string
}

export type ToolRender = (root: HTMLElement) => void | (() => void)

export interface ToolEntry extends ToolMeta {
  load: () => Promise<{ default: ToolRender }>
}

export interface Category {
  id: CategoryId
  label: string
  icon: string
}
