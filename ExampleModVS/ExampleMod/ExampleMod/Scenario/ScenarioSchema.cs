using Newtonsoft.Json;
using System.Collections.Generic;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本事件 JSON 数据模型（events.jsonc 结构，权威 = plans/scenario-campaign-mode/01-剧本引擎核心.md 事件结构）。
    /// 字段映射 TK5 头字段；脚本步骤 = 线性序列（步骤类型表见 ScenarioRegistry.StepTypes）。
    /// </summary>
    public class ScenarioEventDef
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>触发时机（∈ ScenarioRegistry.Triggers）</summary>
        [JsonProperty("trigger")]
        public string Trigger { get; set; }

        /// <summary>only house_enter 合法（Facility::house 形式，∈ ScenarioRegistry.Facilities）</summary>
        [JsonProperty("facility")]
        public string Facility { get; set; }

        [JsonProperty("once")]
        public bool Once { get; set; } = true;

        [JsonProperty("priority")]
        public string Priority { get; set; } = "normal";

        /// <summary>条件 = DSL 文本表达式（求值器在工作包 W2；加载期只做非空 + 括号配平）</summary>
        [JsonProperty("condition")]
        public string Condition { get; set; }

        [JsonProperty("script")]
        public List<ScenarioScriptStep> Script { get; set; }

        public override string ToString() => $"{Id} [{Trigger}] once={Once} pri={Priority} steps={Script?.Count ?? 0}";
    }

    /// <summary>事件脚本步骤。step = 步骤类型（01 步骤类型表）；effect 的 action = 16a 命令侧名。</summary>
    public class ScenarioScriptStep
    {
        [JsonProperty("step")]
        public string Step { get; set; }

        // 通用参数字段（按 step/action 取用，全部可选——未知字段不反序列化，加载期不报错，
        // 校验/执行器按 step 类型检查必需字段）
        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("when")]
        public string When { get; set; }

        [JsonProperty("playbackId")]
        public string PlaybackId { get; set; }

        [JsonProperty("note")]
        public string Note { get; set; }

        [JsonProperty("then")]
        public List<ScenarioScriptStep> Then { get; set; }

        [JsonProperty("else")]
        public List<ScenarioScriptStep> Else { get; set; }

        public override string ToString() => $"[{Step}] {Action ?? PlaybackId ?? ""}";
    }

    /// <summary>演绎分件（story/*.jsonc）——W5 播放器消费，W1 只负责列出。</summary>
    public class ScenarioPlaybackDef
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("form")]
        public string Form { get; set; }

        [JsonProperty("actors")]
        public List<ScenarioPlaybackActor> Actors { get; set; }

        [JsonProperty("lines")]
        public List<ScenarioScriptStep> Lines { get; set; }
    }

    public class ScenarioPlaybackActor
    {
        [JsonProperty("heroId")]
        public string HeroId { get; set; }

        [JsonProperty("agentId")]
        public string AgentId { get; set; }

        [JsonProperty("slot")]
        public string Slot { get; set; }

        [JsonProperty("present")]
        public bool Present { get; set; }
    }

    /// <summary>
    /// 场景注册表常量（token 权威 = 16-DSL注册表全表.md §二，2026-08-26 起单所有权）。
    /// 🔴 改词条 = 改 16 文档 + 本表同步（表外值 = 加载报错禁用剧本，不静默）。
    /// </summary>
    public static class ScenarioRegistry
    {
        /// <summary>trigger 注册表 v1（16 §二；含 1.5.1/1.2.12 共用 token）</summary>
        public static readonly HashSet<string> Triggers = new HashSet<string>
        {
            "daily", "monthly", "game_start", "settlement_enter", "house_enter",
            "council_start", "travel_screen", "field_battle_start", "field_battle_end",
            "siege_battle_start", "siege_battle_end", "army_move_end", "chapter_freeze",
            "game_clear", "npc_talk", "battle_decided", "clan_destroyed",
            "monthly_forced", "travel_screen_select", "facility_select",
            "army_move_start", "game_over",
        };

        /// <summary>facility 注册表 v1（16 §二；house_enter 才合法；值 = 去 Facility:: 前缀后的 token）</summary>
        public static readonly HashSet<string> Facilities = new HashSet<string>
        {
            "house", "tavern", "castle_hall", "council_room", "za", "clinic", "dojo",
            "house_min", "shop", "nanban_trade", "smithy", "tea_room", "temple",
            "dojo_town", "doctor_house", "artisan_house", "kuge_house", "samurai_house",
            "smithy_town", "pirate_den", "rice_shop", "inn", "tea_master_house",
            "ninja_house", "overseas_trade", "castle_drill", "stable", "nanban_temple",
            "village_drill", "village_training", "fort_drill", "fort_training",
            "shipyard", "council_own", "pirate_manor",
        };

        /// <summary>01 步骤类型表（合法 step 值；表外 = 加载报错）+ 08b §3.5 通用步骤（载体=05 演出的命令侧名）</summary>
        public static readonly HashSet<string> StepTypes = new HashSet<string>
        {
            // 01 步骤类型表
            "perform", "inquiry", "cutscene", "im_message", "effect", "if",
            "wait", "bgm", "se", "scene_enter", "scene_exit", "choice",
            "loop", "break", "module_exit", "note",
            // 08b §3.5 通用步骤（载体=05 演出，命令侧名样例——产物实测 11 条）
            "bgm_change", "se_start", "se_stop", "se_loop", "image_show", "image_hide",
            "bg_change", "bg_restore", "screen_effect", "scene_next", "message_close",
            // 演绎层（05 源格式阶段，W5 编译器透传）
            "actor_enter", "actor_move", "actor_leave", "camera", "actor_action",
            // 容器/代入（16b 组裁定）
            "container_set", "container_filter", "container_exclude", "container_sort",
            "container_pick", "container_clear",
        };

        /// <summary>事件头字段名（校验/日志用；字段权威 = 01 事件结构）</summary>
        public static readonly HashSet<string> EventHeadFields = new HashSet<string>
        {
            "id", "trigger", "facility", "once", "priority", "condition", "script",
        };
    }
}
