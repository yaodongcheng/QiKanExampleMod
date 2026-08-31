using System;
using System.Collections.Generic;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.TwoDimension;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 通用 Sprite 资产管理器（2026-08-30）——「任意 Sprite → 屏上显示 + 内存按需管理」。
    ///
    /// 解决什么：引擎的 SpriteCategory 默认整分类加载（一张 4096 纹理一次进显存）。立绘内容包
    /// （如 ShokuhoTaikouExpansionPack 的 lwnprof_* 四分类，每张卡 = 一个独立 sheet）共计 2700+
    /// 张纹理，必须按需单张加载 / 单张释放 —— 引擎原生提供 PartialLoadAtIndex / PartialUnloadAtIndex。
    ///
    /// 使用姿势（🔴 懂热重载才安全）：
    /// - 显示点唯一入口 = GetOrLoad(name)，**每次显示时调用，不缓存 Sprite 对象**。
    ///   调试模式开 UI 热重载（ResourceDepot.CheckForChanges → SpriteData.Reload）会替换全部
    ///   SpritePart/SpriteCategory 对象，缓存旧 Sprite 会因已卸载纹理渲染成黑块。
    /// - Retrieve 后直接赋 ImageWidget.Sprite（先例 SecretLetterButtonInjector）。
    /// - 无内容包/名称不存在 → 返回 null + [SpriteAssets] 日志，不抛（铁律 1 风格）。
    ///
    /// 内存策略（LRU）：按分类设容量（bustup 级 12 张 ≈ 4.6MB、minihead 级 64 张 ≈ 4MB；
    /// 聊天列表窗口 20-40 行，容量太小会抖动）。驱逐 = PartialUnloadAtIndex；250ms 宽限
    /// （≈2 帧，防"渲染半截被释放"黑块）+ 只逐最近未使用条目。
    /// </summary>
    public static class SpriteAssetsManager
    {
        // 分类 → 允许驻流最多张数（按纹理字节分档：512x768 DXT5 ≈ 384KB；256x256 ≈ 64KB）
        private static readonly Dictionary<string, int> CategoryCap = new Dictionary<string, int>
        {
            { "lwnprof_bustup", 12 },
            { "lwnprof_mini", 64 },
            { "lwnprof_emobustup", 12 },
            { "lwnprof_emomini", 64 },
        };

        /// <summary>驱逐宽限（毫秒 ≈ 2 帧 @30fps；防渲染竞态黑块）</summary>
        private const long EvictGraceMs = 250;

        private struct Entry
        {
            public string Category;
            public int SheetIndex;   // 1-based；sheetID 语义在 SpriteData Reload 后不变
            public long LastUsedMs;
        }

        private static readonly List<Entry> _recent = new List<Entry>();

        private static int CapOf(string category)
        {
            return CategoryCap.TryGetValue(category, out var cap) ? cap : 12;
        }

        /// <summary>查询 sprite（仅字典直查，零分配；不保证纹理已加载）</summary>
        public static Sprite GetSprite(string spriteName)
        {
            var data = UIResourceManager.SpriteData;
            if (data == null)
            {
                DebugLogger.Log($"[SpriteAssets] SpriteData 未初始化（UI 未就绪？）GetSprite({spriteName}) -> null");
                return null;
            }
            return data.GetSprite(spriteName);
        }

        /// <summary>显示点唯一入口：查 sprite + 确保其纹理已加载（未加载则按 sheet 单张加载）</summary>
        public static Sprite GetOrLoad(string spriteName)
        {
            var sprite = GetSprite(spriteName);
            if (sprite == null)
            {
                DebugLogger.Log($"[SpriteAssets] Sprite 不存在: {spriteName}（内容包缺 GUI/LWProfilesSpriteData.xml？）");
                return null;
            }
            EnsureLoaded(sprite);
            return sprite;
        }

        /// <summary>确保 sprite 对应的纹理已加载（幂等；热重载/跨场景引擎卸载后自动重建 partial 状态）</summary>
        public static bool EnsureLoaded(Sprite sprite)
        {
            if (sprite == null) return false;
            try
            {
                var part = (sprite as SpriteGeneric)?.SpritePart;
                if (part == null) return false;
                var cat = part.Category;
                if (cat == null || string.IsNullOrEmpty(cat.Name)) return false;

                int sheetIndex = part.SheetID;
                if (sheetIndex < 1) return false;

                if (!cat.IsLoaded)
                {
                    // 引擎卸载（跨屏/读档）后重建按需模式
                    cat.InitializePartialLoad();
                }
                if (cat.SpriteSheets == null || cat.SpriteSheets.Count < sheetIndex)
                {
                    cat.InitializePartialLoad();
                }
                if (cat.SpriteSheets[sheetIndex - 1] == null)
                {
                    cat.PartialLoadAtIndex(UIResourceManager.ResourceContext, V.UIResourceDepot(), sheetIndex);
                    DebugLogger.Log($"[SpriteAssets] 加载 sheet {cat.Name}#{sheetIndex}（sprite {sprite.Name}）");
                }
                Touch(cat.Name, sheetIndex, sprite.Name);
                Evict(cat);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SpriteAssets] EnsureLoaded 失败 {sprite.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>主动立即释放一张（已加载则卸载）</summary>
        public static bool Release(string spriteName)
        {
            try
            {
                var sprite = GetSprite(spriteName);
                if (sprite == null) return false;
                var part = (sprite as SpriteGeneric)?.SpritePart;
                if (part?.Category == null) return false;
                int idx = part.SheetID;
                if (part.Category.SpriteSheets != null && part.Category.SpriteSheets.Count >= idx &&
                    part.Category.SpriteSheets[idx - 1] != null)
                {
                    part.Category.PartialUnloadAtIndex(idx);
                    DebugLogger.Log($"[SpriteAssets] 释放 {part.Category.Name}#{idx}（{sprite.Name}）");
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SpriteAssets] Release 失败 {spriteName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>清空全部已加载纹理（进出长期 UI 前可调）</summary>
        public static void ReleaseAll()
        {
            try
            {
                _recent.Clear();
                var sd = UIResourceManager.SpriteData;
                if (sd == null) return;
                foreach (var category in sd.SpriteCategories.Values)
                {
                    // 1.2.12 无 IsPartiallyLoaded 标志：未 InitializePartialLoad 时 IsLoaded=false，Release 自动跳过
                    if (category.IsLoaded)
                    {
                        category.ReleasePartialLoad();
                    }
                }
                DebugLogger.Log("[SpriteAssets] ReleaseAll 完成");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SpriteAssets] ReleaseAll 失败: {ex.Message}");
            }
        }

        // ───────────────────────── 内部 ─────────────────────────

        private static long NowMs()
        {
            return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        }

        private static void Touch(string category, int sheetIndex, string spriteName)
        {
            long now = NowMs();
            for (int i = 0; i < _recent.Count; i++)
            {
                var e = _recent[i];
                if (e.Category == category && e.SheetIndex == sheetIndex)
                {
                    e.LastUsedMs = now;
                    _recent[i] = e;
                    return;
                }
                // 丢弃当前已无对应加载纹理的过期条目（防止驱逐对象已失效）
                if ((now - e.LastUsedMs) > EvictGraceMs && !TextureStillLoaded(e.Category, e.SheetIndex))
                {
                    _recent.RemoveAt(i);
                    i--;
                }
            }
            _recent.Add(new Entry { Category = category, SheetIndex = sheetIndex, LastUsedMs = now, });
        }

        private static bool TextureStillLoaded(string category, int sheetIndex)
        {
            var cat = V.GetSpriteCategory(category);
            return cat != null && cat.IsLoaded && cat.SpriteSheets != null &&
                   cat.SpriteSheets.Count >= sheetIndex && cat.SpriteSheets[sheetIndex - 1] != null;
        }

        private static void Evict(SpriteCategory justTouched)
        {
            long now = NowMs();
            // 统计本 category 当前驻流条目
            var touched = _recent.FindAll(e => e.Category == justTouched.Name);
            int cap = CapOf(justTouched.Name);
            if (touched.Count <= cap) return;

            // 从最旧开始驱逐；宽限期内条目跳过（可能正被渲染）
            touched.Sort((a, b) => a.LastUsedMs.CompareTo(b.LastUsedMs));
            for (int i = 0; i < touched.Count - cap; i++)
            {
                var victim = touched[i];
                if (now - victim.LastUsedMs < EvictGraceMs) continue;
                try
                {
                    if (justTouched.SpriteSheets != null &&
                        justTouched.SpriteSheets.Count >= victim.SheetIndex &&
                        justTouched.SpriteSheets[victim.SheetIndex - 1] != null)
                    {
                        justTouched.PartialUnloadAtIndex(victim.SheetIndex);
                        DebugLogger.Log($"[SpriteAssets] 驱逐 {victim.Category}#{victim.SheetIndex}（LRU 超容 {cap}）");
                    }
                    _recent.Remove(victim);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[SpriteAssets] 驱逐失败 {victim.Category}#{victim.SheetIndex}: {ex.Message}");
                }
            }
        }
    }
}
