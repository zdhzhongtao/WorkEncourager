import { compareSync, hashSync } from 'bcryptjs'
import { el } from '../../core/dom'
import { copy, toast } from '../../core/kit'
import { btn, check, foot, pane } from '../../core/ui'

const LOWER = 'abcdefghijklmnopqrstuvwxyz'
const UPPER = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
const DIGITS = '0123456789'
const SYMBOLS = '!@#$%^&*()-_=+[]{};:,.?/'
const AMBIGUOUS = /[Il1O0o]/g

type Tab = 'gen' | 'strength' | 'bcrypt'

function pick(pool: string, count: number): string[] {
  const out: string[] = []
  const max = Math.floor(256 / pool.length) * pool.length
  while (out.length < count) {
    const bytes = crypto.getRandomValues(new Uint8Array(count * 2))
    for (const b of bytes) {
      if (b >= max) continue
      out.push(pool[b % pool.length])
      if (out.length === count) break
    }
  }
  return out
}

function shuffle(list: string[]): string[] {
  const arr = [...list]
  for (let i = arr.length - 1; i > 0; i--) {
    const j = crypto.getRandomValues(new Uint32Array(1))[0] % (i + 1)
    ;[arr[i], arr[j]] = [arr[j], arr[i]]
  }
  return arr
}

function entropyBits(pool: number, length: number): number {
  return pool > 1 ? Math.round(length * Math.log2(pool)) : 0
}

export default function render(root: HTMLElement): void {
  let tab: Tab = 'gen'
  const msg = el('div', { class: 'msg' })
  const tabs = el('div', { class: 'tabs' })
  const body = el('div')

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'gen', label: '随机密码' },
    { id: 'strength', label: '强度评估' },
    { id: 'bcrypt', label: 'Bcrypt' },
  ]

  /* ---------------- 生成器 ---------------- */

  function genView(): void {
    body.innerHTML = ''
    let length = 20
    let count = 5
    let useLower = true
    let useUpper = true
    let useDigits = true
    let useSymbols = true
    let excludeAmbiguous = false

    const lenInput = el('input', { class: 'inp w-sm', type: 'number', min: '4', max: '128', value: String(length) })
    const countInput = el('input', { class: 'inp w-sm', type: 'number', min: '1', max: '50', value: String(count) })
    const out = el('pre', { class: 'out', dataset: { ph: '点「生成」…' }, style: { minHeight: '180px' } })
    const stat = el('span', { text: '' })
    let list: string[] = []

    const pool = (): string => {
      let p = ''
      if (useLower) p += LOWER
      if (useUpper) p += UPPER
      if (useDigits) p += DIGITS
      if (useSymbols) p += SYMBOLS
      if (excludeAmbiguous) p = p.replace(AMBIGUOUS, '')
      return p
    }

    const run = (): void => {
      msg.className = 'msg'
      msg.textContent = ''
      length = Math.max(4, Math.min(128, Number(lenInput.value) || 20))
      count = Math.max(1, Math.min(50, Number(countInput.value) || 5))
      const chars = pool()
      if (!chars) {
        msg.className = 'msg err'
        msg.textContent = '字符集至少要选一类'
        return
      }
      list = Array.from({ length: count }, () => shuffle(pick(chars, length)).join(''))
      out.textContent = list.join('\n')
      const bits = entropyBits(chars.length, length)
      stat.textContent = `${chars.length} 个字符可选 · 每个约 ${bits} bit 熵${bits >= 80 ? '（足够强）' : bits >= 60 ? '（够用）' : '（偏弱）'}`
    }

    lenInput.addEventListener('change', run)
    countInput.addEventListener('change', run)

    body.append(
      el('div', { class: 'card-box stack', style: { marginBottom: '12px' } },
        el('div', { class: 'row' },
          el('span', { class: 'hint', text: '长度' }), lenInput,
          el('span', { class: 'hint', text: '数量' }), countInput,
          el('span', { class: 'spacer' }),
          btn('生成', run, 'primary'),
          btn('全部复制', () => copy(list.join('\n'), `已复制 ${list.length} 条`)),
        ),
        el('div', { class: 'row' },
          check('小写 a-z', useLower, (v) => { useLower = v; run() }),
          check('大写 A-Z', useUpper, (v) => { useUpper = v; run() }),
          check('数字 0-9', useDigits, (v) => { useDigits = v; run() }),
          check('符号 !@#$', useSymbols, (v) => { useSymbols = v; run() }),
          check('排除易混淆 Il1O0o', excludeAmbiguous, (v) => { excludeAmbiguous = v; run() }),
        ),
      ),
      pane('结果', out, { foot: foot(stat) }),
    )
    run()
  }

  /* ---------------- 强度 ---------------- */

  function strengthView(): void {
    body.innerHTML = ''
    const input = el('input', { class: 'inp', style: { flex: '1' }, placeholder: '输入要评估的密码（只在本地计算）' })
    const barHost = el('div', { class: 'swatch-lg', style: { height: '10px', borderRadius: '999px', background: '#eef0f4', overflow: 'hidden' } })
    const bar = el('div', { style: { height: '100%', width: '0%', background: 'var(--err)', transition: 'width .2s, background .2s' } })
    barHost.append(bar)
    const verdict = el('div', { class: 'msg' })
    const detail = el('div', { class: 'stat-grid', style: { marginTop: '12px' } })

    const run = (): void => {
      const pwd = input.value
      const sets = [
        { re: /[a-z]/, size: 26, label: '小写字母' },
        { re: /[A-Z]/, size: 26, label: '大写字母' },
        { re: /[0-9]/, size: 10, label: '数字' },
        { re: /[^A-Za-z0-9]/, size: 33, label: '符号' },
      ]
      const used = sets.filter((s) => s.re.test(pwd))
      const poolSize = used.reduce((sum, s) => sum + s.size, 0)
      const bits = pwd ? entropyBits(poolSize, pwd.length) : 0
      const hasRepeat = /(.)\1{2,}/.test(pwd)
      const hasSequence = /(abc|bcd|cde|123|234|345|456|567|678|789|qwe|asd|zxc)/i.test(pwd)
      const hasDate = /(19|20)\d{2}/.test(pwd)
      const common = /^(password|admin|123456|qwerty|letmein|welcome|iloveyou|monkey|dragon)/i.test(pwd)

      let score = Math.min(100, Math.round((bits / 90) * 100))
      if (hasRepeat) score -= 15
      if (hasSequence) score -= 20
      if (hasDate) score -= 10
      if (common) score = Math.min(score, 15)
      score = Math.max(0, score)

      const color = score >= 75 ? 'var(--ok)' : score >= 45 ? '#d97706' : 'var(--err)'
      bar.style.width = `${score}%`
      bar.style.background = color

      verdict.className = `msg ${score >= 75 ? 'ok' : score >= 45 ? 'warn' : 'err'}`
      verdict.textContent = !pwd
        ? ''
        : score >= 75
          ? `强度 ${score}/100 · 大约 ${bits} bit 熵，离线爆破也很难啃`
          : score >= 45
            ? `强度 ${score}/100 · 大约 ${bits} bit 熵，日常够用但别用来守金库`
            : `强度 ${score}/100 · 大约 ${bits} bit 熵，太弱了`

      detail.innerHTML = ''
      const items: Array<[string, string]> = [
        ['长度', String(pwd.length)],
        ['字符集', String(poolSize)],
        ['熵', `${bits} bit`],
        ['重复字符', hasRepeat ? '有' : '无'],
        ['键盘/数字序列', hasSequence ? '有' : '无'],
        ['疑似年份', hasDate ? '有' : '无'],
        ['常见弱口令', common ? '命中' : '未命中'],
      ]
      for (const [k, v] of items) {
        detail.append(el('div', { class: 'stat' }, el('b', { text: v }), el('span', { text: k })))
      }
    }

    input.addEventListener('input', run)
    body.append(
      el('div', { class: 'card-box stack' },
        input,
        barHost,
        verdict,
        detail,
        el('div', { class: 'hint', text: '熵按「字符集大小 ^ 长度」估算；命中常见弱口令或序列会直接降档。' }),
      ),
    )
    input.focus()
  }

  /* ---------------- Bcrypt ---------------- */

  function bcryptView(): void {
    body.innerHTML = ''
    const plain = el('input', { class: 'inp', style: { flex: '1' }, placeholder: '明文密码' })
    const rounds = el('select', { class: 'inp w-md' })
    for (const r of [4, 6, 8, 10, 12, 14]) rounds.append(el('option', { value: String(r), text: `${r} 轮（2^${r}）` }))
    rounds.value = '10'
    const hashOut = el('pre', { class: 'out', dataset: { ph: 'Bcrypt 哈希…' }, style: { minHeight: '80px' } })
    const verifyInput = el('input', { class: 'inp', style: { flex: '1' }, placeholder: '要校验的哈希' })
    const verifyMsg = el('div', { class: 'msg' })
    let hashValue = ''

    const doHash = (): void => {
      msg.className = 'msg'
      msg.textContent = ''
      if (!plain.value) return toast('先填明文')
      const t0 = performance.now()
      hashValue = hashSync(plain.value, Number(rounds.value))
      hashOut.textContent = hashValue
      msg.className = 'msg ok'
      msg.textContent = `耗时 ${Math.round(performance.now() - t0)} ms —— 轮数越高越慢，暴力破解也越贵`
    }

    const doVerify = (): void => {
      verifyMsg.className = 'msg'
      if (!verifyInput.value.trim()) {
        verifyMsg.className = 'msg err'
        verifyMsg.textContent = '先粘一段哈希'
        return
      }
      try {
        const ok = compareSync(plain.value, verifyInput.value.trim())
        verifyMsg.className = ok ? 'msg ok' : 'msg err'
        verifyMsg.textContent = ok ? '匹配：这份哈希就是用这个密码生成的' : '不匹配'
      } catch {
        verifyMsg.className = 'msg err'
        verifyMsg.textContent = '这段不是合法的 Bcrypt 哈希'
      }
    }

    body.append(
      el('div', { class: 'card-box stack' },
        el('div', { class: 'row' }, el('span', { class: 'hint', text: '明文' }), plain, el('span', { class: 'hint', text: '轮数' }), rounds,
          btn('生成哈希', doHash, 'primary'),
          btn('复制', () => copy(hashValue)),
        ),
        hashOut,
        el('div', { class: 'row' }, el('span', { class: 'hint', text: '校验' }), verifyInput, btn('比对', doVerify)),
        verifyMsg,
        el('div', { class: 'hint', text: 'Bcrypt 每次生成都带随机 salt，同样的密码两次结果不同，这是正常的。' }),
      ),
    )
    plain.focus()
  }

  function paintTabs(): void {
    tabs.innerHTML = ''
    for (const t of TABS) {
      tabs.append(
        el('button', {
          class: `tab${tab === t.id ? ' on' : ''}`,
          text: t.label,
          onclick: () => {
            tab = t.id
            paintTabs()
            paintBody()
          },
        }),
      )
    }
  }

  function paintBody(): void {
    if (tab === 'gen') genView()
    else if (tab === 'strength') strengthView()
    else bcryptView()
  }

  paintTabs()
  root.append(msg, tabs, body)
  paintBody()
}
