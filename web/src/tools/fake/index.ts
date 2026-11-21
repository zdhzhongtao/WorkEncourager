import { el } from '../../core/dom'
import { copy, download } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'
import { sendMenu } from '../../core/transfer'

const SURNAMES = '赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华金魏陶姜'.split('')
const GIVEN = '伟芳娜敏静丽强磊军洋勇艳杰娟涛明超秀霞平刚桂英华文辉力浩宇轩雨欣怡子涵思远晨曦'.split('')
const CITIES = ['北京市', '上海市', '广州市', '深圳市', '杭州市', '成都市', '武汉市', '南京市', '西安市', '苏州市']
const DISTRICTS = ['朝阳区', '海淀区', '浦东新区', '天河区', '南山区', '西湖区', '武侯区', '鼓楼区', '雁塔区', '工业园区']
const STREETS = ['人民路', '中山路', '建设大道', '科技园路', '解放路', '文化街', '滨江路', '创业大道']
const COMPANIES = ['嘉士伯', '云启科技', '恒信数据', '远东物流', '海联电子', '新程软件', '博远咨询', '万象传媒']
const JOBS = ['前端工程师', '后端工程师', '产品经理', '测试工程师', '数据分析师', '运维工程师', '项目经理', '架构师']
const DOMAINS = ['example.com', 'test.local', 'corp.cn', 'mail.dev']

type Field = 'name' | 'phone' | 'email' | 'idcard' | 'address' | 'company' | 'job' | 'date' | 'uuid' | 'amount' | 'bool'

const FIELDS: Array<{ id: Field; label: string }> = [
  { id: 'name', label: '姓名' },
  { id: 'phone', label: '手机号' },
  { id: 'email', label: '邮箱' },
  { id: 'idcard', label: '身份证' },
  { id: 'address', label: '地址' },
  { id: 'company', label: '公司' },
  { id: 'job', label: '职位' },
  { id: 'date', label: '日期时间' },
  { id: 'uuid', label: 'UUID' },
  { id: 'amount', label: '金额' },
  { id: 'bool', label: '布尔值' },
]

const rnd = <T,>(arr: T[]): T => arr[Math.floor(Math.random() * arr.length)]
const digits = (n: number): string => Array.from({ length: n }, () => String(Math.floor(Math.random() * 10))).join('')

function uuid(): string {
  return crypto.randomUUID()
}

function idCard(): string {
  const area = rnd(['110101', '310101', '440106', '330106', '510107'])
  const y = 1960 + Math.floor(Math.random() * 45)
  const m = String(1 + Math.floor(Math.random() * 12)).padStart(2, '0')
  const d = String(1 + Math.floor(Math.random() * 28)).padStart(2, '0')
  const seq = digits(3)
  const body = `${area}${y}${m}${d}${seq}`
  const weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2]
  const codes = '10X98765432'.split('')
  let sum = 0
  for (let i = 0; i < 17; i++) sum += Number(body[i]) * weights[i]
  return body + codes[sum % 11]
}

function value(field: Field): string | number | boolean {
  switch (field) {
    case 'name':
      return rnd(SURNAMES) + rnd(GIVEN) + (Math.random() > 0.6 ? rnd(GIVEN) : '')
    case 'phone':
      return `1${rnd(['3', '5', '6', '7', '8', '9'])}${digits(9)}`
    case 'email':
      return `${Array.from({ length: 6 }, () => String.fromCharCode(97 + Math.floor(Math.random() * 26))).join('')}${digits(2)}@${rnd(DOMAINS)}`
    case 'idcard':
      return idCard()
    case 'address':
      return `${rnd(CITIES)}${rnd(DISTRICTS)}${rnd(STREETS)}${1 + Math.floor(Math.random() * 300)} 号`
    case 'company':
      return `${rnd(COMPANIES)}${rnd(['有限公司', '集团', '科技', '股份'])}`
    case 'job':
      return rnd(JOBS)
    case 'date':
      return new Date(Date.now() - Math.floor(Math.random() * 3 * 365 * 86400e3)).toISOString().slice(0, 19).replace('T', ' ')
    case 'uuid':
      return uuid()
    case 'amount':
      return Number((Math.random() * 99990 + 10).toFixed(2))
    case 'bool':
      return Math.random() > 0.5
  }
}

function toCsv(rows: Array<Record<string, unknown>>): string {
  const headers = Object.keys(rows[0] ?? {})
  const cell = (v: unknown) => {
    const s = String(v ?? '')
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
  }
  return [headers.join(','), ...rows.map((r) => headers.map((h) => cell(r[h])).join(','))].join('\n')
}

function toSql(rows: Array<Record<string, unknown>>, table: string): string {
  const headers = Object.keys(rows[0] ?? {})
  return rows
    .map((r) => {
      const values = headers.map((h) => {
        const v = r[h]
        if (typeof v === 'number' || typeof v === 'boolean') return String(Number(v))
        return `'${String(v ?? '').replace(/'/g, "''")}'`
      })
      return `INSERT INTO ${table} (${headers.join(', ')}) VALUES (${values.join(', ')});`
    })
    .join('\n')
}

export default function render(root: HTMLElement): void {
  let count = 10
  let format: 'json' | 'csv' | 'sql' = 'json'
  let table = 'demo_users'
  const selected = new Set<Field>(['name', 'phone', 'email'])

  const countInput = el('input', { class: 'inp w-sm', type: 'number', min: '1', max: '500', value: '10' })
  const formatSel = el('select', { class: 'inp w-md' })
  formatSel.append(
    el('option', { value: 'json', text: 'JSON 数组' }),
    el('option', { value: 'csv', text: 'CSV 表格' }),
    el('option', { value: 'sql', text: 'SQL INSERT' }),
  )
  const tableInput = el('input', { class: 'inp w-md', value: table, placeholder: '表名' })
  const chips = el('div', { class: 'chips' })
  const out = el('pre', { class: 'out', dataset: { ph: '点「生成」…' }, style: { minHeight: '260px' } })
  const stat = el('span', { text: '' })
  const msg = el('div', { class: 'msg' })
  let current = ''

  function paintChips(): void {
    chips.innerHTML = ''
    for (const f of FIELDS) {
      chips.append(
        el('button', {
          class: `chip${selected.has(f.id) ? ' on' : ''}`,
          text: f.label,
          onclick: () => {
            if (selected.has(f.id)) selected.delete(f.id)
            else selected.add(f.id)
            paintChips()
          },
        }),
      )
    }
  }

  function generate(): void {
    msg.className = 'msg'
    msg.textContent = ''
    count = Math.max(1, Math.min(500, Number(countInput.value) || 10))
    format = formatSel.value as 'json' | 'csv' | 'sql'
    table = tableInput.value.trim() || 'demo'
    if (!selected.size) {
      msg.className = 'msg err'
      msg.textContent = '至少勾一个字段'
      return
    }

    const fields = FIELDS.filter((f) => selected.has(f.id)).map((f) => f.id)
    const rows = Array.from({ length: count }, () => {
      const row: Record<string, unknown> = {}
      for (const f of fields) row[f] = value(f)
      return row
    })

    current = format === 'json' ? JSON.stringify(rows, null, 2) : format === 'csv' ? toCsv(rows) : toSql(rows, table)
    out.textContent = current
    stat.textContent = `${count} 条 · ${fields.length} 个字段 · ${new Blob([current!]).size} 字节`
  }

  countInput.addEventListener('change', generate)
  formatSel.addEventListener('change', generate)
  tableInput.addEventListener('change', generate)

  const bar = toolbar(
    el('span', { class: 'hint', text: '条数' }), countInput,
    el('span', { class: 'hint', text: '格式' }), formatSel,
    tableInput,
    el('span', { class: 'spacer' }),
    btn('生成', generate, 'primary'),
    btn('重新生成', generate, 'ghost'),
  )

  const outPane = pane('结果', out, {
    actions: [
      btn('复制', () => copy(current)),
      sendMenu(['json', 'csv', 'text'], () => current),
      btn('下载', () => {
        if (!current) return
        const ext = format === 'json' ? 'json' : format === 'csv' ? 'csv' : 'sql'
        download(`fake-data.${ext}`, current, format === 'json' ? 'application/json' : 'text/plain;charset=utf-8')
      }, 'ghost'),
    ],
    foot: foot(stat),
  })

  root.append(msg, bar, el('div', { class: 'card-box', style: { marginBottom: '12px' } }, chips), outPane)

  paintChips()
  generate()
}
