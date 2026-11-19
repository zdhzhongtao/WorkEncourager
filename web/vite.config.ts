import { defineConfig } from 'vite'

// base 用相对路径：既能部署到 GitHub Pages 的 /<repo>/ 子路径，
// 也能直接丢到任意静态目录甚至内网共享盘里打开。
export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    target: 'es2022',
    assetsInlineLimit: 4096,
    chunkSizeWarningLimit: 900,
    rollupOptions: {
      output: {
        // 工具都是动态 import，这里只把公共运行时代码单独拆出来
        manualChunks(id) {
          if (id.includes('node_modules')) return 'vendor'
        },
      },
    },
  },
})
