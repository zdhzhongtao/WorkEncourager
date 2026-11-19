import type { Category, ToolEntry } from './types'

export const CATEGORIES: Category[] = [
  { id: 'data', label: '数据与格式', icon: 'braces' },
  { id: 'codec', label: '编码转换', icon: 'codec' },
  { id: 'crypto', label: '加密与认证', icon: 'lock' },
  { id: 'time', label: '时间与运维', icon: 'clock' },
  { id: 'text', label: '文本处理', icon: 'asterisk' },
  { id: 'design', label: '设计与颜色', icon: 'droplet' },
  { id: 'gen', label: '生成器', icon: 'grid' },
]

/**
 * 工具注册表。
 * 加一个工具 = 在 src/tools/<id>/index.ts 导出 default render + 这里加一条。
 * load 用静态字符串，Vite 才能把每个工具拆成独立 chunk 按需加载。
 */
export const TOOLS: ToolEntry[] = [
  {
    id: 'json-format',
    name: 'JSON 格式化',
    desc: '格式化 / 压缩 / 校验，语法高亮，精确定位报错行列',
    keywords: ['json', 'format', 'beautify', 'minify', 'validate', '格式化', '美化', '压缩', '校验'],
    category: 'data',
    icon: 'braces',
    load: () => import('../tools/json-format'),
  },
  {
    id: 'text-diff',
    name: '文本对比',
    desc: '逐行比较两段文本，并排标出新增与删除',
    keywords: ['diff', 'compare', 'text', '对比', '比较', '差异'],
    category: 'data',
    icon: 'diff',
    load: () => import('../tools/text-diff'),
  },
  {
    id: 'encode',
    name: '编解码转换',
    desc: 'Base64 / URL / HTML 实体 / Unicode / Hex 双向互转',
    keywords: ['base64', 'url', 'encode', 'decode', 'html', 'unicode', 'hex', '编码', '解码', '转义'],
    category: 'codec',
    icon: 'codec',
    load: () => import('../tools/encode'),
  },
  {
    id: 'jwt',
    name: 'JWT 解码',
    desc: '解析 Header / Payload，标注过期与生效时间',
    keywords: ['jwt', 'token', 'jose', 'bearer', '令牌', '解码'],
    category: 'crypto',
    icon: 'lock',
    load: () => import('../tools/jwt'),
  },
  {
    id: 'hash',
    name: '哈希计算',
    desc: 'MD5 / SHA-1 / SHA-256 / SHA-384 / SHA-512，支持文件',
    keywords: ['hash', 'md5', 'sha', 'sha256', 'checksum', '哈希', '摘要', '校验'],
    category: 'crypto',
    icon: 'hash',
    load: () => import('../tools/hash'),
  },
  {
    id: 'timestamp',
    name: '时间戳转换',
    desc: '秒 / 毫秒 / 日期互转，多时区并列，实时当前时间',
    keywords: ['timestamp', 'unix', 'date', 'time', 'timezone', '时间戳', '日期', '时区'],
    category: 'time',
    icon: 'clock',
    load: () => import('../tools/timestamp'),
  },
  {
    id: 'regex',
    name: '正则测试',
    desc: '实时匹配高亮、捕获组明细、替换预览、常用模板',
    keywords: ['regex', 'regexp', 'pattern', 'match', '正则', '匹配', '替换'],
    category: 'text',
    icon: 'asterisk',
    load: () => import('../tools/regex'),
  },
  {
    id: 'uuid',
    name: 'UUID / ID 生成',
    desc: 'UUID v4、NanoID、短 ID，批量生成并复制',
    keywords: ['uuid', 'guid', 'nanoid', 'id', 'random', '生成', '唯一'],
    category: 'gen',
    icon: 'card',
    load: () => import('../tools/uuid'),
  },
  {
    id: 'color',
    name: '颜色转换',
    desc: 'HEX / RGB / HSL 互转，明暗色阶，对比度检测',
    keywords: ['color', 'hex', 'rgb', 'hsl', 'palette', 'contrast', '颜色', '配色', '对比度'],
    category: 'design',
    icon: 'droplet',
    load: () => import('../tools/color'),
  },
  {
    id: 'qrcode',
    name: '二维码生成',
    desc: '文本 / 链接转二维码，可调尺寸与纠错级别，导出 PNG、SVG',
    keywords: ['qrcode', 'qr', 'barcode', '二维码', '扫码'],
    category: 'gen',
    icon: 'qr',
    load: () => import('../tools/qrcode'),
  },
]

export function findTool(id: string): ToolEntry | undefined {
  return TOOLS.find((t) => t.id === id)
}

export function countBy(category: string): number {
  return TOOLS.filter((t) => t.category === category).length
}
