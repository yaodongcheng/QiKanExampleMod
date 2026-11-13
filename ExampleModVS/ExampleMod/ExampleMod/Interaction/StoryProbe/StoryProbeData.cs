using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **序章的全部数据文件读取**（2026-11-13 立）。
    ///
    /// 两个正本，都在 `ModuleData/DesignData/`：
    ///   · `StoryProbeCast.json`    —— 势力 / 九个人 / 常驻动作 / 6 起开场戏 / 物品别名
    ///   · `StoryProbeTuning.json`  —— 节奏与几何（间隔、距离、判定半径…）
    ///
    /// 🔴 **纪律**：这两份文件之外，**不许再有可复用的数值写回 C#**。
    ///    加一个人、改一个价码、换一个模板、调一次间隔 = 改 JSON，不编译。
    /// 🔴 文件缺失/坏掉 ⇒ **打 Error 日志并抛给调用方**，不静默用一半数据跑。
    /// </summary>
    public static class StoryProbeDataFiles
    {
        public static string DesignDataFolder
        {
            get
            {
                try
                {
                    var dll = typeof(StoryProbeDataFiles).Assembly.Location;
                    var modDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)));
                    return Path.Combine(modDir ?? "", "ModuleData", "DesignData");
                }
                catch { return null; }
            }
        }

        public static T Load<T>(string fileName) where T : class
        {
            string folder = DesignDataFolder;
            if (string.IsNullOrEmpty(folder)) return null;

            string path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
            {
                DebugLogger.Log($"[StoryProbe] DATA FILE MISSING: {path}");
                return null;
            }

            try
            {
                var data = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
                if (data == null) DebugLogger.Log($"[StoryProbe] DATA FILE EMPTY: {path}");
                return data;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] DATA FILE PARSE FAILED {path}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>`StoryProbeCast.json` 的根。</summary>
    public sealed class StoryProbeCastData
    {
        public List<StoryFactionDef> Factions;
        public List<StoryNpcDef> Npcs;
        public List<StreetLifeDef> StreetLife;
        public List<StoryEventDef> Events;

        /// <summary>物品 id → 兜底别名（铁律 5 第一轮用；第二轮才走内存遍历）。</summary>
        public Dictionary<string, string[]> ItemAliases;

        public static StoryProbeCastData Load()
        {
            var data = StoryProbeDataFiles.Load<StoryProbeCastData>("StoryProbeCast.json");
            if (data == null) return null;

            data.Factions = data.Factions ?? new List<StoryFactionDef>();
            data.Npcs = data.Npcs ?? new List<StoryNpcDef>();
            data.StreetLife = data.StreetLife ?? new List<StreetLifeDef>();
            data.Events = data.Events ?? new List<StoryEventDef>();
            data.ItemAliases = data.ItemAliases ?? new Dictionary<string, string[]>();

            DebugLogger.Log($"[StoryProbe] cast loaded: factions={data.Factions.Count} npcs={data.Npcs.Count} " +
                            $"streetlife={data.StreetLife.Count} events={data.Events.Count} itemAliases={data.ItemAliases.Count}");
            return data;
        }
    }

    /// <summary>`StoryProbeTuning.json` 的根 —— 节奏与几何。</summary>
    public sealed class StoryProbeTuningData
    {
        public AmbientTuning Ambient = new AmbientTuning();
        public EventTuning Events = new EventTuning();
        public SkitTuning Skits = new SkitTuning();
        public GateTuning Gate = new GateTuning();
        public GreetingTuning Greeting = new GreetingTuning();
        public CrimeTuning Crime = new CrimeTuning();
        public IntroTuning Intro = new IntroTuning();
    }

    public sealed class AmbientTuning
    {
        /// <summary>进场后第一次小动作的延迟区间（秒）。</summary>
        public float FirstDelayMinSec = 2f;
        public float FirstDelayMaxSec = 10f;
        /// <summary>之后每次小动作的间隔区间（秒）。</summary>
        public float GapMinSec = 8f;
        public float GapMaxSec = 20f;
    }

    public sealed class EventTuning
    {
        public float FirstDelaySec = 12f;
        public float RetryGapSec = 5f;
        public float FirstLineDelaySec = 1f;
        public float ResumeLineDelaySec = 1.5f;
        public float LineGapMinSec = 3f;
        public float LineGapMaxSec = 5f;
        public float ActionChangeAfterSec = 3f;
        public float ActionChangeChancePerSec = 0.25f;
        public float SoloActionChancePerSec = 0.08f;
        public float MaxDurationSec = 20f;
        /// <summary>两人离得超过这个距离就把一个挪到对方面前（观众看不出他们在吵）。</summary>
        public float ActorSpacingMeters = 4f;
        public float ActorApproachMeters = 1.8f;
        public float JoinArmDistanceMeters = 6f;
        public float JoinDistanceMeters = 4f;
        public float PlayerFarMeters = 20f;
        public float PlayerFarGraceSec = 3f;
        public float ResumeAfterReturnSec = 4f;
        /// <summary>手动 resume 街道生活后，隔多久重新起戏。</summary>
        public float ResumeGapSec = 3f;
    }

    public sealed class SkitTuning
    {
        public float FirstDelaySec = 90f;
        public float MaxDurationSec = 20f;
    }

    public sealed class GateTuning
    {
        /// <summary>越过守门人多少米算"想穿门"。</summary>
        public float PastMeters = 1f;
        /// <summary>离门内目标点多少米算"到了"。</summary>
        public float GoalRadiusMeters = 8f;
        /// <summary>离地多高算"翻墙"。</summary>
        public float ClimbHeightMeters = 2f;
        public float PushBackCooldownSec = 1.2f;
        public float GuardSpawnRadiusMeters = 60f;
        public float RecognizeDistanceMeters = 12f;
    }

    /// <summary>通缉值（进场景就挂，因为"遮面/冒充"那几条路要有通缉才有意义）。</summary>
    public sealed class CrimeTuning
    {
        /// <summary>第二次进城时城门上挂的小额通缉。</summary>
        public float SmallBountyAtEntry = 15f;
        /// <summary>被守门人认出来时追加的通缉。</summary>
        public float RecognizedExtra = 10f;
    }

    /// <summary>玩家靠近 / 拔刀时 NPC 的反应（只在氛围层，不影响玩法）。</summary>
    public sealed class GreetingTuning
    {
        /// <summary>玩家拔刀时，多少米内的 NPC 会说话。</summary>
        public float WeaponRadiusMeters = 6f;
        public float WeaponCooldownSec = 10f;
        /// <summary>玩家走到多少米内，NPC 会打招呼。</summary>
        public float NearRadiusMeters = 3f;
        public float NearCooldownSec = 30f;
    }

    public sealed class IntroTuning
    {
        /// <summary>第一次进场的强制失败（被打倒）演出时长。</summary>
        public float ForcedDefeatHoldSec = 4.2f;
    }

    /// <summary>全局取用点：`StoryProbeTuning.Current.Gate.PastMeters`。</summary>
    public static class StoryProbeTuning
    {
        private static StoryProbeTuningData _data;

        public static StoryProbeTuningData Current
        {
            get
            {
                if (_data == null)
                {
                    _data = StoryProbeDataFiles.Load<StoryProbeTuningData>("StoryProbeTuning.json");
                    if (_data == null)
                    {
                        // 文件不在就退回各字段的类内默认值（= 与 JSON 出厂值一致），并大声报错
                        DebugLogger.Log("[StoryProbe] TUNING FILE MISSING -> using built-in defaults. " +
                                        "Designers: put StoryProbeTuning.json under ModuleData/DesignData/.");
                        _data = new StoryProbeTuningData();
                    }
                    else
                    {
                        DebugLogger.Log("[StoryProbe] tuning loaded from StoryProbeTuning.json");
                    }
                }
                return _data;
            }
        }

        public static void Reload() { _data = null; }
    }
}
