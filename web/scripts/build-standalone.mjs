/**
 * 把整个工具箱打成一个 HTML 文件（JS / CSS 全部内联，classic script）。
 *
 * 为什么不用 vite build 的产物直接改：
 *   vite 输出的是 <script type="module">，而 file:// 协议下浏览器会因为
 *   CORS 拒绝加载 ES module。所以这里用 esbuild 打成 IIFE，再手工内联。
 *   最终产物双击就能在浏览器里跑，不需要任何本地服务器。
 *
 * 产物：
 *   web/release/WadeToolbox-Web.html      —— 给人下载 / 丢桌面
 *   ../src/MdViewer/Assets/toolbox.html   —— 嵌进 MdViewer 的 exe 里，工具栏按钮释放出来打开
 */
import { build } from 'esbuild'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const webRoot = path.resolve(here, '..')
const repoRoot = path.resolve(webRoot, '..')

const targets = [
  path.join(webRoot, 'release', 'WadeToolbox-Web.html'),
  path.join(repoRoot, 'src', 'MdViewer', 'Assets', 'toolbox.html'),
]

const result = await build({
  absWorkingDir: webRoot,
  entryPoints: ['src/main.ts'],
  bundle: true,
  write: false,
  format: 'iife',
  platform: 'browser',
  target: ['chrome100'],
  minify: true,
  legalComments: 'none',
  logLevel: 'warning',
  // 动态 import 全部并进同一个文件（离线单文件不允许再拆 chunk）
  splitting: false,
  outdir: 'release',
  loader: { '.woff2': 'dataurl', '.woff': 'dataurl', '.png': 'dataurl', '.svg': 'dataurl' },
})

const js = result.outputFiles.find((f) => f.path.endsWith('.js'))
const css = result.outputFiles.find((f) => f.path.endsWith('.css'))
if (!js) throw new Error('esbuild 没有产出 JS')

let html = fs.readFileSync(path.join(webRoot, 'index.html'), 'utf8')

// 样式塞进 head，脚本塞回原来 module 标签的位置（body 末尾）
// 注意：替换串里必须用函数形式，否则压缩后的 JS 里的 $& / $' 会被当成替换占位符
if (css) {
  const cssBlock = `    <style>\n${css.text.trim()}\n    </style>\n  </head>`
  html = html.replace('</head>', () => cssBlock)
}
const jsBlock = `<script>\n${js.text.trim()}\n    </script>`
html = html.replace('<script type="module" src="/src/main.ts"></script>', () => jsBlock)

if (html.includes('/src/main.ts')) throw new Error('入口脚本没被替换，检查 index.html')
if (!html.includes('<style>')) throw new Error('CSS 没内联进去')

html = html.replace(
  '<meta name="theme-color"',
  '<meta name="generator" content="WadeToolbox standalone" />\n    <meta name="theme-color"',
)

for (const out of targets) {
  fs.mkdirSync(path.dirname(out), { recursive: true })
  fs.writeFileSync(out, html, 'utf8')
  const kb = (Buffer.byteLength(html, 'utf8') / 1024).toFixed(0)
  console.log(`OK ${path.relative(repoRoot, out).replace(/\\/g, '/')}  ${kb} KB`)
}
