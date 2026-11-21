import { el } from '../../core/dom'
import { workbench } from '../../core/workbench'

type Target = 'fetch' | 'axios' | 'python' | 'go' | 'powershell'

const TARGETS: Array<{ value: Target; label: string }> = [
  { value: 'fetch', label: 'fetch' },
  { value: 'axios', label: 'axios' },
  { value: 'python', label: 'Python requests' },
  { value: 'go', label: 'Go net/http' },
  { value: 'powershell', label: 'PowerShell' },
]

interface Parsed {
  method: string
  url: string
  headers: Array<[string, string]>
  body: string | null
  form: Array<[string, string]>
  user: string | null
}

function shellSplit(input: string): string[] {
  const out: string[] = []
  let cur = ''
  let quote: string | null = null
  let started = false

  for (let i = 0; i < input.length; i++) {
    const ch = input[i]
    if (quote) {
      if (ch === quote) quote = null
      else if (ch === '\\' && quote === '"' && i + 1 < input.length) {
        cur += input[++i]
      } else cur += ch
      continue
    }
    if (ch === "'" || ch === '"') {
      quote = ch
      started = true
      continue
    }
    if (/\s/.test(ch)) {
      if (cur || started) {
        out.push(cur)
        cur = ''
        started = false
      }
      continue
    }
    if (ch === '\\' && i + 1 < input.length) {
      cur += input[++i]
      continue
    }
    cur += ch
  }
  if (cur || started) out.push(cur)
  return out
}

function parseCurl(command: string): Parsed {
  const tokens = shellSplit(command.replace(/\\\r?\n/g, ' '))
  if (!tokens.length) throw new Error('粘贴一条 curl 命令')
  if (tokens[0] !== 'curl') throw new Error('命令要以 curl 开头')

  const parsed: Parsed = { method: '', url: '', headers: [], body: null, form: [], user: null }
  const dataParts: string[] = []

  for (let i = 1; i < tokens.length; i++) {
    const token = tokens[i]
    const next = (): string => {
      const v = tokens[++i]
      if (v === undefined) throw new Error(`${token} 后面缺参数`)
      return v
    }

    switch (token) {
      case '-X':
      case '--request':
        parsed.method = next().toUpperCase()
        break
      case '-H':
      case '--header': {
        const raw = next()
        const idx = raw.indexOf(':')
        if (idx > 0) parsed.headers.push([raw.slice(0, idx).trim(), raw.slice(idx + 1).trim()])
        break
      }
      case '-d':
      case '--data':
      case '--data-raw':
      case '--data-binary':
      case '--data-ascii':
        dataParts.push(next())
        break
      case '--data-urlencode':
        dataParts.push(next())
        break
      case '-F':
      case '--form': {
        const raw = next()
        const idx = raw.indexOf('=')
        if (idx > 0) parsed.form.push([raw.slice(0, idx), raw.slice(idx + 1)])
        break
      }
      case '-u':
      case '--user':
        parsed.user = next()
        break
      case '--url':
        parsed.url = next()
        break
      case '-o':
      case '--output':
      case '-w':
      case '--write-out':
      case '-A':
      case '--user-agent':
      case '-e':
      case '--referer':
      case '-b':
      case '--cookie':
      case '-c':
      case '--cookie-jar':
      case '--connect-timeout':
      case '-m':
      case '--max-time':
      case '-x':
      case '--proxy':
        next()
        break
      default:
        if (!token.startsWith('-')) parsed.url = token
    }
  }

  if (dataParts.length) parsed.body = dataParts.join('&')
  if (!parsed.method) parsed.method = parsed.body !== null || parsed.form.length ? 'POST' : 'GET'
  if (!parsed.url) throw new Error('没找到 URL')

  if (parsed.body !== null && !parsed.headers.some(([k]) => k.toLowerCase() === 'content-type')) {
    const looksJson = /^\s*[{[]/.test(parsed.body)
    parsed.headers.push(['Content-Type', looksJson ? 'application/json' : 'application/x-www-form-urlencoded'])
  }
  return parsed
}

const q = (s: string) => JSON.stringify(s)
const py = (s: string) => JSON.stringify(s)
const goStr = (s: string) => JSON.stringify(s)

function toFetch(p: Parsed): string {
  const lines: string[] = ['const res = await fetch(' + q(p.url) + ', {', `  method: ${q(p.method)},`]
  if (p.headers.length) {
    lines.push('  headers: {')
    for (const [k, v] of p.headers) lines.push(`    ${q(k)}: ${q(v)},`)
    lines.push('  },')
  }
  if (p.body !== null) lines.push(`  body: ${q(p.body)},`)
  lines.push('})', '', 'const data = await res.json()', 'console.log(data)')
  return lines.join('\n')
}

function toAxios(p: Parsed): string {
  const lines: string[] = [`import axios from 'axios'`, '', 'const { data } = await axios({', `  url: ${q(p.url)},`, `  method: ${q(p.method.toLowerCase())},`]
  if (p.headers.length) {
    lines.push('  headers: {')
    for (const [k, v] of p.headers) lines.push(`    ${q(k)}: ${q(v)},`)
    lines.push('  },')
  }
  if (p.body !== null) lines.push(`  data: ${q(p.body)},`)
  lines.push('})', '', 'console.log(data)')
  return lines.join('\n')
}

function toPython(p: Parsed): string {
  const lines: string[] = ['import requests', '', `url = ${py(p.url)}`]
  if (p.headers.length) {
    lines.push('headers = {')
    for (const [k, v] of p.headers) lines.push(`    ${py(k)}: ${py(v)},`)
    lines.push('}')
  } else lines.push('headers = {}')
  if (p.body !== null) lines.push('', `body = ${py(p.body)}`)
  lines.push('')
  const bodyArg = p.body !== null ? ', data=body' : ''
  lines.push(`resp = requests.request(${py(p.method)}, url, headers=headers${bodyArg}, timeout=30)`)
  lines.push('print(resp.status_code, resp.text)')
  return lines.join('\n')
}

function toGo(p: Parsed): string {
  const lines: string[] = [
    'package main',
    '',
    'import (',
    '\t"fmt"',
    '\t"io"',
    '\t"net/http"',
    '\t"strings"',
    ')',
    '',
    'func main() {',
  ]
  if (p.body !== null) lines.push(`\tbody := strings.NewReader(${goStr(p.body)})`)
  else lines.push('\tvar body io.Reader')
  lines.push(`\treq, err := http.NewRequest(${goStr(p.method)}, ${goStr(p.url)}, body)`, '\tif err != nil {', '\t\tpanic(err)', '\t}')
  for (const [k, v] of p.headers) lines.push(`\treq.Header.Set(${goStr(k)}, ${goStr(v)})`)
  lines.push(
    '',
    '\tresp, err := http.DefaultClient.Do(req)',
    '\tif err != nil {',
    '\t\tpanic(err)',
    '\t}',
    '\tdefer resp.Body.Close()',
    '\tout, _ := io.ReadAll(resp.Body)',
    '\tfmt.Println(resp.Status)',
    '\tfmt.Println(string(out))',
    '}',
  )
  return lines.join('\n')
}

function toPowerShell(p: Parsed): string {
  const lines: string[] = []
  if (p.headers.length) {
    lines.push('$headers = @{')
    for (const [k, v] of p.headers) lines.push(`  ${q(k)} = ${q(v)}`)
    lines.push('}', '')
  }
  const parts = [
    `-Uri ${q(p.url)}`,
    `-Method ${p.method}`,
    p.headers.length ? '-Headers $headers' : '',
    p.body !== null ? `-Body ${q(p.body)}` : '',
  ].filter(Boolean)
  lines.push(`$resp = Invoke-RestMethod ${parts.join(' `\n  ')}`)
  lines.push('$resp | ConvertTo-Json -Depth 8')
  return lines.join('\n')
}

const GEN: Record<Target, (p: Parsed) => string> = {
  fetch: toFetch,
  axios: toAxios,
  python: toPython,
  go: toGo,
  powershell: toPowerShell,
}

const SAMPLE = `curl -X POST 'https://api.example.com/v1/orders' \\
  -H 'Content-Type: application/json' \\
  -H 'Authorization: Bearer eyJhbGciOi...' \\
  -d '{"sku":"A-1001","qty":2}'`

export default function render(root: HTMLElement, ctx: { src: string | null }): void {
  let target: Target = 'fetch'
  const chips = el('div', { class: 'chips' })
  let wb: ReturnType<typeof workbench>

  function paint(): void {
    chips.innerHTML = ''
    for (const t of TARGETS) {
      chips.append(
        el('button', {
          class: `chip${target === t.value ? ' on' : ''}`,
          text: t.label,
          onclick: () => {
            target = t.value
            paint()
            wb.run()
          },
        }),
      )
    }
  }

  wb = workbench(root, {
    toolId: 'curl',
    placeholder: '粘贴 curl 命令（支持多行，行尾反斜杠也行）…',
    outPh: '生成的代码…',
    sample: SAMPLE,
    initial: ctx.src ?? undefined,
    produces: ['code'],
    downloadName: 'request.txt',
    run(text, w) {
      if (!text.trim()) {
        w.setOut('')
        return
      }
      const parsed = parseCurl(text)
      w.setOut(GEN[target](parsed))
      const extra = parsed.user ? ` · Basic Auth ${parsed.user.split(':')[0]}` : ''
      w.ok(`${parsed.method} ${new URL(parsed.url).host}${extra}`)
    },
  })

  paint()
  root.insertBefore(el('div', { class: 'card-box row', style: { marginBottom: '12px' } }, el('span', { class: 'hint', text: '目标语言' }), chips), root.firstChild)
}
