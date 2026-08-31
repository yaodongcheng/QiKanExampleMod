"""太阁5 Snr 剧本快照 → 人群归属表导出

用法: python extract_affiliations.py
输入: _analysis/decoded/Snr0-5.plain (已按 DeCode.rtg 解码)
输出: _analysis/decoded/affiliations.csv (personID, era, forceID, rank, superior, salary, kamon)
"""
import re, sys, os

DEC = os.path.join(os.path.dirname(__file__), 'editor124', 'DeCode.rtg')

def decode_snr(scn):
    de = open(DEC, 'rb').read()
    table = de[scn*256:(scn+1)*256]
    raw = open(os.path.join(r'E:/taikou5/Taikou5.Green.Edition-ALI213/Taikou5/TW', 'Snr%d_TW.TR5' % scn), 'rb').read()
    return bytes(table[b] for b in raw)

# 数据锚: 织田信长(编号195) 1560 属性串: 统96 武70 政97 智91 魅99 | 势力19 | 身份08 大名 | 1101无 | 俸禄0 | 野心100 忠诚100
ANCHOR = bytes.fromhex('6046615b6313421000084d04000064642779')

def attr_base(plain):
    hits = [m.start() for m in re.finditer(re.escape(ANCHOR), plain)]
    for h in hits:
        if h - 195*36 >= 0:
            return h - 195*36
    return None

FALLBACK_BASE = {5: 0x9540}  # 1598 太平之章：信长/秀吉已死，锚点不可用，硬编码基址（经 195/517 验证）

def extract(scn):
    plain = decode_snr(scn)
    base = attr_base(plain)
    if base is None:
        base = FALLBACK_BASE.get(scn)
    if base is None:
        return None
    rows = []
    # 人物 ID 1..1300，记录 36 字节
    for pid in range(1, 1400):
        off = base + pid*36
        if off + 36 > len(plain):
            break
        rec = plain[off:off+36]
        # 判空: 全零 = 不存在的人物槽
        if all(b == 0 for b in rec):
            continue
        force = rec[0x05]
        rank  = rec[0x09]
        sup   = int.from_bytes(rec[0x0A:0x0C], 'little')
        sal   = int.from_bytes(rec[0x0C:0x0E], 'little')
        amb   = rec[0x0E]; loy = rec[0x0F]
        kamon = rec[0x10]
        rows.append((pid, force, rank, sup, sal, amb, loy, kamon))
    return rows

eras = ['1554乱麻', '1560日轮', '1568升龙', '1575霸道', '1582转变', '1598太平']
out = []
for scn in range(6):
    rows = extract(scn)
    if rows is None:
        print('Snr%d anchor not found' % scn)
        continue
    for r in rows:
        out.append('%s,%d,%d,%d,%d,%d,%d,%d,%d' % (eras[scn], *r))

with open(os.path.join(os.path.dirname(__file__), '..', '_analysis', 'decoded', 'affiliations.csv'), 'w') as f:
    f.write('era,personID,forceID,rank,superior,salary,ambition,loyalty,kamon\n')
    f.write('\n'.join(out) + '\n')
print('written')
