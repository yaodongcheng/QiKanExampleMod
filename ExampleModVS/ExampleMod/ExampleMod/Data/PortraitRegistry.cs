using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 立绘内容包数据注册表（2026-08-30）—— 通用读取器，契约见下。
    ///
    /// 数据在哪：任何模块（内容包）的 ModuleData/AssetRegistry/ 下两个 CSV：
    ///   ProfileStages.csv   列: StringId,stage,tkid,bustupSprite,miniheadSprite
    ///                       每角色×每立绘阶段一行；无阶段角色 = 单行 stage 空串
    ///   ProfileEmotion.csv  列: tkid,emotion,bustupSprite,miniheadSprite
    /// 数据由内容包生成器产出（ArtSource/scripts/build_profile_pack.py，生成物禁手改）。
    /// 本类纯通用：字符串均来自数据文件（列名大小写敏感），不感知任何文化内容（铁律 3）。
    ///
    /// 使用姿势：懒加载（首次查询才扫模块列表 + 读 CSV；静态构造器/OnSubModuleLoad 禁止触碰）。
    /// 缺失内容包/文件损坏 → 空目录 + 一条 [PortraitRegistry] 日志，不抛（铁律 1 风格）。
    /// </summary>
    public static class PortraitRegistry
    {
        private static bool _loaded;
        private static readonly Dictionary<string, List<StagePortrait>> _stagesByStringId = new Dictionary<string, List<StagePortrait>>();
        private static readonly Dictionary<(string tkid, string emotion), string> _emotionBustup = new Dictionary<(string, string), string>();
        private static readonly Dictionary<(string tkid, string emotion), string> _emotionMinihead = new Dictionary<(string, string), string>();

        private static readonly object _lock = new object();

        /// <summary>单个立绘阶段条目（一卡一形象；stage 空 = 该角色只此一张）</summary>
        public struct StagePortrait
        {
            /// <summary>阶段词（数据文件原样；无阶段 = 空串）</summary>
            public readonly string Stage;
            /// <summary>TPAC 卡编号（内容包内稳定标识）</summary>
            public readonly string Tkid;
            /// <summary>立绘 sprite 名（lwnprof_bustup_{tkid}）</summary>
            public readonly string BustupSpriteName;
            /// <summary>小头像 sprite 名（lwnprof_mini_{tkid}）</summary>
            public readonly string MiniheadSpriteName;

            public StagePortrait(string stage, string tkid, string bustupSpriteName, string miniheadSpriteName)
            {
                Stage = stage;
                Tkid = tkid;
                BustupSpriteName = bustupSpriteName;
                MiniheadSpriteName = miniheadSpriteName;
            }
        }

        /// <summary>取某角色（StringId）的全部立绘阶段，按数据行序（生成器保证阶段序）</summary>
        public static IReadOnlyList<StagePortrait> GetStagePortraits(string stringId)
        {
            EnsureLoaded();
            if (stringId == null) return Array.Empty<StagePortrait>();
            return _stagesByStringId.TryGetValue(stringId, out var list) ? list : null;
        }

        /// <summary>取某角色当前阶段立绘；stage 为空串/未命中阶段 → 回退第一张（单卡角色即其本身）</summary>
        public static StagePortrait GetStagePortrait(string stringId, string stage)
        {
            var list = GetStagePortraits(stringId);
            if (list == null || list.Count == 0)
                return default;
            if (!string.IsNullOrEmpty(stage))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Stage == stage) return list[i];
                }
            }
            if (!string.IsNullOrEmpty(stage)) return list[0]; // 阶段词未命中（数据变更）→ 回退
            return list[0];
        }

        /// <summary>取情绪 sprite 名；isBustup=true → 立绘，false → 小头像</summary>
        public static string GetEmotionSpriteName(string tkid, string emotion, bool isBustup)
        {
            EnsureLoaded();
            var dict = isBustup ? _emotionBustup : _emotionMinihead;
            return dict.TryGetValue((tkid ?? "", emotion ?? ""), out var name) ? name : null;
        }

        // ───────────────────────── 加载 ─────────────────────────

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    foreach (var module in ModuleHelper.GetModules())
                    {
                        string dir = Path.Combine(ModuleHelper.GetModuleFullPath(module.Id), "ModuleData", "AssetRegistry");
                        if (!Directory.Exists(dir)) continue;
                        LoadStages(Path.Combine(dir, "ProfileStages.csv"));
                        LoadEmotions(Path.Combine(dir, "ProfileEmotion.csv"));
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[PortraitRegistry] 扫描失败: {ex.Message}");
                }
                _loaded = true;
            }
        }

        private static void LoadStages(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                using (var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, Delimiters = new[] { "," }, HasFieldsEnclosedInQuotes = true })
                {
                    string[] header = parser.ReadFields();
                    if (header == null) return;
                    int iStringId = Array.IndexOf(header, "StringId");
                    int iStage = Array.IndexOf(header, "stage");
                    int iTkid = Array.IndexOf(header, "tkid");
                    int iBustup = Array.IndexOf(header, "bustupSprite");
                    int iMini = Array.IndexOf(header, "miniheadSprite");
                    if (iStringId < 0 || iTkid < 0 || iBustup < 0 || iMini < 0)
                    {
                        DebugLogger.Log($"[PortraitRegistry] ProfileStages.csv 列头不符（需 StringId/tkid/bustupSprite/miniheadSprite）: {path}");
                        return;
                    }
                    int count = 0;
                    while (!parser.EndOfData)
                    {
                        var fields = parser.ReadFields();
                        if (fields == null || fields.Length <= Math.Max(iStringId, Math.Max(iTkid, Math.Max(iBustup, iMini)))) continue;
                        string sid = fields[iStringId].Trim();
                        if (string.IsNullOrEmpty(sid)) continue;
                        var item = new StagePortrait(
                            iStage >= 0 ? fields[iStage].Trim() : "",
                            fields[iTkid].Trim(),
                            fields[iBustup].Trim(),
                            fields[iMini].Trim());
                        if (!_stagesByStringId.TryGetValue(sid, out var list))
                        {
                            list = new List<StagePortrait>();
                            _stagesByStringId[sid] = list;
                        }
                        list.Add(item);
                        count++;
                    }
                    DebugLogger.Log($"[PortraitRegistry] ProfileStages {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))}: {count} 行");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PortraitRegistry] ProfileStages.csv 解析失败 {path}: {ex.Message}");
            }
        }

        private static void LoadEmotions(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                using (var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, Delimiters = new[] { "," }, HasFieldsEnclosedInQuotes = true })
                {
                    string[] header = parser.ReadFields();
                    if (header == null) return;
                    int iTkid = Array.IndexOf(header, "tkid");
                    int iEmo = Array.IndexOf(header, "emotion");
                    int iBustup = Array.IndexOf(header, "bustupSprite");
                    int iMini = Array.IndexOf(header, "miniheadSprite");
                    if (iTkid < 0 || iEmo < 0 || iBustup < 0 || iMini < 0) return;
                    while (!parser.EndOfData)
                    {
                        var fields = parser.ReadFields();
                        if (fields == null || fields.Length <= Math.Max(iTkid, Math.Max(iEmo, Math.Max(iBustup, iMini)))) continue;
                        string tkid = fields[iTkid].Trim();
                        if (string.IsNullOrEmpty(tkid)) continue;
                        string emo = fields[iEmo].Trim();
                        _emotionBustup[(tkid, emo)] = fields[iBustup].Trim();
                        _emotionMinihead[(tkid, emo)] = fields[iMini].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PortraitRegistry] ProfileEmotion.csv 解析失败 {path}: {ex.Message}");
            }
        }
    }
}
