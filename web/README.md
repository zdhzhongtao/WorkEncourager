# WadeToolbox Web · 前端开发者工具箱

打开一个网页就能用的开发者小工具集。**没有后端、没有账号、不联网、不上传任何数据**，所有计算都在浏览器里完成。

- 在线版：<https://zdhzhongtao.github.io/WorkEncourager/>
- 本地开发：`npm install` → `npm run dev`
- 生产构建：`npm run build`（产物在 `dist/`）
- 单文件版：`npm run build:standalone`
- 部署：推到 `main`，GitHub Actions 自动发布到 Pages

---

## 现在有什么（34 个工具）

| 分类 | 工具 | 说明 |
| --- | --- | --- |
| 数据与格式 | JSON 格式化 | 格式化 / 压缩 / 校验、语法高亮、报错定位到行列、键名排序 |
| 数据与格式 | 格式互转 | JSON ↔ YAML / TOML / XML / CSV / Markdown 表格 |
| 数据与格式 | JSON 转代码 | 生成 TypeScript / Go / Java / C# / Python 类型定义 |
| 数据与格式 | JSON 进阶 | JSONPath 查询、扁平化与还原、结构统计 |
| 数据与格式 | SQL 格式化 | 多方言、关键字大小写、一键压缩 |
| 数据与格式 | XML / HTML 格式化 | 缩进美化与压缩，script/style 原样保留 |
| 数据与格式 | Markdown 预览 | 实时渲染、导出 HTML、打印成 PDF |
| 数据与格式 | curl 转代码 | curl → fetch / axios / Python / Go / PowerShell |
| 数据与格式 | 文本对比 | 逐行 LCS 比对，可忽略大小写与首尾空白 |
| 编码转换 | 编解码转换 | Base64 / Base64URL / Base32 / Base58 / ASCII85 / URL / HTML / Unicode / Hex；**勾选式模式选择**（一行编码、一行解码，每项标出字符集特征，勾上即执行），粘进来自动识别是哪种（带匹配度和依据），每种模式都有说明、示例和「同一句话 10 种编码」对照表，支持文件 ↔ Base64 |
| 编码转换 | URL 解析 | 拆协议、端口、路径、逐参解码，参数转 JSON |
| 加密与认证 | JWT 工具 | 解析 Header / Payload，标注过期与生效时间 |
| 加密与认证 | JWT 验签与签发 | 本地验签（HS / RS），或用 HMAC 签发新令牌 |
| 加密与认证 | 哈希计算 | MD5 / SHA-1 / SHA-256 / SHA-384 / SHA-512，支持文件 |
| 加密与认证 | 对称加解密 | AES-GCM / CBC、PBKDF2 派生、HMAC 签名 |
| 加密与认证 | 国密算法 | SM2 加解密与签名、SM3 摘要、SM4 加解密 |
| 加密与认证 | 密码与强度 | 随机密码生成、熵与弱口令评估、Bcrypt 哈希校验 |
| 时间与运维 | 时间戳转换 | 秒 / 毫秒 / 日期互转、多时区并列、实时刷新 |
| 时间与运维 | Cron 解析 | 表达式解析 + 未来 10 次执行时间 + 常用模板 |
| 时间与运维 | 日期计算 | 日期加减、间隔统计、实时倒计时 |
| 时间与运维 | 运维速查 | HTTP 状态码 / MIME / 端口 / chmod 权限计算 |
| 时间与运维 | IP / 子网计算 | IPv4 子网划分、CIDR 换算、IPv6 ULA 生成 |
| 文本处理 | 正则测试 | 实时高亮、捕获组明细、替换预览、8 个模板 |
| 文本处理 | 文本统计 | 字数 / 行数 / 字节 / 词频，去重、排序、去空行、加行号 |
| 文本处理 | 命名风格转换 | camel / snake / kebab / Pascal / CONSTANT / slug |
| 文本处理 | 中文处理 | 繁简转换、拼音与首字母、全半角 |
| 文本处理 | 假数据生成 | 姓名 / 手机 / 邮箱 / 身份证（校验位正确）/ 地址 / 公司，导出 JSON、CSV、SQL |
| 设计与颜色 | 颜色转换 | HEX / RGB / HSL、明暗色阶、WCAG 对比度、Tailwind 11 级色阶 |
| 设计与颜色 | CSS 生成器 | 渐变、阴影、圆角、cubic-bezier 曲线可视调参 |
| 设计与颜色 | SVG 生成器 | 占位图、波浪分隔、网格背景 |
| 设计与颜色 | 图片处理 | 压缩、格式转换、缩放、水印、九宫格切图、点击取色 |
| 生成器 | UUID / ID 生成 | UUID v4、NanoID、ULID、Snowflake |
| 生成器 | 二维码生成 | 可调尺寸 / 纠错 / 配色，导出 PNG 与 SVG |
| 生成器 | 条码与扫码 | Code128 / EAN13 等条码生成，图片二维码识别 |

---

## 框架级能力（T0）

| 能力 | 怎么用 |
| --- | --- |
| 工具接力 | 输出面板「发送到…」把结果直送下游工具（JSON → 转代码 / SQL → 格式化…） |
| 深链 | `#/t/<工具id>?src=<base64url>`，链接发给同事，打开即填好内容；「分享链接」按钮一键生成 |
| 片段库 | 侧边栏「片段库」，长文本存 localStorage，可命名、复制、发到任意工具 |
| 全局拖拽 | 任意文件拖进窗口交给当前工具；纯文本文件自动填进输入框 |
| PWA | `http(s)` 下自动注册 Service Worker，可安装到桌面、断网可用 |
| 快捷键面板 | 按 `?` 打开；`Ctrl/⌘ + K` 聚焦搜索，`Ctrl/⌘ + Enter` 立即执行 |
| 单文件离线 | `npm run build:standalone`，双击 HTML 就能用 |

首页还有收藏、最近使用、按分类筛选。

---

## 目录结构

```
web/
├── index.html
├── vite.config.ts            base 用 './'，能部署到任意子路径
├── public/
│   ├── manifest.webmanifest  PWA 清单
│   └── sw.js                 Service Worker
├── src/
│   ├── main.ts               入口 + SW 注册
│   ├── app.ts                外壳：顶栏、侧边栏、搜索、路由、拖拽、片段库
│   ├── core/
│   │   ├── types.ts          ToolMeta / ToolCtx / ToolRender 约定
│   │   ├── registry.ts       工具注册表（加工具改这里）
│   │   ├── workbench.ts      双栏工作台骨架（大部分工具都基于它）
│   │   ├── transfer.ts       工具接力 + 深链编码
│   │   ├── files.ts          全局拖拽收发
│   │   ├── store.ts          片段库 / localStorage
│   │   ├── shortcuts.ts      快捷键面板
│   │   ├── dom.ts            el() 构造器、escapeHtml
│   │   ├── ui.ts             btn / seg / check / pane / toolbar
│   │   ├── kit.ts            复制、下载、Toast、防抖
│   │   ├── highlight.ts      JSON 着色 + 行列定位
│   │   ├── icons.ts          内联 SVG 图标
│   │   └── md5.ts            MD5（WebCrypto 没有，得自己带）
│   ├── styles/global.css     全部样式，不引 UI 框架
│   └── tools/<工具 id>/index.ts
```

---

## 加一个新工具

1. 建目录 `src/tools/<id>/index.ts`，默认导出一个渲染函数：

```ts
import { el } from '../../core/dom'
import { workbench } from '../../core/workbench'

export default function render(root: HTMLElement, ctx: { src: string | null }) {
  const wb = workbench(root, {
    toolId: 'my-tool',
    sample: '{"a":1}',
    initial: ctx.src ?? undefined,   // 深链传进来的内容
    produces: ['json'],              // 能被谁接走
    run(text, w) {
      w.setOut(text.toUpperCase())
    },
  })
  return () => {}   // 有定时器 / 事件时返回清理函数
}
```

2. 在 `src/core/registry.ts` 的 `TOOLS` 里加一条（`load` 必须是静态字符串，Vite 才会拆包）：

```ts
{
  id: 'my-tool',
  name: '我的工具',
  desc: '一句话说明，卡片上显示',
  keywords: ['关键词', '搜索用'],
  category: 'data',
  icon: 'braces',
  accepts: ['json'],        // 能接收什么，决定「发送到…」里是否出现
  produces: ['code'],       // 能产出什么
  load: () => import('../tools/my-tool'),
}
```

3. 完事。搜索、路由、收藏、最近使用、按需加载、深链、拖拽都是自动的。

样式可直接复用 `global.css` 里的：`.toolbar` `.btn` `.seg` `.inp` `.pane` `.panes` `.ta` `.out` `.msg` `.kv` `table.data` `.hint` `.card-box` `.tabs` `.chip` `.stat` `.grid-2` `.grid-3`。

---

## 单文件离线版（file:// 双击就能用）

```bash
npm run build:standalone      # 或 npm run build:all（普通构建 + 单文件）
```

产物两份，内容一样：

| 位置 | 用途 |
| --- | --- |
| `web/release/WadeToolbox-Web.html` | 一份 HTML，约 3.5 MB，JS/CSS 全内联，双击用浏览器打开即可 |
| `src/MdViewer/Assets/toolbox.html` | 同一个文件，作为嵌入资源编进 MdViewer 的 exe；工具栏点「工具箱」就释放到 `%LocalAppData%\MdViewer\web\` 并用默认浏览器打开 |

体积主要来自 `opencc-js` 的繁简词典（约 1.2 MB）和 `pinyin-pro` 的拼音表（约 600 KB），只有打开「中文处理」时才会解析。

为什么要单独打一份：`vite build` 输出的是 `<script type="module">`，而 `file://` 协议下浏览器会以 CORS 为由拒绝加载 ES module。所以单文件版改用 esbuild 打成 IIFE 再手工内联，`scripts/build-standalone.mjs` 干的就是这件事。

`file://` 下这些能力实测可用：`crypto.subtle`（哈希 / AES / JWT 验签）、`localStorage`（收藏、片段库）、`canvas`（图片、二维码、条码）。因为浏览器把 `file://` 当作**安全上下文**。Service Worker 在 `file://` 下不可用，所以 PWA 只在 http 部署时生效。

---

## 部署到 GitHub Pages

仓库已经带 `.github/workflows/web-pages.yml`：

1. 仓库 **Settings → Pages → Source** 选 **GitHub Actions**（只需要设一次）
2. 推送到 `main`，且改动命中 `web/**` 时自动构建发布
3. 地址是 `https://<用户名>.github.io/<仓库名>/`

注意：免费账号的 Pages 要求仓库是 **public**。

---

## 能力边界（别设计进去的）

纯静态网页做不了这些，需要浏览器扩展权限：

- 对**别的网页**截屏、取色、量栅格、看性能 —— 没有 `scripting` 权限
- 任意跨域接口调试（简易 Postman）—— 被 CORS 挡死
- 油猴脚本注入、抓包、改请求头
- PDF → Word / Excel 这种真转换（纯前端只能近似）
- DNS / Whois / IP 归属地 / 端口扫描 —— 要服务端代理发包
- 短链、云同步、账号体系 —— 要后端和存储

另外 GitHub Pages **不能自定义响应头**，所以依赖 `SharedArrayBuffer` 的能力（ffmpeg.wasm 多线程、部分 wasm）用不了。要上这类工具得换 Cloudflare Pages（支持 `_headers`）。

---

## 依赖

| 包 | 用途 | 体积 |
| --- | --- | --- |
| `qrcode` | 二维码编码 | ~25 KB gzip |
| `js-yaml` | YAML 解析 / 生成 | ~12 KB |
| `sql-formatter` | SQL 格式化多方言 | ~77 KB gzip |
| `marked` + `dompurify` | Markdown 渲染 + 清洗 | ~29 KB gzip |
| `bcryptjs` | Bcrypt 哈希校验 | ~12 KB gzip |
| `sm-crypto` | SM2 / SM3 / SM4 | ~18 KB gzip |
| `jsbarcode` + `jsqr` | 条码生成 + 二维码识别 | ~63 KB gzip |
| `opencc-js` + `pinyin-pro` | 繁简转换 + 拼音 | ~640 KB gzip |

除按需加载的工具外，首屏运行时约 13 KB gzip。

MIT
