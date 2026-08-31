# -*- coding: utf-8 -*-
"""太阁5 六剧本优雅可读表生成器（era_v2 → 单文件 HTML 仪表盘）

输入: _analysis/decoded/era_v2/{1554,1560,1568,1575,1582,1598}/persons|cities|forces.csv
输出: _analysis/decoded/era_v2/taikou5_eras.html（浏览器打开，零外部依赖，数据内嵌）

用法: python build_viewer.py
生成物：改数据走 build_v2_era.py 重跑，改页面走本文件重跑；HTML 不得手改（铁律 22 精神）。
"""
import sys, os, csv, io, json, colorsys

ROOT = os.path.dirname(os.path.abspath(__file__))
DECODED = os.path.join(ROOT, 'decoded', 'era_v2')
OUT = os.path.join(DECODED, 'taikou5_eras.html')
ERAS = ['1554', '1560', '1568', '1575', '1582', '1598']
ERA_LABEL = {'1554': '乱麻', '1560': '日轮', '1568': '升龙', '1575': '霸道', '1582': '转变', '1598': '太平'}
FAMILY_COLORS = {
    '织田家': '#c8513e', '武田家': '#b03838', '今川家': '#8a4fa8', '北条家': '#5a7bc0',
    '德川家': '#b98d3e', '毛利家': '#4f8f6a', '上杉家': '#3f7f95', '丰臣家': '#d09a2c',
    '长宗我部家': '#6f8f4f', '岛津家': '#9a5f2f', '大友家': '#4f8f9f', '浅井家': '#7f7f3f',
    '朝仓家': '#5f7f9f', '斋藤家': '#7f5f5f', '六角家': '#5f8f7f', '本愿寺家': '#c89f3f',
    '伊达家': '#49567a', '南部家': '#3f6f9f', '芦名家': '#6f5f8f', '真田家': '#4f8f5f',
    '宇喜多家': '#7f9f5f', '龙造寺家': '#8f5f8f', '一条家': '#7f6f9f', '本愿寺显如家': '#c89f3f',
}


def fam_color(name):
    if name in FAMILY_COLORS:
        return FAMILY_COLORS[name]
    h = sum(ord(c) for c in name) % 36 / 36.0
    r, g, b = colorsys.hsv_to_rgb(h, 0.55, 0.62)
    return '#%02x%02x%02x' % (int(r * 255), int(g * 255), int(b * 255))


def load(era):
    d = {}
    for kind in ('persons', 'cities', 'forces'):
        with io.open(os.path.join(DECODED, era, kind + '.csv'), encoding='utf-8-sig') as f:
            d[kind] = list(csv.DictReader(f))
    return d


def compact(era, data):
    """精简字段：城名=官方名单 + 全量历史名事实表；城主/兵/粮/金=城表直读。"""
    cs = []
    for r in data['cities']:
        hist = [x for x in (r['name_history'] or '').split('|') if x]
        cur = r['name_official'] or ('城#' + r['city_idx'])
        cs.append({
            'idx': r['city_idx'], 'kana4': r['kana4'], 'chain': hist, 'name': cur,
            'lord': r['lord_name'], 'force': r['force_name'] or '—',
            's': int(r['soldiers']), 'food': int(r['food']), 'gold': int(r['gold']),
            'train': int(r['train']), 'morale': int(r['morale']),
        })
    ps = []
    for r in data['persons']:
        ps.append({
            'id': r['person_id'], 'name': r['name'], 'force': r['force_name'] or '无',
            'rank': r['rank'], 'sup': r['superior_name'] or '', 'salary': r['salary'] or '0',
            'amb': r['ambition'], 'loyal': r['loyalty'],
        })
    fs = [{'id': r['force_id'], 'name': r['force_name'], 'lord': r['lord_name'],
           'n': r['member_count']} for r in data['forces'] if r['force_name']]
    return {'label': ERA_LABEL[era], 'cities': cs, 'persons': ps, 'forces': fs}


def html_page(eras):
    data = {era: compact(era, load(era)) for era in ERAS}
    DATA = json.dumps(data, ensure_ascii=False)
    STYLE = """
:root{--bg:#171310;--panel:#211b15;--line:#3a2f24;--fg:#e8dcc8;--dim:#9a8a72;--gold:#d0a44c;--red:#c8513e}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font-family:"Microsoft YaHei","PingFang SC",sans-serif;font-size:14px}
header{background:linear-gradient(180deg,#221a10,#171310);border-bottom:1px solid var(--line);padding:18px 28px 0}
h1{margin:0;font-size:22px;letter-spacing:2px;color:var(--gold)} h1 small{font-size:12px;color:var(--dim);letter-spacing:0;margin-left:12px}
.tabs{display:flex;gap:8px;margin-top:14px;flex-wrap:wrap}
.tab{padding:8px 18px;border:1px solid var(--line);border-bottom:none;border-radius:8px 8px 0 0;cursor:pointer;color:var(--dim);background:transparent;user-select:none}
.tab.on{color:#131;font-weight:700;color:#1c1610;background:var(--gold)}
main{padding:18px 28px 40px;max-width:1280px;margin:0 auto}
.stats{display:flex;gap:16px;margin:0 0 14px;flex-wrap:wrap}
.stat{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:10px 18px;min-width:120px}
.stat b{font-size:20px;color:var(--gold);display:block} .stat span{color:var(--dim);font-size:12px}
.bar{display:flex;align-items:center;gap:14px;margin-bottom:14px;flex-wrap:wrap}
select,input{background:#120e0a;border:1px solid var(--line);color:var(--fg);border-radius:8px;padding:7px 12px;outline:none}
input{width:260px}
table{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th{background:#2a2118;color:var(--gold);font-weight:600;padding:8px 10px;text-align:left;cursor:pointer;white-space:nowrap;position:sticky;top:0}
td{padding:6px 10px;border-top:1px solid var(--line);white-space:nowrap}
tr:hover td{background:#2b231a}
.chip{display:inline-block;padding:2px 9px;border-radius:20px;font-size:12px;color:#fff;line-height:1.5}
.dim{color:var(--dim)} .small{font-size:12px}
.wrap{max-width:200px;white-space:normal;overflow-wrap:break-word}
.eq{font-size:11px;color:var(--dim)}
section{margin-top:26px} h2{color:var(--gold);font-size:16px;border-left:3px solid var(--gold);padding-left:10px}
.hint{color:var(--dim);font-size:12px;margin:6px 0 12px}
.foot{color:var(--dim);font-size:12px;margin-top:30px;border-top:1px solid var(--line);padding-top:10px}
"""
    JS = """
const D=__DATA__;let era='1554',forceF='',q='',srtC='s',srtD=-1,view='cities';
const $=s=>document.querySelector(s);
const fmt=n=>n.toLocaleString();
function chip(f){return '<span class="chip" style="background:'+(window.COL[f]||'#555')+'">'+f+'</span>'}
function cityRow(c){return `<tr data-idx="${c.idx}">
<td>${c.name}${c.chain&&c.chain.length>1?` <span class="eq" title="别名(改名链): ${c.chain.join(' → ')}">改名链</span>`:''}</td>
<td>${c.lord}</td><td>${chip(c.force)}</td>
<td><div style="width:90px;height:9px;background:#120e0a;border-radius:5px;overflow:hidden"><div style="width:${Math.min(100,c.s/200)}%;height:100%;background:var(--gold)"></div></div> ${c.s}</td>
<td>${fmt(c.food)}</td><td>${fmt(c.gold)}</td><td class="dim">${c.train} / ${c.morale}</td></tr>`}
function personRow(p){return `<tr><td>${p.name}</td><td>${chip(p.force)}</td><td>${p.sup||'—'}</td>
<td class="dim">${p.salary}</td><td class="dim">${p.amb}</td><td class="dim">${p.loyal}</td></tr>`}
function forceRow(f){return `<tr><td>${chip(f.name)}</td><td>${f.lord}</td><td>${f.n}</td></tr>`}
function render(){
 const d=D[era],cities=d.cities,persons=d.persons,forces=d.forces;
 $('#stats').innerHTML=`<div class="stat"><b>${persons.length}</b><span>人物</span></div>
 <div class="stat"><b>${cities.length}</b><span>城池</span></div>
 <div class="stat"><b>${forces.length}</b><span>势力</span></div>
 <div class="stat"><b>${fmt(cities.reduce((a,c)=>a+c.s,0))}</b><span>总兵力</span></div>`;
 const opts=['',...Array.from(new Set(forces.map(f=>f.name)))].map(f=>`<option value="${f}" ${f===forceF?'selected':''}>${f||'全部势力'}</option>`).join('');
 $('#forceSel').innerHTML=opts;
 let rows;
 if(view==='cities'){rows=cities.filter(c=>(!forceF||c.force===forceF)&&(!q||(c.name+c.lord+c.chain.join('')).includes(q))).map(cityRow)}
 else if(view==='persons'){rows=persons.filter(p=>(!forceF||p.force===forceF)&&(!q||(p.name+p.sup).includes(q))).map(personRow)}
 else {rows=forces.filter(f=>(!forceF||f.name===forceF)).map(forceRow)}
 $('#tbody').innerHTML=rows.join('')||'<tr><td colspan="8" class="dim">无匹配</td></tr>';
 $('.tabs').querySelectorAll('.tab').forEach(t=>t.classList.toggle('on',t.dataset.era===era));
}
document.addEventListener('click',e=>{
 const t=e.target.closest('.tab'); if(t){era=t.dataset.era;render();return}
 const b=e.target.closest('.viewbtn'); if(b){view=b.dataset.view;render();return}
});
$('#forceSel').addEventListener('input',e=>{forceF=e.target.value;render()});
$('#search').addEventListener('input',e=>{q=e.target.value.trim();render()});
render();
"""
    coljson = ",".join('%s:%s' % (json.dumps(k), json.dumps(fam_color(k))) for k in sorted(FAMILY_COLORS))
    files_input = """<footer class="foot">数据源：太阁立志传5 六剧本 Snr 文件解码（S-box 解密）· 生成器 build_v2_era.py / build_viewer.py · 城改名链=括号内别名并集 · 2026-08-31</footer>"""

    html = f"""<!doctype html><html lang="zh"><head><meta charset="utf-8"><title>太阁5 · 六剧本一览</title>
<style>{STYLE}</style></head><body>
<header><h1>太阁立志传5 · 六剧本一览<small>城池 · 人物 · 势力（全 6 剧本）</small></h1>
<div class="tabs" id="tabs">
<button class="tab on" data-era="1554">1554 乱麻</button><button class="tab" data-era="1560">1560 日轮</button>
<button class="tab" data-era="1568">1568 升龙</button><button class="tab" data-era="1575">1575 霸道</button>
<button class="tab" data-era="1582">1582 转变</button><button class="tab" data-era="1598">1598 太平</button>
</div></header>
<main>
<div class="stats" id="stats"></div>
<div class="bar">
<button class="tab small viewbtn" data-view="cities" style="border-radius:8px;border:1px solid var(--line)">城池表</button>
<button class="tab small viewbtn" data-view="persons" style="border-radius:8px;border:1px solid var(--line)">人物表</button>
<button class="tab small viewbtn" data-view="forces" style="border-radius:8px;border:1px solid var(--line)">势力表</button>
<select id="forceSel"></select><input id="search" placeholder="搜索城名 / 城主 / 人物 …">
</div>
<h2>名操作</h2><div class="hint">点击表头可排序；带「改名链」标记的城 = 历史曾用别名（如 观音寺城→安土城、石山本愿寺→大坂城）。</div>
<table><thead><tr><th>城名 / 人物</th><th>城主 / 势力</th><th>势力</th><th>兵</th><th>粮</th><th>金</th><th>训/士</th></tr></thead>
<tbody id="tbody"></tbody></table>
{files_input}
</main><script>
const __DATA__={DATA};
window.COL={{{coljson}}};
{JS}
</script></body></html>"""

    html = html.replace(
        'const __DATA__={DATA};', 'const __DATA__=' + DATA + ';'
    ).replace(
        'window.COL={{{coljson}}};', 'window.COL={' + coljson + '};'
    )
    with open(OUT, 'w', encoding='utf-8') as f:
        f.write(html)
    print('written:', OUT, '(', os.path.getsize(OUT) // 1024, 'KB )')


if __name__ == '__main__':
    html_page(ERAS)
