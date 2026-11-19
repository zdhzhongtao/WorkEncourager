# WadeToolbox Web · 前端开发者工具箱

打开一个网页就能用的开发者小工具集。**没有后端、没有账号、不联网、不上传任何数据**，所有计算都在浏览器里完成。

- 本地开发：`npm install` → `npm run dev`
- 生产构建：`npm run build`（产物在 `dist/`）
- 部署：推到 `main`，GitHub Actions 自动发布到 Pages

---

## 现在有什么

| 分类 | 工具 | 说明 |
| --- | --- | --- |
| 数据与格式 | JSON 格式化 | 格式化 / 压缩 / 校验、语法高亮、报错定位到行列、键名排序 |
| 数据与格式 | 文本对比 | 逐行 LCS 比对，标出新增与删除，可忽略大小写与首尾空白 |
| 编码转换 | 编解码转换 | Base64 / Base64URL / URL / HTML 实体 / Unicode / Hex 双向互转 |
| 加密与认证 | JWT 解码 | 解析 Header / Payload，判断过期与生效，标注时间类声明 |
| 加密与认证 | 哈希计算 | MD5 / SHA-1 / SHA-256 / SHA-384 / SHA-512，支持文本与文件 |
| 时间与运维 | 时间戳转换 | 秒 / 毫秒 / 日期互转、多时区并列、当前时间实时刷新、日期反查 |
| 文本处理 | 正则测试 | 实时高亮、捕获组明细、替换预览、8 个常用模板 |
| 生成器 | UUID / ID 生成 | UUID v4、NanoID（可调长度与字母表），批量最多 200 个 |
| 设计与颜色 | 颜色转换 | HEX / RGB / HSL 互转、11 级明暗色阶、WCAG 对比度判定 |
| 生成器 | 二维码生成 | 可调尺寸 / 纠错级别 / 配色，导出 PNG 与 SVG |

首页支持 `Ctrl / ⌘ + K` 聚焦搜索，卡片可收藏，最近使用记录在本机。

---

## 目录结构

```
web/
├── index.html
├── vite.config.ts            base 用 './'，能部署到任意子路径
├── tsconfig.json
├── src/
│   ├── main.ts               入口
│   ├── app.ts                外壳：顶栏、侧边栏、搜索、路由、卡片网格
│   ├── core/
│   │   ├── types.ts          ToolMeta / ToolEntry 约定
│   │   ├── registry.ts       工具注册表（加工具改这里）
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

export default function render(root: HTMLElement) {
  const box = el('div', { class: 'card-box', text: 'Hello' })
  root.append(box)

  // 需要清定时器 / 解绑事件时返回清理函数，切走页面会自动调用
  return () => {}
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
  load: () => import('../tools/my-tool'),
}
```

3. 完事。搜索、路由、收藏、最近使用、按需加载都是自动的。

样式可直接复用 `global.css` 里的：`.toolbar` `.btn` `.seg` `.inp` `.pane` `.panes` `.ta` `.out` `.msg` `.kv` `table.data` `.hint` `.card-box`。

---

## 单文件离线版（file:// 双击就能用）

```bash
npm run build:standalone      # 或 npm run build:all（普通构建 + 单文件）
```

产物两份，内容一样：

| 位置 | 用途 |
| --- | --- |
| `web/release/WadeToolbox-Web.html` | 一份 HTML，约 92 KB，JS/CSS 全内联，双击用浏览器打开即可，不需要服务器 |
| `src/MdViewer/Assets/toolbox.html` | 同一个文件，作为嵌入资源编进 MdViewer 的 exe；阅读器工具栏点「工具箱」就释放到 `%LocalAppData%\MdViewer\web\` 并用默认浏览器打开 |

为什么要单独打一份：`vite build` 输出的是 `<script type="module">`，而 `file://` 协议下浏览器会以 CORS 为由拒绝加载 ES module。所以单文件版改用 esbuild 打成 IIFE 再手工内联，`scripts/build-standalone.mjs` 干的就是这件事。

`file://` 下这些能力实测可用：`crypto.subtle`（哈希 / AES）、`localStorage`（收藏、最近使用）、`canvas`（二维码导出）。因为浏览器把 `file://` 当作**安全上下文**。

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

另外 GitHub Pages **不能自定义响应头**，所以依赖 `SharedArrayBuffer` 的能力（ffmpeg.wasm 多线程、部分 wasm）用不了。要上这类工具得换 Cloudflare Pages（支持 `_headers`）。

---

## 依赖

只有 `qrcode` 一个运行时依赖（二维码编码，约 25 KB gzip）。其余全部手写，首屏 gzip 约 20 KB。

MIT
