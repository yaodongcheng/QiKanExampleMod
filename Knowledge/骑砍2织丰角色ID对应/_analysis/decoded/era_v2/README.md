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

## 🔴 ID 身份声明（2026-09-01 用户裁定）

**`person_id` = character id（人物数据库号）**，非内存编辑器显示号。依据：
1. 名字列按织丰 TaikouHero.TK5编号 同号映射（TK5编号 = character 表体系；613=风魔小太郎、1049=阿市 三源一致：TK5编号/立绘目录/Snr）。
2. 0-799 段内存编辑器显示名与 character 表名肉眼一致（2026-09-01 用户确认）。
3. 内存编辑器名单（修改器截图繁体名）与 character 表名 = 同一批人的两套显示名，同号同人；对号见 ../人物ID对照表_20260901.csv。

补充说明：
- 人物表共 **1400 槽（0-1399）**，0 号 = 青山忠成（TK5编号 0，2026-09-01 起由 range(0,1400) 读出；先前 range(1,1400) 漏读 0 号，已于同日修正重跑）。
- 1292-1399 为世界表扩展槽（无卡类模板），不在 character 表 0-1291 范围内。
