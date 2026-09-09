# 场景 Tag 清单（生成物，禁止手改）

> 生成脚本：`tools/gen_scene_tag_list.py` ｜ 数据源：`Modules/*/SceneObj/*/scene.xscene`

> **Tag = 场景文件 `scene.xscene` 里 `game_entity` 上的 `<tag name>`**——一个 `.xscene` 一套 tag，任何 settlement/location 调用同一场景看到的 tag 完全相同；差异化只看 location 的繁荣度/人口（往各 tag 塞多少人/谁）。

## 0. 引擎消费的 tag 家族（分类总表）

| 家族 | tag | 用途 |
|---|---|---|
| 玩家出生 | sp_player |（场景内出现 0 次）|
| 玩家出生 | sp_player_conversation |（场景内出现 5 次）|
| 静态可交互贵族位 | sp_notable |（场景内出现 209 次）|
| 静态可交互贵族位 | sp_throne |（场景内出现 1 次）|
| 静态可交互贵族位 | sp_hermit |（场景内出现 0 次）|
| 静态可交互贵族位 | sp_arena |（场景内出现 97 次）|
| 静态可交互贵族位 | sp_tavern_wench |（场景内出现 2 次）|
| 守卫/兵 | sp_guard |（场景内出现 80 次）|
| 守卫/兵 | sp_guard_unarmed |（场景内出现 446 次）|
| 守卫/兵 | sp_guard_with_spear |（场景内出现 0 次）|
| 守卫/兵 | sp_guard_castle |（场景内出现 0 次）|
| 攻城驻防 | sp_defender_infantry_lords_hall |（场景内出现 0 次）|
| 攻城驻防 | sp_defender_archer_lords_hall |（场景内出现 0 次）|
| 随机平民出生 | npc_common |（场景内出现 1108 次）|
| 随机平民出生 | npc_common_limited |（场景内出现 103 次）|
| 随机平民出生 | npc_idle |（场景内出现 41 次）|
| 随机平民出生 | npc_wait |（场景内出现 1382 次）|
| 随机平民出生 | npc_drinker |（场景内出现 53 次）|
| 随机平民出生 | npc_beggar |（场景内出现 10 次）|
| 随机平民出生 | npc_passage |（场景内出现 0 次）|
| 情景人偶组（预摆布景） | sp_notable_hangout_set |（场景内出现 0 次）|
| 情景人偶组（预摆布景） | sp_npc_argue_set |（场景内出现 0 次）|
| 情景人偶组（预摆布景） | sp_npc_argument_trio |（场景内出现 0 次）|
| 情景人偶组（预摆布景） | sp_notable_instructing_with_listeners |（场景内出现 0 次）|
| 情景人偶组（预摆布景） | sp_notable_giving_order |（场景内出现 0 次）|
| 情景人偶组（预摆布景） | sp_battle_set |（场景内出现 0 次）|
| 通道脚本 | passage |（场景内出现 0 次）|

## 1. 织丰场景 → tag 清单

| 场景 | tag 数 | tag 列表（次数） |
|---|---|---|
| Main_map | 27 | village×398 castle×140 town×39 port×34 siege_preparation×27 map_breachable_wall×12 map_broken_wall×12 map_solid_wall×12 map_banner_placeholder×11 main_map_city_gate×11 map_defensive_engine_3×6 map_defensive_engine_0×6 map_defensive_engine_1×6 map_defensive_engine_2×6 map_camp_area_2×5 bo_town×4 map_siege_tower×4 ocean_gate×3 land_gate×3 map_settlement_circle×2 map_camp_area_1×2 map_siege_engine_3×2 map_siege_engine_2×2 map_siege_engine_1×2 map_siege_engine_0×2 map_siege_ram×2 main_map_castle_gate×1 |
| Naval_navmesh_template | 0 |  |
| banner_editor_scene | 3 | spawnpoint_player×1 banner×1 player_camera×1 |
| character_menu_new | 10 | sp_npc×2 spawnpoint_mount_2×1 spawnpoint_brother_brother_stage×1 spawnpoint_player_2×1 spawnpoint_mount_1×1 spawnpoint_mount_3×1 spawnpoint_player_3×1 spawnpoint_player_brother_stage×1 spawnpoint_player_1×1 debug_head×1 |
| conversation_scene_naval | 9 | Archer×63 water_trail×4 clean×1 boat×1 operational×1 Wait×1 PilotCaptain×1 Pilot×1 colliding_box×1 |
| crafting_menu_outdoor | 3 | weapon_point×1 camera_instance×1 camera_point×1 |
| dojo_test | 0 |  |
| duel_test | 12 | camera_instance×4 npc_common_limited×2 npc_wait×2 npc_common×2 sho_duel_set×2 duel_defender×2 duel_attacker×2 customcamera×2 returncamera×2 duel_parent×1 spawner_duelist_npc×1 sp_player_near_duelist×1 |
| main_menu_a | 0 |  |
| main_menu_b | 0 |  |
| meeting_castle_sho_a | 8 | player_bodyguard_infantry_spawn×2 opponent_bodyguard_infantry_spawn×2 binary_conversation_point×1 camera_parent×1 player_infantry_spawn×1 player_bannerbearer_infantry_spawn×1 opponent_infantry_spawn×1 opponent_bannerbearer_infantry_spawn×1 |
| scn_become_king_notification | 24 | npc_wait×20 spawnpoint_player_12×1 spawnpoint_player_11×1 spawnpoint_player_13×1 spawnpoint_player_1×1 customcamera×1 camera_instance×1 spawnpoint_player_21×1 spawnpoint_player_20×1 debug_head×1 spawnpoint_player_8×1 spawnpoint_player_6×1 spawnpoint_player_2×1 spawnpoint_player_15×1 spawnpoint_player_19×1 spawnpoint_player_18×1 spawnpoint_player_17×1 spawnpoint_player_16×1 spawnpoint_player_7×1 spawnpoint_player_14×1 spawnpoint_player_5×1 spawnpoint_player_9×1 spawnpoint_player_10×1 spawnpoint_player_3×1 |
| scn_born_baby | 7 | npc_wait×3 npc_common×3 spawnpoint_player_3×1 spawnpoint_player_1×1 customcamera×1 camera_instance×1 spawnpoint_player_2×1 |
| scn_born_baby_female_hero | 7 | npc_wait×3 npc_common×3 spawnpoint_player_1×1 spawnpoint_player_2×1 customcamera×1 camera_instance×1 spawnpoint_player_3×1 |
| scn_born_baby_female_hero2 | 7 | npc_wait×3 npc_common×3 spawnpoint_player_1×1 spawnpoint_player_2×1 customcamera×1 camera_instance×1 spawnpoint_player_3×1 |
| scn_crowning_notification | 4 | spawnpoint_player_1×1 customcamera×1 camera_instance×1 debug_head×1 |
| scn_cutscene_death_old_age | 10 | spawnpoint_player_2×1 spawnpoint_player_3×1 spawnpoint_player_1×1 debug_head×1 customcamera×1 camera_instance×1 spawnpoint_player_4×1 spawnpoint_player_7×1 spawnpoint_player_6×1 spawnpoint_player_5×1 |
| scn_cutscene_enemykingdom_destroyed | 5 | customcamera×1 camera_instance×1 debug_head×1 banner_settlement×1 banner_1×1 |
| scn_cutscene_factionjoin | 10 | envmap_probe×1 spawnpoint_player_6×1 spawnpoint_player_5×1 spawnpoint_player_4×1 customcamera×1 camera_instance×1 spawnpoint_player_2×1 spawnpoint_player_3×1 spawnpoint_player_1×1 debug_head×1 |
| scn_cutscene_family_member_death | 9 | customcamera×1 camera_instance×1 spawnpoint_player_2×1 spawnpoint_player_1×1 spawnpoint_player_4×1 spawnpoint_player_3×1 spawnpoint_player_5×1 debug_head×1 spawnpoint_player_6×1 |
| scn_cutscene_family_member_death_war | 9 | customcamera×1 camera_instance×1 spawnpoint_player_4×1 debug_head×1 spawnpoint_player_6×1 spawnpoint_player_2×1 spawnpoint_player_3×1 spawnpoint_player_1×1 spawnpoint_player_5×1 |
| scn_cutscene_heir_coming_of_age | 8 | npc_wait×4 npc_common×4 spawnpoint_player_2×1 spawnpoint_player_3×1 customcamera×1 camera_instance×1 spawnpoint_player_4×1 spawnpoint_player_1×1 |
| scn_cutscene_main_hero_battle_death | 32 | npc_wait×24 npc_common×24 dead_body_blood×15 spawnpoint_player_7×1 spawnpoint_player_2×1 debug_head×1 spawnpoint_player_3×1 spawnpoint_player_19×1 spawnpoint_player_22×1 spawnpoint_player_11×1 spawnpoint_player_6×1 spawnpoint_player_20×1 spawnpoint_player_4×1 spawnpoint_player_24×1 spawnpoint_player_5×1 spawnpoint_player_18×1 dead_body×1 attacker×1 spawnpoint_player_12×1 spawnpoint_player_10×1 spawnpoint_player_14×1 spawnpoint_player_13×1 spawnpoint_player_15×1 spawnpoint_player_23×1 spawnpoint_player_8×1 customcamera×1 camera_instance×1 spawnpoint_player_1×1 spawnpoint_player_21×1 spawnpoint_player_17×1 spawnpoint_player_16×1 spawnpoint_player_9×1 |
| scn_cutscene_main_hero_battle_victory_death | 14 | dead_body_blood×12 npc_wait×6 npc_common×6 spawnpoint_player_1×1 customcamera×1 camera_instance×1 dead_body×1 attacker×1 spawnpoint_player_6×1 debug_head×1 spawnpoint_player_3×1 spawnpoint_player_2×1 spawnpoint_player_4×1 spawnpoint_player_5×1 |
| scn_cutscene_rebellion_started | 6 | banner_settlement×2 customcamera×1 camera_instance×1 banner_1×1 banner_2×1 debug_head×1 |
| scn_cutscene_test | 3 | customcamera×1 camera_instance×1 debug_head×1 |
| scn_cutscene_wedding | 16 | npc_wait×9 npc_common×9 sh_border_tag×2 spawnpoint_player_3×1 spawnpoint_player_7×1 customcamera×1 camera_instance×1 debug_head×1 spawnpoint_player_9×1 spawnpoint_player_8×1 spawnpoint_player_1×1 spawnpoint_player_2×1 spawnpoint_player_6×1 spawnpoint_player_5×1 envmap_probe×1 spawnpoint_player_4×1 |
| scn_execution_notification | 5 | debug_head×1 spawnpoint_player_2×1 spawnpoint_player_1×1 customcamera×1 camera_instance×1 |
| scn_hero_come_of_age_female | 8 | npc_wait×4 npc_common×4 customcamera×1 camera_instance×1 spawnpoint_player_2×1 spawnpoint_player_4×1 spawnpoint_player_1×1 spawnpoint_player_3×1 |
| scn_kingdom_made | 10 | npc_wait×6 npc_common×6 spawnpoint_player_5×1 spawnpoint_player_2×1 spawnpoint_player_4×1 spawnpoint_player_3×1 customcamera×1 camera_instance×1 spawnpoint_player_1×1 spawnpoint_player_6×1 |
| scn_new | 0 |  |
| ship_visulization_test | 5 | boat×5 machine_parent×5 ship×5 tracked×1 target×1 |
| sho_arena_a | 9 | sp_arena×4 tournament_fight×1 arena_set×1 sp_arena_1×1 sp_arena_2×1 sp_arena_4×1 sp_arena_3×1 arena_sound×1 camera_instance×1 |
| sho_arena_b | 10 | sp_arena_spectator×434 outer_barrier×9 sp_arena×8 sp_arena_default×6 tournament_fight×3 tournament_practice×3 small×1 large×1 medium×1 arena_sound×1 |
| sho_arena_b_backup | 5 | sp_arena×2 sp_arena_respawn×2 tournament_practice×1 arena_set×1 arena_sound×1 |
| sho_arena_backup | 11 | sp_arena×4 Pilot×3 can_pick_up_ammo×3 camera_instance×1 tournament_fight×1 arena_set×1 sp_arena_1×1 sp_arena_2×1 sp_arena_4×1 sp_arena_3×1 arena_sound×1 |
| sho_bandit_hideout_a | 0 |  |
| sho_bandit_hideout_b | 0 |  |
| sho_bandit_hideout_c | 0 |  |
| sho_bandit_hideout_e | 1 | door×2 |
| sho_bandit_hideout_e_gingko | 0 |  |
| sho_battle_bridge_a | 0 |  |
| sho_battle_coast_a | 0 |  |
| sho_battle_farm_a | 0 |  |
| sho_battle_farm_b | 0 |  |
| sho_battle_farm_c | 0 |  |
| sho_battle_farm_d | 0 |  |
| sho_battle_foothills_a | 0 |  |
| sho_battle_foothills_b | 0 |  |
| sho_battle_foothills_c | 0 |  |
| sho_battle_foothills_d | 0 |  |
| sho_battle_foothills_e | 0 |  |
| sho_battle_forest_a | 0 |  |
| sho_battle_forest_b | 0 |  |
| sho_battle_forest_c | 0 |  |
| sho_battle_forest_d | 0 |  |
| sho_battle_island_a | 0 |  |
| sho_battle_plain_a | 0 |  |
| sho_battle_plain_b | 0 |  |
| sho_battle_plain_c | 0 |  |
| sho_battle_plain_d | 0 |  |
| sho_battle_plain_e | 0 |  |
| sho_battle_plain_f | 0 |  |
| sho_battle_plain_g | 0 |  |
| sho_battle_plain_h | 0 |  |
| sho_battle_plain_i | 0 |  |
| sho_battle_plain_j | 0 |  |
| sho_battle_plain_k | 0 |  |
| sho_battle_site_a | 8 | attacker×30 looting_spawn×5 sp_visual_3×5 sp_visual_5×5 sp_visual_0×5 sp_visual_2×5 sp_visual_1×5 sp_visual_4×5 |
| sho_battle_site_b | 9 | attacker×36 looting_spawn×6 sp_visual_3×6 sp_visual_5×6 sp_visual_0×6 sp_visual_2×6 sp_visual_1×6 sp_visual_4×6 spawnpoint_player×6 |
| sho_battle_site_c | 0 |  |
| sho_battle_site_d | 0 |  |
| sho_battle_valley_a | 0 |  |
| sho_battle_valley_b | 0 |  |
| sho_battle_valley_c | 0 |  |
| sho_battle_valley_d | 0 |  |
| sho_castle_map_a | 0 |  |
| sho_castle_map_b | 63 | defender×116 sho_archer_enforcer×102 0×30 reference×19 gate×15 npc_wait×14 sp_guard_unarmed×14 banner_editable×14 collider_l×12 collider_r×12 extra_collider_l×12 extra_collider_r×12 close×6 outside×6 middle_pos×6 wait_pos×6 1×6 2×6 3×6 operational×4 attacker_wait_pos×4 open×3 collider_agent_l×3 state_0×3 plank×3 state_1×3 state_2×3 state_3×3 state_4×3 collider_agent_r×3 archer_position×3 burned_part×2 heavy_hit×2 inner_gate×2 solid_child×2 broken_child×2 outer_gate×1 strategycameradefender×1 wall_left×1 battle_set×1 spawnpoint_set×1 attacker_archer_reinforcement×1 attacker_ranged_reinforcement×1 attacker_cavalry_reinforcement×1 attacker_horsearcher_reinforcement×1 attacker_infantry_reinforcement×1 defender_archer_reinforcement×1 defender_ranged_reinforcement×1 defender_cavalry_reinforcement×1 defender_horsearcher_reinforcement×1 defender_infantry_reinforcement×1 attacker_archer×1 attacker_ranged×1 attacker_cavalry×1 attacker_horsearcher×1 attacker_infantry×1 defender_infantry×1 defender_horsearcher×1 defender_cavalry×1 defender_archer×1 defender_ranged×1 strategycameraattacker×1 wall_right×1 |
| sho_castle_map_c | 36 | sho_archer_enforcer×105 defender×105 reference×17 0×10 gate×5 middle_pos×5 wait_pos×5 operational×4 collider_l×4 collider_r×4 extra_collider_l×4 extra_collider_r×4 attacker_wait_pos×4 burned_part×3 heavy_hit×3 close×2 outside×2 1×2 2×2 3×2 solid_child×2 broken_child×2 inner_gate×1 open×1 collider_agent_l×1 state_0×1 plank×1 state_1×1 state_2×1 state_3×1 state_4×1 collider_agent_r×1 strategycameraattacker×1 strategycameradefender×1 wall_right×1 wall_left×1 |
| sho_castle_map_d | 38 | sho_archer_enforcer×124 defender×124 reference×30 banner_editable×24 banner_settlement×24 0×20 gate×10 collider_l×8 collider_r×8 extra_collider_l×8 extra_collider_r×8 operational×7 middle_pos×6 wait_pos×6 heavy_hit×5 burned_part×4 close×4 outside×4 1×4 2×4 3×4 attacker_wait_pos×4 inner_gate×2 open×2 collider_agent_l×2 state_0×2 plank×2 state_1×2 state_2×2 state_3×2 state_4×2 collider_agent_r×2 solid_child×2 broken_child×2 strategycameradefender×1 strategycameraattacker×1 wall_right×1 wall_left×1 |
| sho_castle_map_e | 34 | sho_archer_enforcer×85 defender×85 0×20 gate×10 reference×10 collider_l×8 collider_r×8 extra_collider_l×8 extra_collider_r×8 middle_pos×6 wait_pos×6 close×4 outside×4 1×4 2×4 3×4 attacker_wait_pos×4 inner_gate×2 open×2 collider_agent_l×2 operational×2 state_0×2 plank×2 state_1×2 state_2×2 state_3×2 state_4×2 collider_agent_r×2 solid_child×2 broken_child×2 wall_right×1 strategycameraattacker×1 strategycameradefender×1 wall_left×1 |
| sho_castle_map_f | 1 | inner_gate×1 |
| sho_castle_map_g | 0 |  |
| sho_castle_map_h | 32 | 0×80 gate×40 reference×40 collider_l×32 collider_r×32 extra_collider_l×32 extra_collider_r×32 close×16 outside×16 1×16 2×16 3×16 middle_pos×12 wait_pos×12 inner_gate×8 open×8 collider_agent_l×8 operational×8 state_0×8 plank×8 state_1×8 state_2×8 state_3×8 state_4×8 collider_agent_r×8 attacker_wait_pos×4 solid_child×2 broken_child×2 strategycameraattacker×1 strategycameradefender×1 wall_left×1 wall_right×1 |
| sho_castle_map_i | 34 | sho_archer_enforcer×164 defender×164 0×40 reference×27 gate×20 collider_l×16 collider_r×16 extra_collider_l×16 extra_collider_r×16 operational×10 middle_pos×8 wait_pos×8 close×8 outside×8 1×8 2×8 3×8 attacker_wait_pos×4 inner_gate×4 open×4 collider_agent_l×4 state_0×4 plank×4 state_1×4 state_2×4 state_3×4 state_4×4 collider_agent_r×4 broken_child×2 solid_child×2 wall_right×1 strategycameraattacker×1 wall_left×1 strategycameradefender×1 |
| sho_castle_map_j | 34 | sho_archer_enforcer×126 defender×126 0×20 gate×10 reference×10 collider_l×8 collider_r×8 extra_collider_l×8 extra_collider_r×8 middle_pos×6 wait_pos×6 close×4 outside×4 1×4 2×4 3×4 attacker_wait_pos×4 inner_gate×2 open×2 collider_agent_l×2 operational×2 state_0×2 plank×2 state_1×2 state_2×2 state_3×2 state_4×2 collider_agent_r×2 solid_child×2 broken_child×2 strategycameradefender×1 strategycameraattacker×1 wall_left×1 wall_right×1 |
| sho_castle_map_k | 34 | sho_archer_enforcer×126 defender×126 0×10 wait_pos×5 middle_pos×5 gate×5 reference×5 attacker_wait_pos×4 collider_l×4 collider_r×4 extra_collider_l×4 extra_collider_r×4 solid_child×2 broken_child×2 close×2 outside×2 1×2 2×2 3×2 wall_left×1 wall_right×1 strategycameradefender×1 strategycameraattacker×1 inner_gate×1 open×1 collider_agent_l×1 operational×1 state_0×1 plank×1 state_1×1 state_2×1 state_3×1 state_4×1 collider_agent_r×1 |
| sho_city_map_icon | 20 | siege_preparation×9 map_breachable_wall×6 map_broken_wall×6 map_solid_wall×6 map_defensive_engine_3×3 map_defensive_engine_0×3 map_defensive_engine_1×3 map_defensive_engine_2×3 map_settlement_circle×2 map_camp_area_2×2 map_siege_tower×2 bo_town×1 main_map_city_gate×1 map_camp_area_1×1 map_siege_engine_3×1 map_siege_engine_2×1 map_siege_engine_1×1 map_siege_engine_0×1 map_siege_ram×1 map_banner_placeholder×1 |
| sho_city_test | 6 | middle_pos×2 wait_pos×2 wall_right×1 wall_left×1 strategycameraattacker×1 strategycameradefender×1 |
| sho_duel_map_a | 0 |  |
| sho_duel_map_b | 11 | sho_duel_set×3 duel_defender×3 duel_attacker×3 customcamera×3 camera_instance×3 npc_common_limited×2 npc_wait×2 npc_common×2 duel_parent×1 spawner_duelist_npc×1 sp_player_near_duelist×1 |
| sho_duel_map_c | 0 |  |
| sho_feature_testing_a | 0 |  |
| sho_keep_scene | 9 | npc_wait×30 npc_common×29 sp_notable×29 npc_drinker×28 npc_idle×1 sp_throne×1 gambler_npc×1 reserved×1 gambler_player×1 |
| sho_naval_empty_test_boardingtest | 2 | defender_ship_spawns×1 attacker_ship_spawns×1 |
| sho_naval_empty_test_nonavmesh | 2 | defender_ship_spawns×1 attacker_ship_spawns×1 |
| sho_naval_open_ocean | 4 | defender_ship_spawns×1 defender_ship_spawns_short×1 attacker_ship_spawns_short×1 attacker_ship_spawns×1 |
| sho_naval_test | 0 |  |
| sho_postbattle_test | 0 |  |
| sho_prefab_making_scene | 0 |  |
| sho_prefab_propstuff | 0 |  |
| sho_prison_a | 0 |  |
| sho_prison_b | 5 | npc_wait×3 npc_common×3 sp_prison_guard×3 npc_idle×1 npc_drinker×1 |
| sho_tavern_a | 8 | npc_wait×19 npc_common×19 npc_drinker×8 musician×3 npc_idle×2 gambler_npc×1 gambler_player×1 reserved×1 |
| sho_testscene | 0 |  |
| sho_town_a | 33 | sho_archer_enforcer×139 defender×139 0×20 gate×10 reference×10 wait_pos×10 collider_l×8 collider_r×8 extra_collider_l×8 extra_collider_r×8 middle_pos×6 close×4 outside×4 1×4 2×4 3×4 inner_gate×2 open×2 collider_agent_l×2 operational×2 state_0×2 plank×2 state_1×2 state_2×2 state_3×2 state_4×2 collider_agent_r×2 solid_child×2 broken_child×2 strategycameraattacker×1 wall_right×1 strategycameradefender×1 wall_left×1 |
| sho_town_b | 34 | sho_archer_enforcer×170 defender×170 0×20 gate×10 reference×10 collider_l×8 collider_r×8 extra_collider_l×8 extra_collider_r×8 middle_pos×6 wait_pos×6 close×4 outside×4 1×4 2×4 3×4 attacker_wait_pos×4 inner_gate×2 open×2 collider_agent_l×2 operational×2 state_0×2 plank×2 state_1×2 state_2×2 state_3×2 state_4×2 collider_agent_r×2 broken_child×2 solid_child×2 strategycameradefender×1 strategycameraattacker×1 wall_left×1 wall_right×1 |
| sho_town_c | 34 | sho_archer_enforcer×191 defender×191 0×20 gate×10 reference×10 collider_l×8 collider_r×8 extra_collider_l×8 extra_collider_r×8 wait_pos×6 middle_pos×6 attacker_wait_pos×4 close×4 outside×4 1×4 2×4 3×4 solid_child×2 broken_child×2 inner_gate×2 open×2 collider_agent_l×2 operational×2 state_0×2 plank×2 state_1×2 state_2×2 state_3×2 state_4×2 collider_agent_r×2 wall_right×1 strategycameradefender×1 strategycameraattacker×1 wall_left×1 |
| sho_town_c_test | 0 |  |
| sho_update | 0 |  |
| sho_village_a | 0 |  |
| sho_village_b | 0 |  |
| sho_village_c | 1 | npc_common×3 |
| sho_village_d | 8 | npc_common×48 sp_rural_notable_notary×16 npc_common_limited×10 sp_guard×6 common_area_marker×1 npc_wait×1 sp_notable×1 food_loot_major×1 |
| sho_village_e | 0 |  |
| sho_village_e_raid | 13 | npc_common×48 sp_rural_notable_notary×16 npc_common_limited×10 loot_standing_point×8 sp_guard×6 visible_loot×6 common_area_marker×1 food_loot_major×1 npc_wait×1 sp_notable×1 loot_point×1 pile_collider×1 food_loot×1 |
| sho_village_e_test | 0 |  |
| sho_village_e_test_v2 | 0 |  |
| sho_village_e_test_v2_dawa | 0 |  |
| sho_village_f | 0 |  |
| sho_village_h | 0 |  |
| sho_village_j | 0 |  |
| temporary_crafting_menu_outdoor | 3 | weapon_point×1 camera_instance×1 camera_point×1 |
| test_temple | 1 | npc_abbot×1 |

## 2. 原版对照（SandBox 战役场景节选：领主殿/地牢/酒馆/城镇）

| 场景 | tag 数 | tag 列表（次数） |
|---|---|---|
| aserai_castle_keep_a_l1_interior（原版 SandBox）| 9 | sp_guard_unarmed×21 npc_wait×13 sp_notable×11 npc_common×7 sp_guard_patrol×3 sp_guard×1 gambler_player×1 reserved×1 gambler_npc×1 |
| empire_castle_keep_a_l1_interior（原版 SandBox）| 7 | npc_wait×22 sp_guard_unarmed×21 sp_notable×6 npc_common×5 gambler_npc×1 reserved×1 gambler_player×1 |
| empire_dungeon_a（原版 SandBox）| 6 | npc_wait×12 alternative×7 npc_common×5 sp_guard×5 sp_prisoner×4 spawnpoint_cleaner×2 |
| empire_interior_tavern_a（原版 SandBox）| 4 | npc_wait×2 reserved×1 gambler_player×1 gambler_npc×1 |
| empire_village_a（原版 SandBox）| 2 | npc_wait×4 npc_common×4 |

## 3. sho_keep_scene vs 原版领主殿（empire_castle_keep_a_l1_interior）差异

- 原版有、织丰缺：sp_guard_unarmed
- 织丰有、原版缺：npc_drinker, npc_idle, sp_throne
