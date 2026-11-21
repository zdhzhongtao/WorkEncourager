export type CategoryId = 'data' | 'codec' | 'crypto' | 'time' | 'text' | 'design' | 'gen'

/** 渲染函数拿到的上下文：深链参数、拖进来的文件 */
export interface ToolCtx {
  /** 从 URL ?src= 或「发送到…」传进来的文本，没有则为 null */
  src: string | null
  /** 全局拖拽落到当前工具的文件 */
  files: File[]
}

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
  /** 能接收的输入类型，决定「发送到…」里是否出现它 */
  accepts?: string[]
  /** 能产出的类型，决定「发送到…」里列给谁 */
  produces?: string[]
}

export type ToolRender = (root: HTMLElement, ctx: ToolCtx) => void | (() => void)

export interface ToolEntry extends ToolMeta {
  load: () => Promise<{ default: ToolRender }>
}

export interface Category {
  id: CategoryId
  label: string
  icon: string
}
