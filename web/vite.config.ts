import { defineConfig } from 'vite'

// base 用相对路径：既能部署到 GitHub Pages 的 /<repo>/ 子路径，
// 也能直接丢到任意静态目录甚至内网共享盘里打开。
export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    target: 'es2022',
    assetsInlineLimit: 4096,
    chunkSizeWarningLimit: 1600,
    // 不加 manualChunks：让 Rollup 跟着动态 import 自动拆，
    // 否则打开任何一个工具都会被迫下载整个 vendor（opencc 字典就 1MB+）。
  },
})
