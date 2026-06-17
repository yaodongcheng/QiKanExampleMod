# 骑马与砍杀2：霸主 控制台指令大全

---

## 通用/帮助 (General/Help)

| 指令 | 说明 |
|------|------|
| `help` | 显示所有可用的控制台指令 |
| `close` | 关闭控制台 |

---

## 角色/单位 (Agent)

| 指令 | 说明 |
|------|------|
| `agent.equip_clear` | 清空指定单位的装备 |
| `agent.goto` | 命令指定单位移动到某个坐标 |

---

## 人工智能 (AI)

| 指令 | 说明 |
|------|------|
| `ai.formation_speed_adjustment_enabled` | 启用/禁用AI阵型速度调整 |

---

## 环境/天气 (Atmosphere)

| 指令 | 说明 |
|------|------|
| `atmosphere.current` | 显示当前天气/环境设置 |
| `atmosphere.list` | 列出所有可用的天气/环境预设 |
| `atmosphere.reset` | 重置天气/环境为默认值 |
| `atmosphere.set_by_index` | 按索引号设置天气/环境 |
| `atmosphere.set_by_name` | 按名称设置天气/环境 |
| `atmosphere.set_interpolation_tod` | 设置一天中时间的插值 |

---

## 性能测试 (Benchmark)

| 指令 | 说明 |
|------|------|
| `benchmark.cpu_benchmark` | 运行CPU性能测试 |
| `benchmark.cpu_benchmark_mission` | 在任务场景中运行CPU性能测试 |

---

## 战役模式 (Campaign)

### 金钱/资源

| 指令 | 说明 |
|------|------|
| `campaign.add_money_to_main_party` | 为主角部队增加金钱 |
| `campaign.add_gold_to_hero` | 为指定英雄增加金钱 |
| `campaign.add_gold_to_all_heroes` | 为所有英雄增加金钱 |
| `campaign.add_influence` | 为主角增加影响力 |
| `campaign.add_renown_to_clan` | 为指定家族增加声望 |
| `campaign.add_all_crafting_materials_to_main_party` | 为主角部队添加所有锻造材料 |
| `campaign.add_crafting_materials` | 添加锻造材料 |

### 角色属性/技能

| 指令 | 说明 |
|------|------|
| `campaign.add_attribute_points_to_hero` | 为指定英雄增加属性点 |
| `campaign.add_focus_points_to_hero` | 为指定英雄增加专注点 |
| `campaign.add_skill_xp_to_hero` | 为指定英雄增加技能经验 |
| `campaign.set_all_skills_main_hero` | 设置主角的所有技能 |
| `campaign.set_skill_main_hero` | 设置主角的某个技能等级 |
| `campaign.set_skills_of_hero` | 设置指定英雄的技能 |
| `campaign.set_all_companion_skills` | 设置所有同伴的技能 |
| `campaign.set_skill_of_all_companions` | 设置所有同伴的技能 |
| `campaign.set_all_heroes_skills` | 设置所有英雄的技能 |
| `campaign.set_main_hero_age` | 设置主角年龄 |
| `campaign.set_hero_trait` | 设置英雄的特质 |
| `campaign.set_hero_culture` | 设置英雄的文化 |
| `campaign.set_hero_crafting_stamina` | 设置英雄的锻造体力 |
| `campaign.set_player_name` | 设置玩家姓名 |
| `campaign.set_player_reputation_trait` | 设置玩家的声望特质 |
| `campaign.reset_player_skills_level_and_perks` | 重置玩家的技能等级和专长 |
| `campaign.print_character_feats` | 打印角色专长 |
| `campaign.print_hero_traits` | 打印英雄特质 |
| `campaign.print_player_traits` | 打印玩家特质 |

### 部队/人员管理

| 指令 | 说明 |
|------|------|
| `campaign.add_troops` | 添加部队 |
| `campaign.add_troops_xp` | 增加部队经验 |
| `campaign.add_companion` | 添加一个同伴 |
| `campaign.add_companions` | 添加多个同伴 |
| `campaign.add_horse` | 为主角添加一匹马 |
| `campaign.add_item_to_main_party` | 为主角部队添加指定物品 |
| `campaign.add_modified_item` | 为主角部队添加一个带有词缀的物品 |
| `campaign.add_morale_to_party` | 为指定部队增加士气 |
| `campaign.add_random_hero_to_party` | 为指定部队添加一个随机英雄 |
| `campaign.add_supporters_for_main_hero` | 为主角增加支持者 |
| `campaign.heal_main_party` | 治愈主角部队 |

### 俘虏

| 指令 | 说明 |
|------|------|
| `campaign.add_prisoner` | 添加一个俘虏 |
| `campaign.add_prisoner_to_party` | 为指定部队添加俘虏 |
| `campaign.add_prisoners_xp` | 增加俘虏的经验值 |
| `campaign.add_random_prisoner_hero` | 添加一个随机英雄作为俘虏 |
| `campaign.kick_capturer_party` | 踢出俘虏部队 |
| `campaign.print_party_prisoners` | 打印部队俘虏 |
| `campaign.print_prisoners` | 打印俘虏 |

### 定居点/建筑

| 指令 | 说明 |
|------|------|
| `campaign.add_building_level` | 为指定定居点增加建筑等级 |
| `campaign.add_progress_to_current_building` | 为当前建筑增加建造进度 |
| `campaign.set_current_building` | 设置当前建筑 |
| `campaign.set_loyalty_of_settlement` | 设置定居点的忠诚度 |
| `campaign.set_settlement_variable` | 设置定居点变量 |
| `campaign.set_settlements_visible` | 使所有定居点可见 |
| `campaign.give_settlement_to_kingdom` | 将定居点给予指定王国 |
| `campaign.give_settlement_to_player` | 将定居点给予玩家 |
| `campaign.give_workshop_to_player` | 将一个工坊给予玩家 |
| `campaign.clear_settlement_defense` | 清除定居点的防御 |
| `campaign.remove_militas_from_settlement` | 从定居点移除民兵 |
| `campaign.add_power_to_notable` | 增加名士的权势 |

### 王国/外交

| 指令 | 说明 |
|------|------|
| `campaign.create_player_kingdom` | 创建玩家自己的王国 |
| `campaign.join_kingdom` | 加入一个王国 |
| `campaign.join_kingdom_as_mercenary` | 作为雇佣兵加入一个王国 |
| `campaign.leave_faction` | 离开当前派系 |
| `campaign.lead_your_faction` | 领导你的派系 |
| `campaign.declare_war` | 宣战 |
| `campaign.declare_peace` | 宣布和平 |
| `campaign.start_player_vs_world_war` | 开启玩家与全世界的战争 |
| `campaign.start_player_vs_world_truce` | 开启玩家与全世界的休战 |
| `campaign.start_world_war` | 开启世界大战 |
| `campaign.start_world_peace` | 开启世界和平 |
| `campaign.activate_all_policies_for_player_kingdom` | 为玩家王国激活所有政策 |
| `campaign.set_clan_culture` | 设置家族文化 |
| `campaign.create_random_clan` | 创建一个随机家族 |
| `campaign.rebellion_enabled` | 启用/禁用叛乱 |
| `campaign.print_strength_of_factions` | 打印派系实力 |
| `campaign.print_influence_change_of_clan` | 打印家族影响力变化 |

### AI/部队行为

| 指令 | 说明 |
|------|------|
| `campaign.ai_attack_party` | 命令AI攻击指定部队 |
| `campaign.ai_defend_settlement` | 命令AI防守指定定居点 |
| `campaign.ai_goto_settlement` | 命令AI前往指定定居点 |
| `campaign.ai_raid_village` | 命令AI袭击村庄 |
| `campaign.ai_siege_settlement` | 命令AI围攻定居点 |
| `campaign.control_party_ai_by_cheats` | 使用作弊码控制部队AI |
| `campaign.boost_cohesion_of_all_armies` | 提升所有军团的凝聚力 |
| `campaign.boost_cohesion_of_army` | 提升指定军团的凝聚力 |
| `campaign.set_main_party_attackable` | 设置主角部队是否可被攻击 |
| `campaign.set_armies_and_parties_visible` | 使所有军团和部队可见 |

### 英雄/NPC管理

| 指令 | 说明 |
|------|------|
| `campaign.kill_hero` | 杀死指定英雄 |
| `campaign.make_hero_fugitive` | 使英雄成为逃犯 |
| `campaign.make_hero_wounded` | 使英雄受伤 |
| `campaign.make_main_hero_ill` | 使主角生病 |
| `campaign.conceive_child` | 使英雄怀孕生子 |
| `campaign.marry_player_with_hero` | 让玩家与指定英雄结婚 |
| `campaign.is_hero_suitable_for_marriage_with_player` | 检查英雄是否适合与玩家结婚 |
| `campaign.print_heroes_suitable_for_marriage` | 打印适合结婚的英雄 |
| `campaign.add_hero_relation` | 增加与指定英雄的关系 |

### 锻造

| 指令 | 说明 |
|------|------|
| `campaign.unlock_all_crafting_pieces` | 解锁所有锻造部件 |

### 任务/事件

| 指令 | 说明 |
|------|------|
| `campaign.cancel_quest` | 取消任务 |
| `campaign.print_all_issues` | 打印所有问题/任务 |
| `campaign.print_issues` | 打印问题/任务 |
| `campaign.spawn_new_alley_attack` | 生成新的小巷攻击事件 |
| `campaign.win_board_game` | 赢得棋盘游戏 |

### 聚焦/定位

| 指令 | 说明 |
|------|------|
| `campaign.focus_hero` | 聚焦到指定英雄 |
| `campaign.focus_hostile_army` | 聚焦到敌对军团 |
| `campaign.focus_infested_hideout` | 聚焦到被侵占的藏身处 |
| `campaign.focus_issue` | 聚焦到问题/任务 |
| `campaign.focus_mobile_party` | 聚焦到移动部队 |
| `campaign.focus_tournament` | 聚焦到锦标赛 |

### 其他

| 指令 | 说明 |
|------|------|
| `campaign.set_campaign_speed_multiplier` | 设置大地图时间流逝速度 |
| `campaign.set_custom_maximum_map_height` | 设置自定义最大地图高度 |
| `campaign.set_criminal_rating` | 设置犯罪等级 |
| `campaign.show_hideouts` | 显示所有藏身处 |
| `campaign.hide_hideouts` | 隐藏所有藏身处 |
| `campaign.toggle_information_restrictions` | 切换信息限制 |
| `campaign.remove_all_circle_notifications` | 移除所有圆形通知 |
| `campaign.refresh_battle_scene_index_map` | 刷新战斗场景索引图 |
| `campaign.print_criminal_ratings` | 打印犯罪等级 |
| `campaign.print_gameplay_statistics` | 打印游戏统计数据 |
| `campaign.print_main_party_position` | 打印主角部队位置 |
| `campaign.print_strength_of_lord_parties` | 打印领主部队实力 |
| `campaign.print_tournaments` | 打印锦标赛信息 |

---

## 游戏设置 (Config)

| 指令 | 说明 |
|------|------|
| `cheat_mode` | 开启/关闭作弊模式 |
| `config.ai_quality` | 设置AI质量 |
| `config.animation_sampling_quality` | 设置动画采样质量 |
| `config.antialiasing_technique` | 设置抗锯齿技术 |
| `config.brightness` | 设置亮度 |
| `config.character_detail` | 设置角色细节等级 |
| `config.decal_quality` | 设置贴花质量 |
| `config.disable_sound` | 禁用声音 |
| `config.display_height` | 设置显示高度 |
| `config.display_width` | 设置显示宽度 |
| `config.display_mode` | 设置显示模式 |
| `config.display_refresh_rate` | 设置刷新率 |
| `config.dlss_technique` | 设置DLSS技术 |
| `config.environment_detail` | 设置环境细节 |
| `config.foliage_quality` | 设置植被质量 |
| `config.gamma` | 设置伽马值 |
| `config.lighting_quality` | 设置光照质量 |
| `config.master_volume` | 设置主音量 |
| `config.max_framerate` | 设置最大帧率 |
| `config.particle_detail` | 设置粒子细节 |
| `config.particle_quality` | 设置粒子质量 |
| `config.postfx_...` | 设置后期处理效果（辉光、景深、动态模糊等） |
| `config.resolution_scale` | 设置渲染分辨率缩放 |
| `config.shader_quality` | 设置着色器质量 |
| `config.shadowmap_...` | 设置阴影质量/类型 |
| `config.sound_...` | 设置声音相关选项 |
| `config.terrain_quality` | 设置地形质量 |
| `config.tesselation` | 设置曲面细分 |

---

## 调试 (Debug)

| 指令 | 说明 |
|------|------|
| `debug.check_all_scenes_for_problems` | 检查所有场景是否存在问题 |
| `debug.crash_test` | 进行崩溃测试 |
| `debug.print_...` | 打印各种调试信息（材质、网格、纹理等） |

---

## 游戏核心 (Game)

| 指令 | 说明 |
|------|------|
| `game.reload_...` | 重新加载游戏资源（动画、物品、参数等） |
| `game.pause` | 暂停游戏 |

---

## 任务/战斗场景 (Mission)

| 指令 | 说明 |
|------|------|
| `mission.fix_camera_toggle` | 切换固定视角 |
| `mission.flee_enemies` | 使敌人逃跑 |
| `mission.flee_team` | 使队伍逃跑 |
| `mission.kill_agent` | 击杀指定单位 |
| `mission.set_battering_ram_speed` | 设置冲车速度 |
| `mission.set_siege_tower_speed` | 设置攻城塔速度 |
| `mission.toggleDisableDying` | 切换无敌/不会死亡模式 |
| `mission_cpp.kill_all_agents` | 击杀所有单位 |
| `mission_cpp.kill_all_agents_excluding_this` | 击杀除当前单位外的所有单位 |
| `mission_cpp.remove_all_corpses` | 移除所有尸体 |

---

## 多人模式 (Multiplayer)

| 指令 | 说明 |
|------|------|
| `mp_host.end_warmup` | 结束热身阶段 |
| `mp_host.kill_player` | 击杀玩家 |
| `mp_perks.raise_event` | 触发多人perk事件 |
| `mp_perks.tick_perks` | 更新多人perk状态 |

---

## 资源/引擎 (Resource/Engine)

| 指令 | 说明 |
|------|------|
| `resource.…` | 管理游戏资源和着色器 |
| `rgl_module_ini_options.…` | 底层引擎模块设置 |
| `gfx.set_quality` | 设置图形质量 |
| `memory.…` | 显示内存使用情况 |
| `localization.…` | 管理游戏语言和文本 |

---

> **使用提示**：在游戏中按 `Alt + ~` 打开控制台（需要在启动器中开启作弊模式或在config中设置 `cheat_mode = 1`），输入上述指令即可使用。部分指令需要附带参数，输入指令后按空格会有提示。