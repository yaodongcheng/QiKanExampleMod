# TK5 六剧本全量隶属关系（v3 确定版, 2026-08-31）

## 数据引用导览（去哪里查什么）

六个剧本年代目录：`1554/ 1560/ 1568/ 1575/ 1582/ 1598/`（年代=剧本起始年）。

| 要查什么 | 路径 | 关键列 |
|---|---|---|
| 人物归属（某人当年属谁的势力、身份等级、上司、俸禄/野心/忠诚/家纹） | `{年代}/persons.csv` | person_id=人物号(character id)；force_id/force_name=所属势力；rank=身份等级；superior_id/superior_name=上司；salary/ambition/loyalty/kamon |
| 势力归属（某势力当主是谁、部众多少人） | `{年代}/forces.csv` | force_id/force_name=势力；lord_pid/lord_name=当主；member_count=成员数 |
| 城池归属（某城/町/砦当年城主、属哪家、兵粮金钱粮） | `{年代}/cities.csv` | city_idx=城序号(0-179城/180-245町/246-273里砦)；lord_name=城主；force_id/force_name=归属势力；soldiers/food/gold/train/morale |
| 人物号 ↔ 内存编辑器名单名（同号对照） | `../人物ID对照表_20260901.csv` | ID/character名单名/内存编辑器名单名 |
| 人物号 800-1056 三 AI 识别基准（claudecode 列为准） | `../../人物ID_820_1056_三方识别_20260831.csv` | id/豆包/ds/claudecode |
| 800-1056 段 × 6 年代提取（person 一览） | `person_era_info_20260901.csv` | 截图id/基准名/年代/世界表pid/force/rank/superior/... |

## 生成方法与校验

方法：Snr 解码 → 城表(180条×36B 等距连续, 城主/兵/粮/金直读) + 人物表(36B×1400) + 官方城名单(按城位序号直取)。
城名 = name_official(官方名单) / name_history(历史名事实表 RENAME_FACTS, 史料明确记载的改名)。
无推断：不存在投票/匹配；所有列均为文件直读或事实表 join。

## 🔴 ID 身份声明（2026-09-01 用户裁定 + AppearID 大修正）

**`person_id` = 人物番号**（database.xml / database_dx.xml 人物段，官方空间原版 0-1286 / DX 0-1294 + 本机扩展槽），非内存编辑器显示号、非外观ID。依据：
1. Snr 人物记录 = 36B×id（基址+36×人物ID 公式），信长槽=195 与 database.xml 一致（高亚男截图/城表城主字段交叉自证）。
2. 名字列（显示名）按 EDITOR_NAMES / TaikouHero 映射：🔴 2026-09-01 复核 TaikouHero.外观ID 列（原列名 TK5编号）≠ 人物番号——阿市 外观ID=1049 vs 人物番号 1019（原版）/1181（DX）；列内混身份枚举（忍者/足轻/备大将）。名字列映射 = 旧路径，追加段（800+）需按 database.xml/database_dx.xml 重对齐——**待下轮数据工程，勿再引用本行作权威**。
3. 外观ID = BUSTUP 目录编号（E:\taikou5\TaikouImage\BUSTUP，1292 槽「编号_姓名」）+ character_1292_名单.csv；与人物番号是两套钥匙禁混用；史实段多数同号（195 信长/506 家康/613 风魔）系外观槽顺排巧合而非同一体系。

补充说明：
- 人物表共 **1400 槽（0-1399）**，0 号 = 青山忠成（人物番号 0＝快照槽 0＝外观ID 0 三表同号；2026-09-01 起由 range(0,1400) 读出；先前 range(1,1400) 漏读 0 号，已于同日修正重跑）。
- 1292-1399 为世界表扩展槽（无卡类模板），不在 character 表 0-1291 范围内。
