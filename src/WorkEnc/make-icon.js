// 生成 app.ico（纯 Node 实现，无外部依赖）
const fs = require('fs');
const zlib = require('zlib');

const S = 256;      // 输出尺寸
const SS = 4;       // 超采样倍数
const W = S * SS;

// ---------- 几何辅助 ----------
function roundedRectAlpha(x, y, w, h, r) {
  // 返回 0/1：点是否在圆角矩形内
  const cx = Math.min(Math.max(x, r), w - r);
  const cy = Math.min(Math.max(y, r), h - r);
  const dx = x - cx, dy = y - cy;
  return (dx * dx + dy * dy) <= r * r || (x >= r && x <= w - r) || (y >= r && y <= h - r);
}
function distToSeg(px, py, x1, y1, x2, y2) {
  const dx = x2 - x1, dy = y2 - y1;
  const len2 = dx * dx + dy * dy;
  let t = len2 === 0 ? 0 : ((px - x1) * dx + (py - y1) * dy) / len2;
  t = Math.max(0, Math.min(1, t));
  const qx = x1 + t * dx, qy = y1 + t * dy;
  return Math.hypot(px - qx, py - qy);
}

const R = 56 * SS;
const STROKE = 19 * SS;
const segs = [
  [78, 70, 128, 130], [178, 70, 128, 130],   // ¥ 两条斜笔
  [128, 130, 128, 190],                       // 竖笔
  [86, 138, 170, 138], [86, 163, 170, 163]    // 两条横笔
].map(s => s.map(v => v * SS));

// 渐变色
const c1 = [251, 191, 36], c2 = [249, 115, 22];

// ---------- 绘制（超采样） ----------
const buf = Buffer.alloc(W * W * 4, 0);
for (let y = 0; y < W; y++) {
  for (let x = 0; x < W; x++) {
    const i = (y * W + x) * 4;
    if (!roundedRectAlpha(x, y, W, W, R)) continue;

    const t = (x / W + y / W) / 2;
    let r = Math.round(c1[0] + (c2[0] - c1[0]) * t);
    let g = Math.round(c1[1] + (c2[1] - c1[1]) * t);
    let b = Math.round(c1[2] + (c2[2] - c1[2]) * t);

    // 白色 ¥
    let d = Infinity;
    for (const s of segs) d = Math.min(d, distToSeg(x, y, s[0], s[1], s[2], s[3]));
    if (d <= STROKE / 2) { r = 255; g = 255; b = 255; }

    buf[i] = r; buf[i + 1] = g; buf[i + 2] = b; buf[i + 3] = 255;
  }
}

// ---------- 降采样 ----------
const out = Buffer.alloc(S * S * 4, 0);
for (let y = 0; y < S; y++) {
  for (let x = 0; x < S; x++) {
    let r = 0, g = 0, b = 0, a = 0;
    for (let sy = 0; sy < SS; sy++) {
      for (let sx = 0; sx < SS; sx++) {
        const i = ((y * SS + sy) * W + (x * SS + sx)) * 4;
        r += buf[i]; g += buf[i + 1]; b += buf[i + 2]; a += buf[i + 3];
      }
    }
    const n = SS * SS;
    const o = (y * S + x) * 4;
    out[o] = Math.round(r / n); out[o + 1] = Math.round(g / n);
    out[o + 2] = Math.round(b / n); out[o + 3] = Math.round(a / n);
  }
}

// ---------- PNG 编码 ----------
const crcTable = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return t;
})();
function crc32(buff) {
  let c = 0xFFFFFFFF;
  for (let i = 0; i < buff.length; i++) c = crcTable[(c ^ buff[i]) & 0xFF] ^ (c >>> 8);
  return (c ^ 0xFFFFFFFF) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length, 0);
  const t = Buffer.from(type, 'ascii');
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([t, data])), 0);
  return Buffer.concat([len, t, data, crc]);
}
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(S, 0); ihdr.writeUInt32BE(S, 4);
ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;

const raw = Buffer.alloc((S * 4 + 1) * S);
for (let y = 0; y < S; y++) {
  raw[y * (S * 4 + 1)] = 0;
  out.copy(raw, y * (S * 4 + 1) + 1, y * S * 4, (y + 1) * S * 4);
}
const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
  chunk('IHDR', ihdr),
  chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0))
]);

// ---------- ICO 封装 ----------
const header = Buffer.alloc(6 + 16);
header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(1, 4);
header.writeUInt8(0, 6); header.writeUInt8(0, 7); header.writeUInt8(0, 8); header.writeUInt8(0, 9);
header.writeUInt16LE(1, 10); header.writeUInt16LE(32, 12);
header.writeUInt32LE(png.length, 14); header.writeUInt32LE(22, 18);

const target = process.argv[2];
fs.writeFileSync(target, Buffer.concat([header, png]));
console.log('icon ->', target, fs.statSync(target).size, 'bytes');
