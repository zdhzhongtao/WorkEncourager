import { sm2, sm3, sm4 } from 'sm-crypto'
import { el } from '../../core/dom'
import { copy, toast } from '../../core/kit'
import { btn, foot, pane, toolbar } from '../../core/ui'

const DEFAULT_SM4_KEY = '0123456789abcdeffedcba9876543210'
const DEFAULT_IV = '0000000000000000'

type Tab = 'sm4' | 'sm3' | 'sm2'

export default function render(root: HTMLElement): void {
  let tab: Tab = 'sm4'
  const msg = el('div', { class: 'msg' })
  const tabs = el('div', { class: 'tabs' })
  const host = el('div')

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'sm4', label: 'SM4 对称加密' },
    { id: 'sm3', label: 'SM3 摘要' },
    { id: 'sm2', label: 'SM2 非对称' },
  ]

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

  function showError(err: unknown): void {
    msg.className = 'msg err'
    msg.textContent = (err as Error).message || String(err)
  }

  /* ---------------- SM4 ---------------- */

  function sm4View(): void {
    host.innerHTML = ''
    const modeSel = el('select', { class: 'inp w-md' })
    modeSel.append(el('option', { value: 'cbc', text: 'CBC（需要 IV）' }), el('option', { value: 'ecb', text: 'ECB' }))
    const keyInput = el('input', { class: 'inp', value: DEFAULT_SM4_KEY, style: { flex: '1', minWidth: '260px' } })
    const ivInput = el('input', { class: 'inp', value: DEFAULT_IV, style: { width: '160px' } })
    const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '明文，或要解密的 Hex 密文…' })
    const out = el('pre', { class: 'out', dataset: { ph: '结果…' } })
    let value = ''

    const run = (dir: 'enc' | 'dec'): void => {
      msg.className = 'msg'
      msg.textContent = ''
      try {
        const key = keyInput.value.trim()
        if (!/^[0-9a-fA-F]{32}$/.test(key)) throw new Error('SM4 密钥必须是 32 位十六进制（16 字节）')
        const mode = modeSel.value as 'cbc' | 'ecb'
        const iv = ivInput.value.trim()
        if (mode === 'cbc' && !/^[0-9a-fA-F]{32}$/.test(iv)) throw new Error('CBC 模式的 IV 也必须是 32 位十六进制')
        if (!input.value.trim()) return
        value = dir === 'enc'
          ? sm4.encrypt(input.value, key, { mode, iv })
          : sm4.decrypt(input.value.trim(), key, { mode, iv })
        out.textContent = value
        msg.className = 'msg ok'
        msg.textContent = dir === 'enc' ? '加密完成，输出 Hex' : '解密完成'
      } catch (err) {
        out.textContent = ''
        showError(err)
      }
    }

    const bar = toolbar(
      el('span', { class: 'hint', text: '模式' }), modeSel,
      el('span', { class: 'hint', text: '密钥' }), keyInput,
      el('span', { class: 'hint', text: 'IV' }), ivInput,
      el('span', { class: 'spacer' }),
      btn('加密 →', () => run('enc'), 'primary'),
      btn('← 解密', () => run('dec')),
    )

    host.append(
      bar,
      el('div', { class: 'panes' },
        pane('输入', input, { foot: foot(el('span', { text: 'SM4 密钥与 IV 都是 16 字节十六进制' })) }),
        pane('输出', out, {
          actions: [
            btn('复制', () => copy(value || out.textContent || '')),
            btn('拿去当输入', () => {
              input.value = value || out.textContent || ''
              toast('已填入左侧')
            }),
          ],
        }),
      ),
    )
  }

  /* ---------------- SM3 ---------------- */

  function sm3View(): void {
    host.innerHTML = ''
    const input = el('textarea', { class: 'ta', spellcheck: false, placeholder: '要算摘要的文本…' })
    const out = el('pre', { class: 'out', dataset: { ph: 'SM3 摘要（Hex）…' } })
    let value = ''

    const run = (): void => {
      msg.className = 'msg'
      msg.textContent = ''
      try {
        value = input.value ? sm3(input.value) : ''
        out.textContent = value
      } catch (err) {
        showError(err)
      }
    }

    host.append(
      toolbar(btn('计算', run, 'primary'), el('span', { class: 'spacer' }), btn('示例', () => {
        input.value = 'abc'
        run()
      }, 'ghost')),
      el('div', { class: 'panes' },
        pane('输入', input, { foot: foot(el('span', { text: '输出 256 位摘要，等同 SHA-256 长度' })) }),
        pane('输出', out, { actions: [btn('复制', () => copy(value))] }),
      ),
    )
    input.addEventListener('input', run)
  }

  /* ---------------- SM2 ---------------- */

  function sm2View(): void {
    host.innerHTML = ''
    let privateKey = ''
    let publicKey = ''

    const privInput = el('textarea', { class: 'ta', spellcheck: false, placeholder: '私钥（Hex）…', style: { minHeight: '80px' } })
    const pubInput = el('textarea', { class: 'ta', spellcheck: false, placeholder: '公钥（04 开头的 Hex）…', style: { minHeight: '80px' } })
    const dataInput = el('textarea', { class: 'ta', spellcheck: false, placeholder: '明文 / 密文 / 待签名内容…', style: { minHeight: '80px' } })
    const out = el('pre', { class: 'out', dataset: { ph: '结果…' }, style: { minHeight: '80px' } })
    const cipherSel = el('select', { class: 'inp w-md' })
    cipherSel.append(el('option', { value: '1', text: 'C1C3C2（新标准）' }), el('option', { value: '0', text: 'C1C2C3（旧标准）' }))
    let value = ''

    const sync = (): void => {
      privInput.value = privateKey
      pubInput.value = publicKey
    }

    const gen = (): void => {
      const pair = sm2.generateKeyPairHex()
      privateKey = pair.privateKey
      publicKey = pair.publicKey
      sync()
      msg.className = 'msg ok'
      msg.textContent = '已生成一对新密钥，私钥不要发给别人'
    }

    const act = (kind: 'enc' | 'dec' | 'sign' | 'verify'): void => {
      msg.className = 'msg'
      msg.textContent = ''
      try {
        const mode = (cipherSel.value === '0' ? 0 : 1) as 0 | 1
        const data = dataInput.value.trim()
        if (!data) return
        if (kind === 'enc') {
          if (!publicKey) throw new Error('先填公钥')
          value = sm2.doEncrypt(data, publicKey, mode)
        } else if (kind === 'dec') {
          if (!privateKey) throw new Error('先填私钥')
          value = sm2.doDecrypt(data, privateKey, mode)
        } else if (kind === 'sign') {
          if (!privateKey) throw new Error('先填私钥')
          value = sm2.doSignature(data, privateKey, { hash: true })
        } else {
          if (!publicKey) throw new Error('先填公钥')
          value = sm2.doVerifySignature(data.split('\n')[0], data.split('\n')[1] ?? '', publicKey, { hash: true })
            ? '验签通过'
            : '验签失败'
        }
        out.textContent = value
      } catch (err) {
        out.textContent = ''
        showError(err)
      }
    }

    host.append(
      toolbar(
        btn('生成密钥对', gen, 'primary'),
        el('span', { class: 'hint', text: '密文顺序' }), cipherSel,
        el('span', { class: 'spacer' }),
        btn('加密', () => act('enc')),
        btn('解密', () => act('dec')),
        btn('签名', () => act('sign')),
        btn('验签', () => act('verify')),
      ),
      el('div', { class: 'grid-2' },
        el('div', { class: 'field' }, el('span', { class: 'field-label', text: '私钥' }), privInput),
        el('div', { class: 'field' }, el('span', { class: 'field-label', text: '公钥' }), pubInput),
      ),
      el('div', { style: { marginTop: '10px' } },
        pane('内容', dataInput, {
          foot: foot(el('span', { text: '验签时：第一行原文，第二行签名' })),
        }),
      ),
      el('div', { style: { marginTop: '10px' } },
        pane('输出', out, { actions: [btn('复制', () => copy(value || out.textContent || ''))] }),
      ),
    )

    privInput.addEventListener('input', () => {
      privateKey = privInput.value.trim()
    })
    pubInput.addEventListener('input', () => {
      publicKey = pubInput.value.trim()
    })
  }

  function paintBody(): void {
    if (tab === 'sm4') sm4View()
    else if (tab === 'sm3') sm3View()
    else sm2View()
  }

  paintTabs()
  root.append(msg, tabs, host)
  paintBody()
}
