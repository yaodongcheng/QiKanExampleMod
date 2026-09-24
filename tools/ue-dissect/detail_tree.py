# -*- coding: utf-8 -*-
"""生成「FCS 详情解析」文件夹：**文件层级镜像 UE 工程自己的目录结构**，一个资产一个 .md。

为什么用 Markdown 而不是 XML/JSON：
  - 第一读者是人（要能直接在 IDE 里读、点、搜）；XML 只增加仪式感没有可读性。
  - 机器可读的部分（数据表、枚举、结构体）本来就有 CSV/JSON 侧车（out/tables、out/digest），
    这里只做「人读详情页」，需要机读时走侧车，不重复维护两份结构化数据。
  - T3D 原始文本（577MB）在 Debug/offline/fcs_dump/out/t3d/，是保真底稿，不做搬运。

每个蓝图详情页包含：父类/类型 · 变量（名/类型/默认值）· 实现接口 · 用到的组件（从 GetComponentByClass 反推）
· 每个图的伪代码（有 exec 走执行线、纯函数走数据流）· 引用的资产（粒子/音效/材质/网格/其他蓝图）
枚举页 = 有序值表；结构体页 = 字段+类型+默认值；数据表页 = 行×列值表。
"""
import os, io, re, sys, json, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t3d_tools as T
import vfx_breakdown as VB          # Niagara 逐系统拆解（含参数值解码）
import cascade_breakdown as CB      # Cascade 逐系统拆解
import digest_anim as DA            # 动画通知摘要

from paths import DUMP_ROOT as OUT
from paths import MIRROR_ROOT as DST
WITH_PINS = '--no-pins' not in sys.argv
TAIL = re.compile(r'_(\d{1,3})_([0-9A-Fa-f]{32})$')

REF_KIND = [
    ('NiagaraSystem', '粒子'), ('ParticleSystem', '粒子(Cascade)'), ('NiagaraEmitter', '粒子发射器'),
    ('AnimMontage', '动画'), ('AnimSequence', '动画序列'), ('BlendSpace', '混合空间'), ('BlendSpace1D', '混合空间'),
    ('SoundCue', '音效'), ('SoundWave', '音效'), ('MetaSoundSource', '音效'),
    ('MaterialInstanceConstant', '材质'), ('Material', '材质'), ('MaterialFunction', '材质函数'),
    ('Texture2D', '贴图'), ('StaticMesh', '网格'), ('SkeletalMesh', '网格'),
    ('WidgetBlueprintGeneratedClass', '控件'), ('WidgetBlueprint', '控件'),
    ('BlueprintGeneratedClass', '蓝图类'), ('Blueprint', '蓝图'), ('Class', '类'),
    ('DataTable', '数据表'), ('CurveFloat', '曲线'), ('CurveVector', '曲线'), ('CurveLinearColor', '曲线'),
    ('PhysicsAsset', '物理资产'), ('Skeleton', '骨架'), ('CameraShake', '相机震动'),
]
BOILER = re.compile(r'^(b|Primary|Replicated|Net|Role|Owner|Instigator|Root|Pivot|Sprite|Actor|Default|Custom|Input|Min|Max|On[A-Z])')
BOILER_KEYS = set("""bNetTemporary bNetStartup bOnlyRelevantToOwner bAlwaysRelevant bReplicateMovement bHidden bTearOff
bForceNetAddressable bNetLoadOnClient bNetUseOwnerRelevancy bRelevantForNetworkReplays bRelevantForLevelBounds
bReplayRewindable bAllowTickBeforeBeginPlay bAutoDestroyWhenFinished bCanBeDamaged bBlockInput bCollideWhenPlacing
bFindCameraComponentWhenViewTarget bGenerateOverlapEventsDuringLevelStreaming bIgnoresOriginShifting
bEnableAutoLODGeneration bIsEditorOnlyActor bActorSeamlessTraveled bReplicates bCanBeInCluster
bAllowReceiveTickEventOnDedicatedServer bActorEnableCollision UpdateOverlapsMethodDuringLevelStreaming
DefaultUpdateOverlapsMethodDuringLevelStreaming ReplicatedMovement InitialLifeSpan CustomTimeDilation Owner
NetDriverName Role NetDormancy SpawnCollisionHandlingMethod AutoReceiveInput InputPriority InputComponent
NetCullDistanceSquared NetUpdateFrequency MinNetUpdateFrequency NetPriority Instigator RootComponent PivotOffset
ParentComponent SpriteScale ActorLabel FolderPath bHiddenEd bLockLocation bActorLabelEditable bEditable
bListedInSceneOutliner bOptimizeBPComponentData PrimaryActorTick Tags ActorGuid ActorLabel
""".split())
BOILER_PREFIX = ('OnTakeAny', 'OnTakePoint', 'OnTakeRadial', 'OnActor', 'OnBeginCursor', 'OnEndCursor', 'OnClicked',
                 'OnReleased', 'OnInputTouch', 'OnDestroyed', 'OnEndPlay', 'bCanEverTick')


def q(s):
    return (s or '').strip().strip('"')


def mk(path):
    d = os.path.dirname(path)
    if d and not os.path.isdir(d):
        os.makedirs(d)


def parse_refs(txt):
    c = collections.defaultdict(collections.Counter)
    for m in re.finditer(r"(\w+)'\"(/Game/[^\"]+)\"", txt):
        kind = None
        for k, label in REF_KIND:
            if m.group(1) == k:
                kind = label
                break
        if kind:
            c[kind][m.group(2).split('.')[-1]] += 0
            c[kind][m.group(2).split('.')[-1]] += 1
    return c


def parse_vars(txt):
    """NewVariables(n)=(VarName=..,VarType=(..),FriendlyName=..,Category=..)"""
    out = []
    for m in re.finditer(r'NewVariables\((\d+)\)=\((.*)\)\s*$', txt, re.M):
        body = m.group(2)
        d = {}
        for part in T.split_top(body):
            if '=' not in part:
                continue
            k, v = part.split('=', 1)
            d[k.strip()] = v.strip()
        vt = d.get('VarType', '')
        cat = re.search(r'PinCategory="([^"]*)"', vt)
        sub = re.search(r'PinSubCategoryObject=(?:Class|ScriptStruct|UserDefinedEnum|UserDefinedStruct)\'"?([^"\']+)', vt)
        cont = re.search(r'ContainerType=(\w+)', vt)
        out.append({
            'idx': int(m.group(1)),
            'name': q(d.get('VarName')),
            'friendly': q(d.get('FriendlyName')),
            'type': (cat.group(1) if cat else '?'),
            'sub': (sub.group(1).split('.')[-1].rstrip("'") if sub else ''),
            'container': (cont.group(1) if cont and cont.group(1) != 'None' else ''),
            'category': q(d.get('Category')),
        })
    out.sort(key=lambda x: x['idx'])
    return out


def graph_pseudocode(grp, maxsteps=200):
    has_exec = any(T.pin_summary(p)['category'] == 'exec' for e in grp.values() for p in e['pins'])
    try:
        if has_exec:
            return '\n'.join(T.linearize(grp, max_steps=maxsteps))
        return '\n'.join(T.dataflow(grp))
    except Exception as e:
        return '<线性化失败 %r>' % e


def bp_page(name, r):
    p = os.path.join(OUT, 't3d', 'bp', name + '.t3d')
    cp = os.path.join(OUT, 't3d', 'cdo', name + '_CDO.t3d')
    if not os.path.exists(p):
        return None
    txt = io.open(p, encoding='utf-8', errors='replace').read()
    root, scopes, graphs = T.parse_t3d(p)
    gs = T.graphs_of(scopes)
    top = scopes.get(('root',), {}).get(name)
    L = []
    L.append('# %s' % name)
    L.append('')
    L.append('> `%s` ｜ 类型 **%s**' % (r['path'], r['class']))
    if top:
        pc = None
        for pr in top['props']:
            if pr.startswith('ParentClass='):
                pc = pr.split('=', 1)[1]
        if pc:
            L.append('> 父类 `%s`' % pc.split('/')[-1].strip("'\""))
    # 接口
    ifaces = sorted(set(re.findall(r"ImplementedInterfaces\(\d+\)=\(Interface=\w+'\"([^\"]+)\"", txt)))
    L.append('')
    L.append('## 实现接口（%d）' % len(ifaces))
    for i in (ifaces or ['（无）']):
        L.append('- `%s`' % i.split('.')[-1])
    # 组件 + 调用清单
    # 变量
    vs = parse_vars(txt)
    if vs:
        L.append('')
        L.append('## 变量（%d）' % len(vs))
        L.append('')
        L.append('| # | 变量 | 类型 | 默认值(CDO) | 分类 |')
        L.append('|---|---|---|---|---|')
        cdo_vals = {}
        if os.path.exists(cp):
            cr, cs, cg = T.parse_t3d(cp)
            for k, e in cs.get(('root',), {}).items():
                if k.startswith('Default__'):
                    for pr in e['props']:
                        if '=' in pr:
                            kk, vv = pr.split('=', 1)
                            cdo_vals[kk] = vv[:60]
                    break
        for v in vs:
            ty = v['type'] + ((' of ' + v['sub']) if v['sub'] else '') + ((' [%s]' % v['container']) if v['container'] else '')
            cat = v['category']
            mm = re.findall(r'"([^"]*)"', cat)
            if mm:
                cat = mm[-1]
            L.append('| %d | `%s` | %s | `%s` | %s |' % (
                v['idx'], v['friendly'] or v['name'], ty, cdo_vals.get(v['name'], ''), cat))
    # 组件：引用闭包里落在 Blueprints/Components/ 下的蓝图类（T3D 拿不到 SCS 组件模板，用引用闭包替代）
    refcls = set(re.findall(r"BlueprintGeneratedClass'\"(/Game/[^\"]+)\"", txt))
    comps = sorted({c.split('.')[-1].replace('_C', '') for c in refcls if '/Blueprints/Components/' in c})
    L.append('')
    L.append('## 用到的组件（%d）' % len(comps))
    for c in (comps or ['（无）']):
        L.append('- `%s`' % c)
    # 调用的函数（跨全图去重）
    fns = collections.Counter()
    for gname, grp in gs.items():
        for n, e in grp.items():
            k = T.kind_of(e)
            if k in ('call', 'callarr', 'super'):
                mn = T.member_of(e)[1]
                if mn:
                    fns[mn] += 1
    if fns:
        L.append('')
        L.append('## 调用的函数（%d 个，去重）' % len(fns))
        L.append('')
        L.append(' '.join('`%s`' % f for f, _ in fns.most_common(60)))
    # 图
    L.append('')
    L.append('## 图与伪代码（%d 个图）' % len(gs))
    for gname in sorted(gs):
        grp = gs[gname]
        if not grp:
            continue
        L.append('')
        L.append('### `%s`（%d 节点）' % (gname, len(grp)))
        L.append('')
        L.append('```')
        L.append(graph_pseudocode(grp))
        L.append('```')
        if WITH_PINS:
            L.append('')
            L.append('<details><summary>节点引脚明细（%d 节点：每个引脚来自谁 / 去往谁 / 字面量 / 类型）</summary>' % len(grp))
            L.append('')
            L.extend(T.node_pin_detail(grp, indent=''))
            L.append('</details>')
    # 引用
    refs = parse_refs(txt)
    if refs:
        L.append('')
        L.append('## 引用的资产')
        for kind in sorted(refs):
            items = refs[kind].most_common(40)
            L.append('- **%s**（%d）: %s' % (kind, len(refs[kind]), ', '.join('`%s`' % i[0] for i in items)))
    # CDO 其余默认值
    if os.path.exists(cp):
        cr, cs, cg = T.parse_t3d(cp)
        entry = None
        for k, e in cs.get(('root',), {}).items():
            if k.startswith('Default__'):
                entry = e
                break
        if entry:
            extra = []
            for pr in entry['props']:
                if '=' not in pr:
                    continue
                kk = pr.split('=', 1)[0]
                if kk in BOILER_KEYS or kk.startswith(BOILER_PREFIX):
                    continue
                extra.append(pr[:150])
            if extra:
                L.append('')
                L.append('<details><summary>其它默认值（%d，含引擎样板）</summary>' % len(extra))
                L.append('')
                L.append('```')
                L.extend(extra)
                L.append('```')
                L.append('</details>')
    L.append('')
    return '\n'.join(L)


def enum_page(name, r):
    p = os.path.join(OUT, 't3d', 'enum', name + '.t3d')
    if not os.path.exists(p):
        return None
    txt = io.open(p, encoding='utf-8', errors='replace').read()
    m = re.search(r'DisplayNameMap=\((.*)\)\s*$', txt, re.M)
    items = re.findall(r'\("([^"]+)",\s*NSLOCTEXT\([^)]*?"([^"]*)"\)\)', m.group(1)) if m else []
    L = ['# %s' % name, '', '> `%s` ｜ 枚举 ｜ %d 项' % (r['path'], len(items)), '', '| 值 | 显示名 | 内部名 |', '|---|---|---|']
    for i, (raw, disp) in enumerate(items):
        L.append('| %d | %s | %s |' % (i, disp, raw))
    L.append('')
    return '\n'.join(L)


def struct_page(name, r):
    p = os.path.join(OUT, 't3d', 'struct', name + '.t3d')
    if not os.path.exists(p):
        return None
    root, scopes, graphs = T.parse_t3d(p)
    props = []
    for k, e in scopes.get(('root',), {}).items():
        props.extend(e['props'])
    rows = []
    for pr in props:
        m = re.match(r'VariablesDescriptions\((\d+)\)=\((.*)\)$', pr)
        if not m:
            continue
        idx = int(m.group(1))
        kv = {}
        for part in T.split_top(m.group(2)):
            if '=' in part:
                k, v = part.split('=', 1)
                kv[k.strip()] = v.strip()
        rows.append((idx, kv))
    rows.sort()
    L = ['# %s' % name, '', '> `%s` ｜ 结构体 ｜ %d 字段' % (r['path'], len(rows)), '',
         '| # | 字段 | 类型 | 子类型 | 默认值 |', '|---|---|---|---|---|']
    for idx, kv in rows:
        fn = q(kv.get('FriendlyName')) or TAIL.sub('', kv.get('VarName', ''))
        cat = q(kv.get('Category'))
        sub = kv.get('SubCategoryObject', '')
        sub = sub.split('.')[-1].rstrip("'") if sub else ''
        dv = kv.get('DefaultValue', '')
        dv = dv[:120].replace('|', '\\|')
        L.append('| %d | `%s` | %s | %s | `%s` |' % (idx, fn, cat, sub, dv))
    L.append('')
    return '\n'.join(L)


def dt_page(name, r, cells):
    t = cells.get(name)
    if not t:
        return None
    TAIL2 = TAIL
    cols = {TAIL2.sub('', k): v for k, v in t['cells'].items()}
    rows = t['rows']
    L = ['# %s' % name, '', '> `%s` ｜ 数据表 ｜ %d 行 × %d 列（有值）' % (r['path'], len(rows), len(cols)), '']
    L.append('> 宽表看 CSV 侧车更舒服：`Debug/offline/fcs_dump/out/tables/%s.csv`' % name)
    L.append('')
    hdr = sorted(cols)
    L.append('| 行 | %s |' % ' | '.join('`%s`' % c for c in hdr))
    L.append('|' + '---|' * (len(hdr) + 1))
    for i, rn in enumerate(rows):
        vals = []
        for c in hdr:
            v = cols[c][i] if i < len(cols[c]) else ''
            vals.append(v.replace('|', '\\|')[:160])
        L.append('| `%s` | %s |' % (rn, ' | '.join(vals)))
    L.append('')
    return '\n'.join(L)


def vfx_page(name, r):
    """Niagara 系统/发射器页：emitter → 渲染器（材质/网格/图集）→ 模块栈 → **全部参数值**。"""
    p = os.path.join(VB.VFX, name + '.t3d')
    if not os.path.exists(p):
        return None
    d = VB.extract(p)
    L = ['# %s' % name, '', '> `%s` ｜ 类型 **%s**' % (r['path'], r['class']), '']
    if not d.get('emitters'):
        # 发射器资产（NiagaraEmitter）或空系统：退化为系统级属性
        root, scopes, graphs = T.parse_t3d(p)
        top = scopes.get(('root',), {}).get(name)
        L.append('> 该系统无 emitter 子对象（独立发射器资产），系统级属性如下：')
        L.append('')
        L.append('```')
        for pr in (top['props'] if top else [])[:60]:
            L.append(pr[:220])
        L.append('```')
        L.append('')
        return '\n'.join(L)
    for em in d['emitters']:
        L.append('## emitter `%s`%s%s' % (em['name'],
                 '（局部空间）' if em.get('local_space') else '',
                 '（插值生成）' if em.get('interpolated') else ''))
        L.append('')
        for rd in em.get('renderers') or []:
            line = '- 渲染器 `%s`' % rd['type']
            if rd.get('material'):
                line += ' 材质 `%s`' % rd['material']
            if rd.get('Mesh'):
                line += ' 网格 `%s`' % rd['Mesh'].split('/')[-1].strip("'")
            for k in ('Alignment', 'FacingMode', 'SubImageSize', 'SortMode'):
                if rd.get(k):
                    line += ' %s=%s' % (k, rd[k])
            L.append(line)
            for n, t in (rd.get('textures') or []):
                L.append('  - 贴图 `%s` ← `%s`' % (n, t.split('/')[-1]))
        for st, mods in (em.get('stages') or {}).items():
            L.append('- 模块[%s]: %s' % (st, ' → '.join('`%s`' % m for m in mods)))
        params = em.get('params') or []
        if params:
            L.append('')
            L.append('### 参数值（%d）' % len(params))
            L.append('')
            L.append('| 阶段 | 参数 | 值 |')
            L.append('|---|---|---|')
            for prm in params:
                # 🔴 常量前缀的段名 ≠ 图里的 emitter 节点名（Embers001 vs Embers001_10），必须前缀无关地剥
                pname = re.sub(r'^Constants\.[^.]+\.', '', prm['name'])
                L.append('| %s | `%s` | `%s` |' % (prm['stage'], pname, prm['value']))
        L.append('')
    return '\n'.join(L)


def cascade_page(name, r):
    """Cascade 页：emitter → LOD → Required（材质/时长/图集）→ Spawn → 模块与分布值。"""
    p = os.path.join(CB.VFX, name + '.t3d')
    if not os.path.exists(p):
        return None
    d = CB.extract(p)
    L = ['# %s' % name, '', '> `%s` ｜ 类型 **%s**（Cascade 老粒子）' % (r['path'], r['class']), '']
    for em in d.get('emitters') or []:
        L.append('## emitter `%s` (%s) LOD=%d' % (em.get('name'), em.get('type'), len(em.get('levels') or [])))
        L.append('')
        for Lv in (em.get('lod') or []):
            if not Lv.get('enabled'):
                continue
            req = Lv.get('required') or {}
            sp = (Lv.get('spawn') or {}).get('rate')
            rate = ''
            if isinstance(sp, dict):
                rate = sp.get('max') if sp.get('min') == sp.get('max') else '%s~%s' % (sp.get('min'), sp.get('max'))
            L.append('- **LOD%s** 材质 `%s` 时长=%s 延迟=%s 图集=%s %s SpawnRate=%s TypeData=%s' % (
                Lv.get('level'), req.get('material'), req.get('emitter_duration'), req.get('emitter_delay'),
                req.get('subimages'), req.get('alignment') or '', rate,
                (Lv.get('typedata') or '').replace('ParticleModuleTypeData', '')))
            for md in (Lv.get('modules') or []):
                parts = []
                for k, v in md.items():
                    if k == 'module':
                        continue
                    if isinstance(v, dict):
                        if 'min' in v and 'max' in v and v.get('min') != v.get('max'):
                            parts.append('%s=%s~%s' % (k, v['min'], v['max']))
                        elif 'min' in v:
                            parts.append('%s=%s' % (k, v['min']))
                        elif v.get('MaxValueVec'):
                            parts.append('%s=%s~%s' % (k, v.get('MinValueVec'), v['MaxValueVec']))
                        elif v.get('dist'):
                            parts.append('%s[%s]' % (k, v.get('dist_name')))
                        else:
                            parts.append('%s=?' % k)
                    else:
                        parts.append('%s=%s' % (k, str(v)[:40]))
                L.append('  - `%s` %s' % (md['module'], ' '.join(parts[:8])))
        L.append('')
    return '\n'.join(L)


ANIM_CLASSES = ('AnimMontage', 'AnimSequence', 'AnimComposite', 'BlendSpace', 'BlendSpace1D', 'AimOffsetBlendSpace')
RETARGET_ROOT = os.environ.get('UE_RETARGET_ROOT', r'D:/BrainMaker/骑砍2动画重定向')
ANIM_INDEX = {}      # 名称 -> 附加信息（骨架/帧数/时长/通知数/引用/被引用/已导出/已重定向）


def _read_retarget_status():
    """可选：读重定向工程的产物，标出「已导出 FBX / 已重定向 TRF」。目录不存在就返回空集。"""
    exp, trf = set(), set()
    for sub, bucket in (('output/fbx', exp), ('output/trf', trf)):
        d = os.path.join(RETARGET_ROOT, sub)
        if not os.path.isdir(d):
            continue
        for f in os.listdir(d):
            if f.lower().endswith(('.fbx', '.trf')):
                n = f.rsplit('.', 1)[0]
                bucket.add(n[3:] if n.startswith('ue_') else n)
    return exp, trf


def anim_page(name, r):
    """动画页：骨架 / 帧数帧率 / 时长 / RateScale / 通知（名字+时间+时长+轨道）/ 段落 / 引用与被引用 / 提取状态。"""
    p = os.path.join(OUT, 't3d', 'anim', name + '.t3d')
    if not os.path.exists(p):
        return None
    txt = io.open(p, encoding='utf-8', errors='replace').read()
    root, scopes, graphs = T.parse_t3d(p)
    e = scopes.get(('root',), {}).get(name)
    L = ['# %s' % name, '', '> `%s` ｜ 类型 **%s**' % (r['path'], r['class']), '']
    info = ANIM_INDEX.get(name) or {}
    # 引用的动画片段（蒙太奇 → 序列）—— 先算，下面状态行要用
    refs = sorted(set(m.group(1).split('.')[-1] for m in
                      re.finditer(r"(?:LinkedSequence|AnimReference)=AnimSequence'\"([^\"]+)\"", txt)))
    # 骨架 / 帧数
    sk = re.search(r"Skeleton=Skeleton'\"([^\"]+)\"", txt)
    nf = re.search(r'NumFrames=(\d+)', txt)
    fr = re.search(r'ImportFileFramerate=([\d.]+)', txt)
    tracks = len(re.findall(r'TrackToSkeletonMapTable\(', txt))
    L.append('| 项 | 值 |')
    L.append('|---|---|')
    if sk:
        L.append('| 骨架 | `%s` |' % sk.group(1).split('/')[-1].split('.')[0])
    if nf:
        L.append('| 帧数 | %s%s |' % (nf.group(1), (' @ %s fps' % fr.group(1)) if fr else ''))
    for pr in (e['props'] if e else []):
        if pr.startswith(('SequenceLength=', 'RateScale=', 'bEnableRootMotion=')):
            L.append('| %s | %s |' % tuple(pr.split('=', 1)))
    if tracks:
        L.append('| 骨骼轨道数 | %d |' % tracks)
    if info.get('notifies') is not None:
        L.append('| 通知数 | %d |' % info['notifies'])
    if r['class'] == 'AnimMontage':
        st = []
        for x in refs:
            v = ANIM_INDEX.get(x) or {}
            st.append('%s：%s' % (x, ('已导出+已TRF' if v.get('trf') else ('已导出' if v.get('exported') else '未导出'))))
        L.append('| 重定向工程 | %s |' % ('；'.join(st) if st else '（它自己不导出，导出的是片段）'))
    else:
        exp, trf = info.get('exported'), info.get('trf')
        L.append('| 重定向工程 | %s |' % ('✅ 已导出 FBX' + ('，✅ 已重定向 TRF' if trf else '，未重定向') if exp else '❌ 未导出'))
    # 通知明细
    notif_lines = [l for l in (DA.dig(name) or '').split('\n') if '通知[' in l]
    if notif_lines:
        L.append('')
        L.append('## 动画通知（决定出手帧）')
        L.append('')
        L.append('```')
        L.extend(l.strip() for l in notif_lines)
        L.append('```')
    # 引用 / 被引用
    L.append('')
    L.append('## 引用的动画片段（%d）' % len(refs))
    for x in (refs or ['（无 —— 它自己就是片段）']):
        L.append('- `%s`' % x)
    users = sorted(info.get('usedby') or [])
    L.append('')
    L.append('## 被这些蒙太奇引用（%d）' % len(users))
    for x in (users or ['（无）']):
        L.append('- `%s`' % x)
    # 段落 / 槽位（原始行，便于核对）
    slots = [pr for pr in (e['props'] if e else []) if pr.startswith(('CompositeSections(', 'SlotAnimTracks('))]
    if slots:
        L.append('')
        L.append('<details><summary>段落与槽位（原始行）</summary>')
        L.append('')
        L.append('```')
        L.extend(s[:400] for s in slots[:12])
        L.append('```')
        L.append('</details>')
    L.append('')
    return '\n'.join(L)


def main():
    inv = json.load(io.open(os.path.join(OUT, '01_inventory.json'), encoding='utf-8'))
    cells = {}
    cj = os.path.join(OUT, '13_dtcells.json')
    if os.path.exists(cj):
        cells = json.load(io.open(cj, encoding='utf-8'))
    # ── 预扫：动画的「被谁引用」反向索引 + 通知数 + 提取状态 ──
    exp_set, trf_set = _read_retarget_status()
    seq_users = collections.defaultdict(set)
    for r in inv:
        if r.get('class') != 'AnimMontage':
            continue
        ap = os.path.join(OUT, 't3d', 'anim', r['name'] + '.t3d')
        if not os.path.exists(ap):
            continue
        t = io.open(ap, encoding='utf-8', errors='replace').read()
        for m in re.finditer(r'(?:LinkedSequence|AnimReference)=AnimSequence\'"([^"\']+)', t):
            seq_users[m.group(1).split('.')[-1]].add(r['name'])
    for r in inv:
        if r.get('class') in ANIM_CLASSES:
            nm = r['name']
            ap = os.path.join(OUT, 't3d', 'anim', nm + '.t3d')
            t = io.open(ap, encoding='utf-8', errors='replace').read() if os.path.exists(ap) else ''
            g = lambda pat: (re.search(pat, t, re.M).group(1) if re.search(pat, t, re.M) else '')
            blk = DA.dig(nm) or ''
            ANIM_INDEX[nm] = {
                'class': r['class'], 'path': r['path'],
                'len': g(r'SequenceLength=([\d.]+)'),
                'frames': g(r'NumFrames=(\d+)'),
                'fps': g(r'ImportFileFramerate=([\d.]+)'),
                'skel': (g(r"Skeleton=Skeleton'\"([^\"]+)\"") or '').split('/')[-1].split('.')[0],
                'notifies': sum(1 for l in blk.split('\n') if '通知[' in l),
                'usedby': seq_users.get(nm, set()),
                'exported': nm in exp_set,
                'trf': nm in trf_set,
            }
    counts = collections.Counter()
    for r in inv:
        p = r['path']
        if not p.startswith('/Game/FlexibleCombatSystem'):
            continue
        rel = p[len('/Game/FlexibleCombatSystem/'):]
        cls = r['class']
        out = None
        if cls in ('Blueprint', 'WidgetBlueprint', 'AnimBlueprint'):
            out = bp_page(r['name'], r)
            sub = os.path.join(DST, rel + '.md')
        elif cls in ('NiagaraSystem', 'NiagaraEmitter'):
            out = vfx_page(r['name'], r)
            sub = os.path.join(DST, rel + '.md')
        elif cls == 'ParticleSystem':
            out = cascade_page(r['name'], r)
            sub = os.path.join(DST, rel + '.md')
        elif cls in ('AnimMontage', 'AnimSequence', 'AnimComposite', 'BlendSpace', 'BlendSpace1D', 'AimOffsetBlendSpace'):
            out = anim_page(r['name'], r)
            sub = os.path.join(DST, rel + '.md')
        elif cls == 'UserDefinedEnum':
            out = enum_page(r['name'], r)
            sub = os.path.join(DST, rel + '.md')
        elif cls == 'UserDefinedStruct':
            out = struct_page(r['name'], r)
            sub = os.path.join(DST, rel + '.md')
        elif cls == 'DataTable':
            out = dt_page(r['name'], r, cells)
            sub = os.path.join(DST, rel + '.md')
        if not out:
            continue
        mk(sub)
        with io.open(sub, 'w', encoding='utf-8') as w:
            w.write(out)
        counts[cls] += 1
    # 索引
    with io.open(os.path.join(DST, 'README.md'), 'w', encoding='utf-8') as w:
        w.write("""# FCS 详情解析（逐资产）

> 由 T3D 导出自动生成，**文件层级镜像 UE 工程 `Content/FlexibleCombatSystem/` 自己的目录结构**，一个资产一个 `.md`。
> 结论文档（怎么用、映射到骑砍2 怎么落）在 [FlexibleCombatSystem施法设计_UE实现分析.md](../FlexibleCombatSystem施法设计_UE实现分析.md)；原始证据（577MB T3D）在 `Debug/offline/fcs_dump/out/t3d/`。

## 覆盖

| 类型 | 数量 | 每页内容 |
|---|---|---|
| 蓝图 / 控件蓝图 / 动画蓝图 | %d | 父类 · 变量（名/类型/CDO 默认值）· 实现接口 · 用到的组件（引用闭包推断）· 调用的函数 · **每个图的伪代码** · **节点引脚明细** · 引用的资产 · 其它默认值 |
| **粒子系统**（Niagara / Cascade） | %d | emitter 逐个 · 渲染器（材质/网格/图集/朝向）· 模块栈（按阶段）· **每个模块参数的实际数值** · 材质解析出的贴图 |
| **动画**（蒙太奇/序列/混合空间） | %d | 长度 · RateScale · **动画通知（名字 + 触发时间 + 时长 + 轨道）** · 段落 · 引用的序列 |
| 枚举 | %d | 有序值表（值 / 显示名 / 内部名） |
| 结构体 | %d | 字段表（序号 / 字段 / 类型 / 子类型 / 默认值） |
| 数据表 | %d | 行 × 列值表（全值） |

## 为什么是 Markdown 不是 XML

- 第一读者是人：要能在 IDE 里直接读、点、搜。XML 只增加仪式感。
- 机读侧车已有：数据表 CSV（`Debug/offline/fcs_dump/out/tables/`）、结构化 JSON（`out/digest/`）、伪代码包（`out/digest/PSEUDOCODE/`）——不重复维护两份结构化数据。
- 保真底稿是 T3D 原文（577MB），本文件夹是**导读层**，不是唯一副本。

## 怎么重新生成

```bash
python Debug/offline/fcs_dump/dump_t3d.py            # ① UE 侧导出 T3D（需无头编辑器）
python Debug/offline/fcs_dump/gen_fcs_detail.py      # ② 生成/刷新本文件夹
```
""" % (counts.get('Blueprint', 0) + counts.get('WidgetBlueprint', 0) + counts.get('AnimBlueprint', 0),
       counts.get('NiagaraSystem', 0) + counts.get('NiagaraEmitter', 0) + counts.get('ParticleSystem', 0),
       counts.get('AnimMontage', 0) + counts.get('AnimSequence', 0) + counts.get('AnimComposite', 0)
       + counts.get('BlendSpace', 0) + counts.get('BlendSpace1D', 0) + counts.get('AimOffsetBlendSpace', 0),
       counts.get('UserDefinedEnum', 0), counts.get('UserDefinedStruct', 0), counts.get('DataTable', 0)))
    # ── Animation/ 总索引：给「提取还原动画」用（一条一条对齐重定向工程的状态）──
    adir = os.path.join(DST, 'Animation')
    if os.path.isdir(adir) and ANIM_INDEX:
        with io.open(os.path.join(adir, 'README.md'), 'w', encoding='utf-8') as w:
            ex = sum(1 for v in ANIM_INDEX.values() if v['exported'])
            tf = sum(1 for v in ANIM_INDEX.values() if v['trf'])
            w.write("# 动画总索引（%d 条）\n\n" % len(ANIM_INDEX))
            w.write("> 给「提取还原动画」用：骨架 / 帧数 / 时长 / 通知数 / 被哪个蒙太奇引用 / **重定向工程是否已导出**。\n")
            w.write("> 重定向工程状态来自 `%s`（设 `UE_RETARGET_ROOT` 可换）；目录不存在时该列显示 ❌。\n\n" % RETARGET_ROOT)
            w.write("**已导出 FBX %d 条 / 已重定向 TRF %d 条 / 本工程共 %d 条**\n\n" % (ex, tf, len(ANIM_INDEX)))
            w.write("| 动画 | 类型 | 时长 | 帧数 | 骨架 | 通知 | 被引用 | 已导出 | 已TRF |\n")
            w.write("|---|---|---|---|---|---|---|---|---|\n")
            for nm in sorted(ANIM_INDEX):
                v = ANIM_INDEX[nm]
                w.write("| [`%s`](%s.md) | %s | %s | %s | %s | %s | %s | %s | %s |\n" % (
                    nm, v['path'].split('/')[-1], v['class'].replace('Anim', ''), v['len'], v['frames'],
                    (v['skel'] or '').replace('_Skeleton', ''), v['notifies'],
                    len(v['usedby']) or '',
                    ('—' if v['class'] == 'AnimMontage' else ('✅' if v['exported'] else '')),
                    ('—' if v['class'] == 'AnimMontage' else ('✅' if v['trf'] else ''))))
    print('DETAIL_DONE', dict(counts))


main()
