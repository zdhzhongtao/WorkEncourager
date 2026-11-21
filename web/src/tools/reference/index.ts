import { el } from '../../core/dom'
import { copy } from '../../core/kit'
import { btn } from '../../core/ui'

type Tab = 'http' | 'mime' | 'port' | 'chmod'

const HTTP: Array<[string, string, string]> = [
  ['100', 'Continue', '收到请求头，客户端可以继续发 body'],
  ['101', 'Switching Protocols', '协议升级，WebSocket 握手成功'],
  ['200', 'OK', '最常见的成功'],
  ['201', 'Created', '资源创建成功，通常回 Location'],
  ['202', 'Accepted', '已接收，异步处理中'],
  ['204', 'No Content', '成功但没有响应体，DELETE 常用'],
  ['206', 'Partial Content', '断点续传 / Range 请求'],
  ['301', 'Moved Permanently', '永久重定向，搜索引擎会记'],
  ['302', 'Found', '临时重定向，方法可能被改成 GET'],
  ['303', 'See Other', '重定向后强制用 GET'],
  ['304', 'Not Modified', '命中协商缓存，走本地副本'],
  ['307', 'Temporary Redirect', '临时重定向且保持原方法'],
  ['308', 'Permanent Redirect', '永久重定向且保持原方法'],
  ['400', 'Bad Request', '参数格式不对'],
  ['401', 'Unauthorized', '没登录 / Token 无效'],
  ['403', 'Forbidden', '登录了但没权限'],
  ['404', 'Not Found', '路径不存在（也可能是不想告诉你存在）'],
  ['405', 'Method Not Allowed', '方法不对，看 Allow 头'],
  ['408', 'Request Timeout', '服务端等请求超时'],
  ['409', 'Conflict', '状态冲突，比如重复创建'],
  ['410', 'Gone', '资源永久下线'],
  ['413', 'Payload Too Large', '请求体太大，调大上传限制'],
  ['415', 'Unsupported Media Type', 'Content-Type 不对'],
  ['422', 'Unprocessable Entity', '格式对但语义错，校验失败常用'],
  ['429', 'Too Many Requests', '限流，看 Retry-After'],
  ['499', 'Client Closed Request', 'Nginx 私有：客户端提前断开'],
  ['500', 'Internal Server Error', '服务端未捕获异常'],
  ['501', 'Not Implemented', '服务端不支持这个功能'],
  ['502', 'Bad Gateway', '网关拿到上游的无效响应'],
  ['503', 'Service Unavailable', '服务不可用 / 过载'],
  ['504', 'Gateway Timeout', '网关等上游超时'],
]

const MIME: Array<[string, string]> = [
  ['application/json', 'JSON 数据'],
  ['application/xml', 'XML 数据'],
  ['application/x-www-form-urlencoded', '表单提交（键值对）'],
  ['multipart/form-data', '带文件上传的表单'],
  ['text/plain', '纯文本'],
  ['text/html', 'HTML 页面'],
  ['text/css', '样式表'],
  ['text/javascript', '脚本（旧写法）'],
  ['text/csv', 'CSV 表格'],
  ['text/markdown', 'Markdown'],
  ['application/javascript', 'JavaScript'],
  ['application/pdf', 'PDF'],
  ['application/zip', 'ZIP 压缩包'],
  ['application/gzip', 'gzip 压缩'],
  ['application/octet-stream', '任意二进制流（下载默认）'],
  ['application/vnd.ms-excel', 'Excel（xls）'],
  ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'Excel（xlsx）'],
  ['application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'Word（docx）'],
  ['application/vnd.openxmlformats-officedocument.presentationml.presentation', 'PowerPoint（pptx）'],
  ['image/png', 'PNG 图片'],
  ['image/jpeg', 'JPEG 图片'],
  ['image/gif', 'GIF 图片'],
  ['image/webp', 'WebP 图片'],
  ['image/svg+xml', 'SVG 矢量图'],
  ['image/avif', 'AVIF 图片'],
  ['image/x-icon', 'ICO 图标'],
  ['audio/mpeg', 'MP3 音频'],
  ['audio/wav', 'WAV 音频'],
  ['video/mp4', 'MP4 视频'],
  ['video/webm', 'WebM 视频'],
  ['font/woff2', 'WOFF2 字体'],
  ['application/wasm', 'WebAssembly'],
]

const PORTS: Array<[number, string, string]> = [
  [20, 'FTP-DATA', 'FTP 数据传输'],
  [21, 'FTP', 'FTP 控制'],
  [22, 'SSH', '远程登录、Git over SSH'],
  [23, 'Telnet', '明文远程登录，别用'],
  [25, 'SMTP', '发邮件'],
  [53, 'DNS', '域名解析'],
  [67, 'DHCP', 'DHCP 服务端'],
  [80, 'HTTP', '明文 Web'],
  [110, 'POP3', '收邮件'],
  [143, 'IMAP', '收邮件（保留服务端）'],
  [161, 'SNMP', '网络设备监控'],
  [389, 'LDAP', '目录服务'],
  [443, 'HTTPS', '加密 Web'],
  [445, 'SMB', 'Windows 文件共享'],
  [465, 'SMTPS', '加密发邮件'],
  [587, 'SMTP Submission', '邮件提交（STARTTLS）'],
  [636, 'LDAPS', '加密目录服务'],
  [993, 'IMAPS', '加密 IMAP'],
  [995, 'POP3S', '加密 POP3'],
  [1433, 'MSSQL', 'SQL Server'],
  [1521, 'Oracle', 'Oracle 数据库'],
  [3000, 'Dev', '前端 / Node 开发常用'],
  [3306, 'MySQL', 'MySQL 数据库'],
  [3389, 'RDP', 'Windows 远程桌面'],
  [5000, 'Dev', 'Flask / .NET 开发常用'],
  [5432, 'PostgreSQL', 'PostgreSQL 数据库'],
  [5672, 'AMQP', 'RabbitMQ'],
  [6379, 'Redis', 'Redis 缓存'],
  [8080, 'HTTP Alt', '备用 Web / 代理'],
  [8443, 'HTTPS Alt', '备用加密 Web'],
  [9000, 'PHP-FPM / MinIO', '常见应用端口'],
  [9092, 'Kafka', 'Kafka broker'],
  [9200, 'Elasticsearch', 'ES HTTP'],
  [11211, 'Memcached', '内存缓存'],
  [27017, 'MongoDB', 'MongoDB 数据库'],
]

const PERM_ITEMS: Array<{ bit: number; who: string; perm: string; label: string }> = [
  { bit: 0o400, who: '所有者', perm: 'r', label: '读' },
  { bit: 0o200, who: '所有者', perm: 'w', label: '写' },
  { bit: 0o100, who: '所有者', perm: 'x', label: '执行' },
  { bit: 0o040, who: '组', perm: 'r', label: '读' },
  { bit: 0o020, who: '组', perm: 'w', label: '写' },
  { bit: 0o010, who: '组', perm: 'x', label: '执行' },
  { bit: 0o004, who: '其他人', perm: 'r', label: '读' },
  { bit: 0o002, who: '其他人', perm: 'w', label: '写' },
  { bit: 0o001, who: '其他人', perm: 'x', label: '执行' },
]

function chmodText(mode: number): string {
  const sym = PERM_ITEMS.map((p) => ((mode & p.bit) !== 0 ? p.perm : '-')).join('')
  const oct = (mode & 0o777).toString(8).padStart(3, '0')
  return `${oct}  ${sym}`
}

export default function render(root: HTMLElement): void {
  let tab: Tab = 'http'
  const tabs = el('div', { class: 'tabs' })
  const search = el('input', { class: 'inp', placeholder: '过滤…', style: { flex: '1', maxWidth: '360px' } })
  const host = el('div')
  const msg = el('div', { class: 'msg' })

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'http', label: 'HTTP 状态码' },
    { id: 'mime', label: 'MIME 类型' },
    { id: 'port', label: '常用端口' },
    { id: 'chmod', label: 'chmod 权限' },
  ]

  function table(rows: Array<[string, string, string]>): HTMLElement {
    const t = el('table', { class: 'data' })
    t.append(el('thead', null, el('tr', null, el('th', { text: '值' }), el('th', { text: '说明' }), el('th', { text: '' }))))
    t.append(
      el('tbody', null, ...rows.map(([a, b, c]) =>
        el('tr', null,
          el('td', { class: 'k mono-sm', text: a }),
          el('td', { text: c ? `${b} —— ${c}` : b }),
          el('td', null, btn('复制', () => copy(a), 'ghost')),
        ),
      )),
    )
    return el('div', { class: 'table-wrap' }, t)
  }

  function chmodView(): void {
    host.innerHTML = ''
    let mode = 0o644
    const out = el('pre', { class: 'out', style: { minHeight: 'auto', padding: '10px 12px' } })
    const grid = el('div', { class: 'grid-3' })

    const paint = (): void => {
      out.textContent = chmodText(mode)
      grid.querySelectorAll('input[type=checkbox]').forEach((node) => {
        const input = node as HTMLInputElement
        input.checked = (mode & Number(input.dataset.bit)) !== 0
      })
    }

    for (const who of ['所有者', '组', '其他人']) {
      const box = el('div', { class: 'card-box' }, el('div', { class: 'hint', style: { marginBottom: '6px' } }, who))
      for (const item of PERM_ITEMS.filter((p) => p.who === who)) {
        const checkbox = el('input', {
          type: 'checkbox',
          dataset: { bit: String(item.bit) },
          onchange: (e: Event) => {
            const on = (e.target as HTMLInputElement).checked
            mode = on ? mode | item.bit : mode & ~item.bit
            paint()
          },
        })
        checkbox.checked = (mode & item.bit) !== 0
        box.append(el('label', { class: 'check', style: { display: 'flex', marginBottom: '4px' } }, checkbox, `${item.perm}  ${item.label}`))
      }
      grid.append(box)
    }

    const octInput = el('input', { class: 'inp w-sm mono', placeholder: '755' })
    octInput.addEventListener('input', () => {
      const value = octInput.value.replace(/[^0-7]/g, '')
      if (!value) return
      mode = parseInt(value, 8) & 0o777
      paint()
    })

    host.append(
      el('div', { class: 'card-box stack', style: { marginBottom: '12px' } },
        el('div', { class: 'row' }, el('span', { class: 'hint', text: '直接填八进制' }), octInput,
          btn('复制', () => copy(chmodText(mode).split('  ')[0]))),
        out,
      ),
      grid,
      el('div', { class: 'hint', style: { marginTop: '10px' } }, '常用：644 文件（所有者读写、其他人只读）、755 可执行 / 目录、600 私钥、777 谁都别用。'),
    )
    paint()
  }

  function paint(): void {
    tabs.innerHTML = ''
    for (const t of TABS) {
      tabs.append(
        el('button', {
          class: `tab${tab === t.id ? ' on' : ''}`,
          text: t.label,
          onclick: () => {
            tab = t.id
            paint()
            paintBody()
          },
        }),
      )
    }
    search.style.display = tab === 'chmod' ? 'none' : ''
  }

  function paintBody(): void {
    host.innerHTML = ''
    const q = search.value.trim().toLowerCase()
    const match = (s: string) => !q || s.toLowerCase().includes(q)

    if (tab === 'http') {
      host.append(table(HTTP.filter((r) => match(r.join(' ')))))
    } else if (tab === 'mime') {
      host.append(table(MIME.filter((r) => match(r.join(' '))).map(([a, b]) => [a, b, ''] as [string, string, string])))
    } else if (tab === 'port') {
      host.append(table(PORTS.filter((r) => match(r.join(' '))).map(([a, b, c]) => [String(a), `${b} —— ${c}`, ''] as [string, string, string])))
    } else {
      chmodView()
    }
  }

  search.addEventListener('input', paintBody)

  root.append(msg, tabs, el('div', { class: 'row', style: { marginBottom: '10px' } }, search), host)
  paint()
  paintBody()
}
