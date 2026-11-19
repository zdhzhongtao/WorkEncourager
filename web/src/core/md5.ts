/**
 * MD5（RFC 1321）。
 * 浏览器 WebCrypto 不提供 MD5，而校验老系统文件时又要用，所以自己实现一份。
 * 单次处理整个 Uint8Array，纯函数、无依赖。
 */

const S = [
  7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9,
  14, 20, 5, 9, 14, 20, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21,
  6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
]

const K = (() => {
  const k = new Uint32Array(64)
  for (let i = 0; i < 64; i++) k[i] = Math.floor(Math.abs(Math.sin(i + 1)) * 4294967296)
  return k
})()

function rotl(x: number, c: number): number {
  return ((x << c) | (x >>> (32 - c))) >>> 0
}

/** 32 位字按小端序输出成 8 位十六进制 */
function leHex(v: number): string {
  let s = ''
  for (let i = 0; i < 4; i++) s += ((v >>> (i * 8)) & 0xff).toString(16).padStart(2, '0')
  return s
}

export function md5(input: Uint8Array): string {
  const len = input.length
  // 补齐到 64 字节整数倍，最后 8 字节放原始比特长度
  const padded = new Uint8Array((((len + 8) >> 6) + 1) << 6)
  padded.set(input)
  padded[len] = 0x80

  const bitLen = len * 8
  const view = new DataView(padded.buffer)
  view.setUint32(padded.length - 8, bitLen >>> 0, true)
  view.setUint32(padded.length - 4, Math.floor(bitLen / 4294967296), true)

  let a0 = 0x67452301
  let b0 = 0xefcdab89
  let c0 = 0x98badcfe
  let d0 = 0x10325476

  const m = new Uint32Array(16)

  for (let offset = 0; offset < padded.length; offset += 64) {
    for (let i = 0; i < 16; i++) m[i] = view.getUint32(offset + i * 4, true)

    let a = a0
    let b = b0
    let c = c0
    let d = d0

    for (let i = 0; i < 64; i++) {
      let f: number
      let g: number
      if (i < 16) {
        f = (b & c) | (~b & d)
        g = i
      } else if (i < 32) {
        f = (d & b) | (~d & c)
        g = (5 * i + 1) % 16
      } else if (i < 48) {
        f = b ^ c ^ d
        g = (3 * i + 5) % 16
      } else {
        f = c ^ (b | ~d)
        g = (7 * i) % 16
      }
      f = (f + a + K[i] + m[g]) >>> 0
      a = d
      d = c
      c = b
      b = (b + rotl(f, S[i])) >>> 0
    }

    a0 = (a0 + a) >>> 0
    b0 = (b0 + b) >>> 0
    c0 = (c0 + c) >>> 0
    d0 = (d0 + d) >>> 0
  }

  return leHex(a0) + leHex(b0) + leHex(c0) + leHex(d0)
}
