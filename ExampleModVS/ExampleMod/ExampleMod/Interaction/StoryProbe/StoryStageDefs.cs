using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 台本里的一条指令 = **一个原子 + 它的参数**（2026-11-13 立）。
    ///
    /// 🔴 字段名 = JSON 键名，**与 `plans/scenario-campaign-mode/05-演出系统.md` 的 `ScriptNode` 同构**
    ///    （`Cmd` + 参数 + `Async`）——将来接 `StoryEngine` 时**台本数据一行不改**，只换执行器。
    ///
    /// 缺省值（JSON 里不写就用这个）：
    ///   · <see cref="Scale"/> = 1
    ///   · <see cref="Facing"/> = -1（面朝玩家）
    ///   · <see cref="Civilian"/> = true（用平民装生成）
    /// </summary>
    public sealed class StageBeat
    {
        /// <summary>从进场景算起的秒数（绝对时间；v1 只支持这一种时序）。</summary>
        public float At;

        /// <summary>原子名（英文 token）：player_freeze / player_release / camera_cut / camera_return /
        /// prop_spawn / actor_place / actor_action / actor_say / wait。</summary>
        public string Cmd;

        /// <summary>prop_spawn 的实例名（日志用，必须唯一）。</summary>
        public string Id;

        /// <summary>camera_cut 的机位 id（= Camera.csv 的 ID 列）。</summary>
        public string Case;

        /// <summary>actor_* 的目标角色（= StoryProbeDefs 的 NpcId）。</summary>
        public string Actor;

        /// <summary>actor_action 的动作名（原版 act_*）。</summary>
        public string Action;

        /// <summary>actor_say 的本地化键。</summary>
        public string TextKey;

        /// <summary>prop_spawn 的预制品名（必须是本场景已加载的）。</summary>
        public string Prefab;

        /// <summary>语义位：player / gate / gate_inner / idle / market（见 StoryStageRunner.ResolveAnchor）。</summary>
        public string Anchor;

        /// <summary>相对语义位的米数（+Fwd = 朝城门方向，+Right = 右手侧，+Up = 抬高）。</summary>
        public float Fwd, Right, Up;

        /// <summary>物件朝向（度，绕竖直轴；0 = 语义位的基准朝向）。</summary>
        public float Yaw;

        /// <summary>物件缩放。</summary>
        public float Scale = 1f;

        /// <summary>人朝向（度；-1 = 面朝玩家）。</summary>
        public float Facing = -1f;

        /// <summary>actor_place：true = 用平民装生成（默认），false = 战斗装。</summary>
        public bool Civilian = true;

        /// <summary>v1 **接受但不生效**：所有原子都是瞬时的，等 actor_enter 落地再启用。</summary>
        public bool Async;

        /// <summary>wait 原子的秒数。</summary>
        public float Seconds;

        /// <summary>中文备注（给人看的，不参与执行）。</summary>
        public string Note;
    }

    /// <summary>一份台本 = 按 `At` 升序排列的指令流。</summary>
    public sealed class StageScript
    {
        public string Script;
        public List<StageBeat> Beats;

        /// <summary>
        /// 语义位 → 场景实体标签（可自己加）。
        /// `player` / `gate` / `gate_inner` 是引擎侧现成锚点，不用在这里写。
        /// 例：`"idle": ["npc_idle"]`、`"market": ["sp_merchant"]`。
        /// </summary>
        public Dictionary<string, string[]> Anchors;
    }

    /// <summary>
    /// 台本文件的读写（`ModuleData/DesignData/StoryStage*.json`，与 Dialogue 数据同一套用法）。
    /// 🔴 放 `DesignData/` 根、**不建子目录**（铁律 26）。
    /// </summary>
    public static class StoryStageDefs
    {
        private static readonly Dictionary<string, StageScript> _cache = new Dictionary<string, StageScript>();

        public static string DesignDataFolder
        {
            get
            {
                try
                {
                    var dll = typeof(StoryStageDefs).Assembly.Location;
                    var modDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)));
                    return Path.Combine(modDir ?? "", "ModuleData", "DesignData");
                }
                catch { return null; }
            }
        }

        public static StageScript Load(string scriptName, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(scriptName)) scriptName = "StoryStage";
            if (!forceReload && _cache.TryGetValue(scriptName, out var cached)) return cached;

            string folder = DesignDataFolder;
            if (string.IsNullOrEmpty(folder)) return null;

            string path = Path.Combine(folder, scriptName + ".json");
            if (!File.Exists(path))
            {
                DebugLogger.Log($"[StoryStage] script file missing: {path}");
                return null;
            }

            try
            {
                var script = JsonConvert.DeserializeObject<StageScript>(File.ReadAllText(path));
                if (script?.Beats == null || script.Beats.Count == 0)
                {
                    DebugLogger.Log($"[StoryStage] script empty: {path}");
                    return null;
                }

                // 按 At 稳定排序（同 At 保持文件里的先后）
                script.Beats = StableSortByAt(script.Beats);
                _cache[scriptName] = script;
                DebugLogger.Log($"[StoryStage] loaded '{scriptName}' beats={script.Beats.Count} ({path})");
                return script;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryStage] script parse failed {path}: {ex.Message}");
                return null;
            }
        }

        private static List<StageBeat> StableSortByAt(List<StageBeat> beats)
        {
            var indexed = new List<KeyValuePair<int, StageBeat>>(beats.Count);
            for (int i = 0; i < beats.Count; i++) indexed.Add(new KeyValuePair<int, StageBeat>(i, beats[i]));
            indexed.Sort((a, b) =>
            {
                int c = a.Value.At.CompareTo(b.Value.At);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            var result = new List<StageBeat>(beats.Count);
            foreach (var kv in indexed) result.Add(kv.Value);
            return result;
        }

        public static void ClearCache() => _cache.Clear();
    }
}
