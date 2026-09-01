# 词表/注册表生成器轮子（扣除法自检 + 塌缩断言，2026-08-27 登记）

> **场景**：手里有一份外部语料（太阁事件包、原版 XML、别人的 mod 数据），要把它翻成我们侧的词表/注册表，并且要能证明「翻全了、没翻重、没翻串」。
> **来源**：2026-08-27 太阁 DSL 翻译总表查漏（`16a-DSL翻译总表.csv` 1481 行）实战产出。当次靠这四条抓到：跨表同词异名 5 组、侧名塌缩 21+1 组、纯数字属性"万能接收器"36 行 / 346 处。
> **现成实现**：`plans/scenario-campaign-mode/tools/`（`gen_registry_tables.py` 规则与自检 / `build_registry_csv.py` 出表 / `registry_residue_scan.py` 扣除法扫描）。**做同类事直接照抄这三个脚本的骨架，不要重写。**

## 一、扣除法自检 —— 证明「翻全了」

**解决什么问题**：正向抽查只能发现「你想得到的那些形态」的缺失。语料里真正的坑是**你压根没想到的形态**（命令裸参数、触发名、BGM 名、容器字段……正向正则根本不去那些位置看）。

**做法（三句话）**：
1. 把语料**复制一份**（🔴 原语料只读，副本 + 报告写进 workdir）
2. 凡是注册表 + 规则 + 名字表能解释的片段，**就地从副本里扣掉**
3. 扣不干净的就是漏洞 —— 残渣 = exit 1，报告落 `workdir/residue_report.txt`

**残渣分两类，处理方式不同**：

| 类 | 是什么 | 怎么办 |
|---|---|---|
| A 未识别形态 | 连词法都没模型的字符（真·未知未知） | 补解析模型（先看懂这是什么语法） |
| B 表外词条 | 形态认得、注册表查不到落点 | 回填映射表（铁律 22：改生成器，不改产物） |

**关键签名**（`registry_residue_scan.py`）：
```python
copy = os.path.join(args.workdir, 'TK5AllEvents_merged.copy.txt')
shutil.copy2(args.source, copy)          # 🔴 原文件只读，全部操作走副本
# 按参数位结构解析，不逐字符猜：命令头 → 头值 → 参数位 → 参数内部 → 台词插值
# 退出码：0 = 零残渣；1 = 有残渣
```

**验收话术**（给审批人看的）：副本 md5 与原语料一致 + 残渣 0 + `git status --short -- <语料目录>` 为空 = 覆盖已证明、语料未动。

## 二、词汇表单一来源 —— 治「同一套词写在两张表里」

**解决什么问题**：同一批词被不同的表各写一遍，写着写着就分叉了。实测病例：`城主間` 在四张表里分别叫 `lord_room` / hash / `lord_room` / `castle_hall`；`主人公軍團` 在两张表里 `main_army` vs `player`。

**做法**：一套词**只在一处定义**，其余表**派生**并各加自己的前缀。

```python
PLACE_TOKENS = {'城主間': 'castle_hall', ...}       # 唯一定义（52 词）
# 設施/背景/場面/決鬥場地 四个域共用同一张表，域前缀不同、token 相同
ENUM_SETS['決鬥場地'] = {p: PLACE_TOKENS[p] for p in DUEL_PLACES}   # 派生，不重抄
```

**纪律**：改词 = 改源表重跑；禁止在派生表或产物 CSV 里单改（铁律 22）。

## 三、孪生词表检测 —— 同词异名生成期报错

**解决什么问题**：单一来源做不到 100%（有些表天然独立），需要一道自动闸门兜住分叉。

```python
def twin_divergences():
    """同一个 TK5 词在两张表里给出不同侧名 → 生成期报错。"""
    # 合法的一词多义写进白名单，不是往产物里打补丁
TWIN_EXEMPT = {'終結', '歸還'}   # 終結=战斗结束/解散军团；歸還=持续方针/一次性命令
```

## 四、侧名塌缩断言 —— 治「两个源词落到同一个我们侧字段」

**解决什么问题**：翻译器分不开、写回会串。实测病例：
- `出現標誌`/`生病標誌`/`死刑標誌`… 7 个独立布尔全叫 `Hero.state`（一个字段存七件事）
- `日數計數器::10/12/14…` 85 个独立计时器全叫 `Time::day`
- `武將２/３/４/５` 编号被规则抹掉，四个槽变一个名

**三条断言（都在生成期跑，命中即 exit 1）**：

| 断言 | 数据源 | 白名单 |
|---|---|---|
| 同域内两个属性共用一个侧名 | `attr_pairs`（语料实测的「域-属性」对） | `ATTR_SYNONYM_EXEMPT` |
| 同域内两个域值共用一个侧名 | `domain_vals` | — |
| 同一枚举里两个词共用一个成员名 | `ENUM_SETS` | — |

```python
def attr_synonym_collisions():
    """返回 [(域, [属性…], 侧名)] —— 同域内多个属性共用一个侧名的塌缩清单。"""
```

**🔴 不算塌缩、别去"修"的三种**：
1. **命令的「我们侧名」= 处理器分组标签**（`se` / `rename` / `05 视觉`）——多条命令共用一个处理器是设计
2. **域的侧名 = 命名空间前缀**（城/町/里/砦/據點 都是 `Settlement::`）——共用合法
3. **跨域同名**（`交易品數量` 与 `所有個数` 都是 `Item.count`）——主体不同，写回不会串；断言只查**同域**

**踩过的坑**：手写映射表里出现**重复键**（同一个词写了两遍，后写的静默赢）是塌缩的隐蔽来源之一 —— 加断言前先查一遍重复键，否则你会以为改了其实没生效。

## 五、"万能接收器"识别 —— 兜底规则让自检形同虚设

**症状**：某条兜底规则能接住**任何**输入（`if attr.isdigit(): return f'{prefix}.attr_{attr}'`），于是这一块永远不报错 —— 自检等于没查。

**判定**：任何 `return` 前不带查表的规则分支，都要问一句「它会不会把不该接的也接了」。

**修法（三类拆分范本）**：语料里捞出**主体**形态，按证据分类，只有查表命中的才放行，其余返回 `None`（= 生成期报错）。

```python
def digit_attr_class(dom, attr, subj=None):
    """纯数字属性位三分类 → ('A'|'B'|'C'|None, 侧名)。表外 = None = 报错。"""
    # A 真属性位  → 查专表 DIGIT_ATTR（19 对，逐条核对语料值空间）
    # B 域值编号后缀 → 主体本身是已登记域值（官位::從三位.15）→ 不是属性，不出行
    # C 转储原始引用 → 主体也是纯数字（環境變量::5270.88）→ 合并成一行说明
```

**收益实测**：属性区 234 → 217 行，删掉的全是跨域拼接的垃圾行（一行 `3` 里塞着 `Item.attr_3 / QuestDef.attr_3 / court_rank.attr_3`）。

**代价（要说清）**：专表是手写的，语料扩充时新出现的形态会在生成期报错、需人工查语义后登记 —— **这正是要的效果**（表外 = 报错，不再静默接住）。

## 六、复跑（三步，都必须 exit 0）

```bash
export PYTHONIOENCODING=utf-8      # 🔴 控制台是 gbk，脚本打 ✅/🔴 会 UnicodeEncodeError
cd <仓库根>                          # 🔴 生成器用相对路径读语料，必须从根跑
python plans/scenario-campaign-mode/tools/gen_registry_tables.py      # 生成期自检（表外/塌缩 = exit 1）
python plans/scenario-campaign-mode/tools/build_registry_csv.py       # 重写产物 CSV
python plans/scenario-campaign-mode/tools/registry_residue_scan.py    # 扣除法残渣扫描（残渣 = exit 1）
```

**产物冗余检查清单**（出表后过一遍，全部应为 0）：整行完全重复 / 同类别+同域+同原词重复 / 词汇类同域侧名塌缩 / 空侧名 / 无例句。

**踩过的坑（省时间用）**：
- Git Bash 的 `grep -oE ".{40}X.{40}"` 按字节算偏移，中日文会被劈成乱码 → 取上下文用 Python `re.finditer` + 切片
- Python 非 raw 字符串里的 `'\b'` 是退格符不是词边界，正则会静默零命中
- 管道会吞掉脚本退出码，判定用 `${PIPESTATUS[0]}`

## 七、数据源读取纪律 —— 找太阁5 数据：CSV 优先，不读 xlsx（2026-08-28 登记）

**解决什么问题**：织丰表是 3.2MB 二进制 xlsx（`Knowledge/太阁5/骑砍2织丰角色ID对应/骑砍2太阁Mod表.xlsx`）——git diff 不可读、openpyxl 解析不了它的样式段（`Fill() takes no arguments`，所以才手扒 zip+XML）、补一列数据只能开 Excel。2026-08-28 用户裁定：**数据层以 CSV 入 git，xlsx 只作上游导入源**。

**三个动作用哪个文件**（记这行就够了）：

| 动作 | 用这个 | 禁止 |
|---|---|---|
| 找/读数据 | `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/*.csv`（一张 sheet 一个 csv，UTF-8 BOM，Excel 可开） | 直接读 xlsx 找数据 |
| 上游数据本身要改 | 改 xlsx → 重跑 `python tools/xlsx_to_csv.py` | 手改镜像 CSV（重跑即覆盖，铁律 22） |
| 人填映射（StringId 补列/地方名对照/别名） | `gen_entity_maps.py` 映射表（`NAME_ALIAS` / `TK5_ONLY_HERO` / `SAME_AS_NEAR` 范式）或独立文件 | 写进镜像 CSV |

**关键文件**：`plans/scenario-campaign-mode/tools/xlsx_to_csv.py` = **唯一**允许解析 xlsx 的脚本（转换 + 逐行回读自检 15/15）；`gen_entity_maps.py` 只读 CSV 镜像（zip 解析段已删）。15 张 sheet = 15 个 csv：TaikouHero（1049×123）/ Clan（605×10）/ Kingdom（135×8）/ Settlements（764×7）/ CityTaikou（180×7，含 MatchSettlement/NearSettlement 两列，语义别混）/ ForceTaikou（204×2，07b §五-2 要补 StringId 列）/ Culture（21×29，地方对照）/ BaseInfo / 演出层 6 张（Appearance/Emotion/Animation/Camera/TagPoint/Music）/ ReadMe。

**回归证据**：新旧管线（xlsx 直读 vs CSV 镜像）产出的 `entity_maps.py` 全部 12 张表逐字典一致、`--report` 输出一字不差——镜像可信，放心读。

**留意**：① TaikouHero.csv 末尾第 123 列「外观描述_光荣」（立绘描述）当前仅 3 行非空（信长/幸村/秀吉样本）；② `ArtSource/update_appearance.py` 是例外路径——它要**写回** xlsx 的外观列（另一条立绘流水线，维持现状），它更新后记得重跑 xlsx_to_csv 刷镜像。
