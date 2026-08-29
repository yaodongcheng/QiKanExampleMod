using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 队伍屏/家族屏「密信」按钮注入器（2026-08-17，纯 C# 动态插入，零 prefab 覆写）：
    /// - 用户裁定：不覆写原版 prefab（避免与其他 UI mod 同名互斥）——扫描原版 widget 树，
    ///   `Screen.Layers → GauntletLayer.UIContext.Root` 找 Id="TalkButton"（队伍行）/ Id="TalkToHeroButton"
    ///   （家族详情面板）→ `AddChildAtIndex` 在交谈按钮旁注入密信按钮（Id="LWN_SecretLetterBtn"，幂等）。
    /// - 行→Hero 映射：读原版绑定已赋值的 widget 属性——队伍行根 `PartyTroopTupleButtonWidget.CharacterID`
    ///   （=`Character.StringId`，反编译实锤）；家族详情面板 `CharacterTableauWidget.CharStringId`
    ///   （=`character.StringId`，反编译实锤）。反射读属性，不依赖强类型（该类程序集跨版本归属不定）。
    /// - 点击 → `ImChatManager.GetDirectConversation(heroId)` + `ImChatView.Open(conv)`（探查板传信同款链路）；
    ///   关屏再开（PopScreen 后延迟 0.1s 开 IM，防层挂到将销毁的屏上）。
    /// - 可见性：每帧跟随原版 slot 容器（引擎绑定求值 `@IsTalkableCharacter`/`@IsTalkVisible`）+ PlotEnabled 总闸。
    /// - hover 提示：手动 hit-test（ImChatView 缩略模式同款 IsPointInRect）+ MBInformationManager.ShowHint。
    /// - 版本兼容：Id 定位 + 反射读属性；1.2.12 找不到 → 静默不注入（安全降级，不崩）。
    /// 设计文档：plans/im-secret-letter-button.md
    /// </summary>
    public static class SecretLetterButtonInjector
    {
        private const string ButtonId = "LWN_SecretLetterBtn";
        // 本地化 key：密信按钮提示（LWN_im_secret_letter_hint）
        private const string HintKey = "LWN_im_secret_letter_hint";
        private const float ScanIntervalSec = 0.3f;

        private static float _scanTimer;
        private static Widget _hoverOn;            // 当前 hover 的注入按钮（防 ShowHint 刷屏）
        private static readonly List<Injected> _live = new List<Injected>();
        // 🔴 2026-08-25（实机日志 17:19:53 刷屏）：树销毁窗口期内 Scan 对同一批将死行反复重注入
        // （失败自清理删了 _live 条目 → 幂等去重失效 → 0.3s 后 Scan 又注入 → 下帧再失败再删，
        // 每个 0.3s 周期刷 ~14 行）。修：销毁失败后抑制注入 2s（销毁窗口只持续数帧，远短于 2s；
        // 面板刷新重建场景同样被抑制 2s 后自动恢复，不影响功能），日志 5s 节流（WndProc 异常同款）。
        private static float _injectSuppressSec;
        private static int _lastCleanupLogTick;

        private class Injected
        {
            public ButtonWidget Button;
            public Widget VisibilityAnchor;   // 原版 slot 容器（可见性跟随；队伍=TalkButton 父容器 @IsTalkableCharacter / 家族=TalkToHeroButton 容器 @IsTalkVisible）
            public Widget ClanRoot;           // 家族屏 UIContext.Root（tableau 定位用；队伍行 = null）
            public bool IsHeroTarget;         // 诊断统计：行根 Hero 判定结果（目标：替换 slot 锚为「任意列表英雄行」，待反射链路验证后启用）
        }

        // ───────────────────────── 驱动入口 ─────────────────────────

        /// <summary>每帧驱动（ImChatView.OnScreenFrameTick 调用）：可见性/hover 每帧同步；注入扫描 0.3s 节流。</summary>
        public static void TickInject(float dt)
        {
            try
            {
                if (_injectSuppressSec > 0f) _injectSuppressSec -= dt;
                UpdateLive();
                _scanTimer += dt;
                if (_scanTimer >= ScanIntervalSec && _injectSuppressSec <= 0f)
                {
                    _scanTimer = 0f;
                    Scan();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] TickInject 异常: {ex.Message}");
            }
        }

        // ───────────────────────── 扫描与注入 ─────────────────────────

        /// <summary>只在 TopScreen 是队伍屏/家族屏时扫描（其余屏零开销）。
        /// 🔴 实际类名 GauntletPartyScreen/GauntletClanScreen（SandBox.GauntletUI.dll 实锤），
        /// 用 Contains 覆盖各版本命名变体。</summary>
        private static void Scan()
        {
            var top = ScreenManager.TopScreen;
            if (top == null) return;
            string name = top.GetType().Name;
            bool isParty = name.Contains("PartyScreen");
            bool isClan = name.Contains("ClanScreen");
            if (!isParty && !isClan) return;

            foreach (var layer in top.Layers)
            {
                var ui = (layer as GauntletLayer)?.UIContext;
                if (ui?.Root == null) continue;
                if (isParty)
                    InjectPartyRows(ui.Root);
                else
                    InjectClanDetail(ui.Root);
            }
        }

        /// <summary>队伍屏：收集全部行 TalkButton → 逐行注入（DFS 收集，不依赖兄弟遍历）。</summary>
        private static void InjectPartyRows(Widget root)
        {
            var talks = new List<Widget>();
            FindAllWidgetsById(root, "TalkButton", talks);
            int n = 0;
            foreach (var t in talks)
            {
                if (InjectNextToSlot(t, isParty: true, root: null)) n++;
            }
            int m = 0;
            foreach (var x in _live) if (x.IsHeroTarget) m++;
            if (n > 0)
                DebugLogger.Log($"[SecretLetter] 队伍屏注入 {n} 个密信按钮（hero 目标 {m}）");
        }

        /// <summary>家族屏：详情面板 TalkToHeroButton 容器 → 在其父（LastSeenLocationParent 横向 ListPanel）中紧跟插入。</summary>
        private static void InjectClanDetail(Widget root)
        {
            if (InjectNextToSlot(FindWidgetById(root, "TalkToHeroButton"), isParty: false, root))
                DebugLogger.Log("[SecretLetter] 家族屏注入密信按钮");
        }

        /// <summary>
        /// 幂等注入：在交谈按钮/容器旁插入密信按钮（返回是否新注入）。
        /// 插入点 = 容纳交谈按钮的列表（上溯最近 ListPanel），紧跟交谈按钮（或其容器）。
        /// 🔴 2026-08-29（实机诊断「密信按钮叠在交谈按钮上」根因修复）：行结构存在两代形态——
        ///   H盘 1.5.2: TalkButton → 容器 → ButtonsList(ListPanel) → ButtonCarrier；
        ///   玩家实机(旧版): TalkButton → ButtonsList(ListPanel) → ButtonCarrier（无容器）。
        ///   旧代码统一插「slot.ParentWidget」：实机上 = ButtonCarrier（无布局的普通 Widget）→
        ///   按钮渲染在容器原点 = 正好叠在交谈按钮上（诊断坐标实锤 btn=(1489,586) vs talk=(1489,590)）。
        ///   现改为上溯最近 ListPanel 插入，两代形态都落到列表布局里，结构未知则安全跳过。
        /// </summary>
        private static bool InjectNextToSlot(Widget talkWidget, bool isParty, Widget root)
        {
            if (talkWidget == null) return false;

            Widget insertInto;   // 注入目标列表
            Widget slot;         // 紧跟目标（插其 index+1）
            Widget anchor;       // 可见性锚
            if (isParty)
            {
                // 队伍：wrapper = 交谈按钮的「最近包装」（1.5.2 形态=容器 / 旧形态=按钮自身），
                // insertInto = wrapper 所在列表（两形态下统一为 ListPanel）
                Widget wrapper = talkWidget;
                if (!(talkWidget.ParentWidget is ListPanel))
                    wrapper = talkWidget.ParentWidget;
                if (wrapper == null) return false;
                insertInto = wrapper.ParentWidget;
                if (!(insertInto is ListPanel)) return false;   // 结构未知 → 安全跳过（不注入、不崩）
                slot = wrapper;
                anchor = wrapper;
            }
            else
            {
                // 家族：talkWidget = Id=TalkToHeroButton 的容器（FindWidgetById 父先命中）→ slot = 自身
                slot = talkWidget;
                if (slot == null || slot.ParentWidget == null) return false;
                insertInto = slot.ParentWidget;
                anchor = slot;
                // 🔴 家族屏不能跟随 TalkToHeroButton 容器（@IsTalkVisible 在非本队成员行 = false——召回按钮显示时交谈按钮隐藏）
                // → 改为向上找「子树含 CharacterTableauWidget 的最近祖先」= 详情面板容器（@IsAnyValidMemberSelected，选中有效成员即显示）
                for (Widget p = slot.ParentWidget; p != null; p = p.ParentWidget)
                {
                    if (FindWidgetByType(p, "CharacterTableauWidget") != null) { anchor = p; break; }
                }
            }

            // 幂等：同一父容器已注入过则跳过（_live 引用判断，不依赖 Id——比 FindWidgetById 可靠）
            foreach (var x in _live)
            {
                if (x.Button.ParentWidget == insertInto) return false;
            }

            // 🔴 2026-08-29（用户质疑「队伍 7 行只 1 个 hero，为什么注入 7 个」）：先做英雄行判定再注入——
            // 队伍屏非英雄行（模板士兵行/玩家行）根本不注入，列表里不留隐藏按钮。原实现「全行注入 + 依赖
            // 可见性锚隐藏」会让隐藏按钮带默认布局盒 (0,0) 悬在 ButtonsList——「引擎 StackLayout 不给
            // 不可见子节点分配布局盒」正是密信按钮盖住交谈按钮那类隐患的土壤；且每 0.3s 扫描对全行做
            // 创建-摘除循环 = 无效布局抖动。行数据变化（士兵行→英雄行）由 0.3s 扫描重注入兜底。
            bool heroTarget = !isParty || ResolveIsHeroTarget(talkWidget, null);
            if (!heroTarget) return false;

            var btn = CreateButton(slot.Context);
            int idx = IndexOfChild(insertInto, slot);
            if (idx < 0) return false;
            insertInto.AddChildAtIndex(btn, idx + 1);
            _live.Add(new Injected { Button = btn, VisibilityAnchor = anchor, ClanRoot = isParty ? null : root, IsHeroTarget = heroTarget });
            return true;
        }

        /// <summary>
        /// 注入时判定该行是否可密信目标：行根解析出 Hero StringId ∈ Hero.AllAliveHeroes，且排除玩家行（行根 IsMainHero）。
        /// 不跟随原版 @IsTalkableCharacter——该绑定只覆盖 Right 侧英雄行（反编译 UpdateTalkable 实锤）。
        /// </summary>
        private static bool ResolveIsHeroTarget(Widget btn, Widget clanRoot)
        {
            try
            {
                string id = ResolveRowHeroId(btn, clanRoot);
                if (string.IsNullOrEmpty(id)) return false;
                // 玩家行排除：队伍行根 IsMainHero（类型名匹配，见 ResolveRowHeroId 的 Id 覆盖说明）；家族成员行无玩家
                for (Widget p = btn.ParentWidget; p != null; p = p.ParentWidget)
                {
                    if (p.GetType().Name == "PartyTroopTupleButtonWidget")
                    {
                        if (p.GetType().GetProperty("IsMainHero", BindingFlags.Instance | BindingFlags.Public)?.GetValue(p) is bool m && m) return false;
                        break;
                    }
                    if (clanRoot != null) break;
                }
                return IsAliveHero(id);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] Hero 判定失败: {ex.Message}");
            }
            return false;
        }

        private static bool IsAliveHero(string heroId)
        {
            try { return Hero.AllAliveHeroes.Any(h => h.StringId == heroId); } catch { return false; }
        }

        /// <summary>构造密信按钮：原版交谈槽位同款样式（Party.TalkSlot.Background + talk_icon 变体）。
        /// 🔴 2026-08-17（Q4 手柄）：GamepadNavigationIndex=999——注入按钮是纯 C# 动态插入，给显式索引
        /// 让原版屏 NavigationScope 收编（手柄可聚焦；高值 = 排尾避免与原版按钮索引冲突——验证点，
        /// 失败 → 探查板传信（L3 可达）为手柄入口）。</summary>
        private static ButtonWidget CreateButton(UIContext context)
        {
            var btn = new ButtonWidget(context)
            {
                Id = ButtonId,
                GamepadNavigationIndex = 999,
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 50f,
                SuggestedHeight = 50f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                MarginLeft = 10f,
                MarginRight = 10f,
                DoNotPassEventsToChildren = true,
                UpdateChildrenStates = true,
            };
            try { btn.Brush = context.BrushFactory.GetBrush("Party.TalkSlot.Background"); } catch { }

            // 图标：原版 GameMenu 对话图标（用户指定 SPGeneral\GameMenu\conversation_icon；直接设 Sprite，
            // 不依赖自建 Brush——Brush XML 的 HueFactor 属性解析存在风险，已实测显示为空）
            var icon = new ImageWidget(context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 28f,
                SuggestedHeight = 28f,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            try { icon.Sprite = context.SpriteData.GetSprite("SPGeneral\\GameMenu\\conversation_icon"); } catch (Exception ex) { DebugLogger.Log($"[SecretLetter] 图标 sprite 加载失败: {ex.Message}"); }
            btn.AddChild(icon);

            btn.ClickEventHandlers.Add(OnClicked);
            return btn;
        }

        // ───────────────────────── 每帧同步（可见性 + hover 提示）─────────────────────────

        private static void UpdateLive()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var it = _live[i];
                // 屏幕/树已销毁 → 自清理。🔴 Context 判据不可省：销毁窗口期（EventManager.OnFinalize
                // 已置 _widgetContainers=null 但 widget 树尚未拆完）ParentWidget 仍非 null，
                // 此时设 IsVisible 会触发 RefreshState → RegisterWidgetForEvent NRE（2026-08-21 实机
                // 家族屏设军需官点完成后崩；详情见下方 catch 注释）。
                if (it.Button.ParentWidget == null || it.Button.Context == null)
                {
                    if (_hoverOn == it.Button) { MBInformationManager.HideInformations(); _hoverOn = null; }
                    _live.RemoveAt(i);
                    continue;
                }
                // 可见性 = 原版槽位绑定结果（hero 条件）+ PlotEnabled 总闸（密聊开关关闭 → 按钮隐藏）。
                // 🔴 2026-08-22（用户裁定分层）：未配置 LLM → 密信按钮同样隐藏（与 M 键/呼出按钮同规则——
                // 传讯入口整体封死；点击兜底仍走 CanOpen 双闸）。
                // 🔴 暂恢复跟随 slot 锚（上一版行为）；IsHeroTarget 判定链路待日志验证后再替换
                bool anchorVisible = it.VisibilityAnchor != null && it.VisibilityAnchor.IsVisible;
                bool isPlayerSelf = false;
                if (anchorVisible && it.ClanRoot != null)
                {
                    // 🔴 2026-08-17（实机反馈）：家族屏详情面板选中**玩家自己**时也显示密信按钮——
                    // 自己给自己发密信无意义，隐藏。每帧 ResolveRowHeroId（家族 = DFS 找 tableau +
                    // 反射读属性）只在「有有效成员选中」时执行（其余帧 anchorVisible=false 短路跳过，
                    // 零 DFS 开销）。
                    try
                    {
                        string selfId = ResolveRowHeroId(it.Button, it.ClanRoot);
                        isPlayerSelf = selfId != null && Hero.MainHero != null && selfId == Hero.MainHero.StringId;
                    }
                    catch { }
                }
                bool wantVisible = anchorVisible && !isPlayerSelf && Settings.Instance.PlotEnabled
                    && Settings.Instance.IsLLMConfigured;
                bool wasVisible = it.Button.IsVisible;
                try
                {
                    it.Button.IsVisible = wantVisible;
                    // 🔴 2026-08-29（用户实机反馈「密信按钮盖住交谈按钮」根因）：注入的按钮是 ButtonsList
                    // 的子节点，引擎 StackLayout 只给**可见**子节点分配布局盒（LayoutLinearHorizontal
                    // 对不可见子节点既不推进 x 也不调用 Layout()，反编译实锤）。若按钮在注入时锚点不可见
                    // （普通士兵行 / 行被 PartyListPanel 回收后数据未判成英雄行），布局盒 = 默认 (0,0)；
                    // 之后行数据变化使锚点可见 → IsVisible 翻转为 true——但 **IsVisible 翻转不触发父列表
                    // 重排** → 按钮仍渲染在列表 (0,0)，正好压住交谈按钮盒 [10..66]（实机覆盖症状）。
                    // 修：false→true 翻转时用 SetSiblingIndex(force: true) 强制整树 measure+layout，
                    // 以可见态重新分配盒（引擎公开 API，1.2.12~1.5.x 签名一致）。
                    if (wantVisible && !wasVisible && it.Button.ParentWidget != null)
                    {
                        it.Button.SetSiblingIndex(it.Button.GetSiblingIndex(), force: true);
                        DebugLogger.Log("[SecretLetter] 可见性翻转 → 强制整树重排（修复按钮布局盒未分配）");
                    }
                }
                catch (Exception ex)
                {
                    // 🔴 2026-08-21（实机）：家族屏设军需官点完成后崩。屏幕销毁窗口期
                    // EventManager.OnFinalize 已置 _widgetContainers=null，树上残留 widget 的
                    // RefreshState → RegisterWidgetForEvent NRE。树已死 → 自清理；若只是面板
                    // 刷新重建（非关屏），0.3s 后 Scan 幂等重注入，无需每帧重试。
                    // 🔴 2026-08-25：光删条目不够——幂等去重依赖 _live，删了之后 0.3s 后 Scan
                    // 会对同一批将死行重注入，形成注入→失败→删除→再注入死循环（实机 17:19:53
                    // 每 0.3s 刷 ~14 行）。抑制注入 2s + 日志 5s 节流（WndProc 异常节流同款）。
                    if (_hoverOn == it.Button) { MBInformationManager.HideInformations(); _hoverOn = null; }
                    _live.RemoveAt(i);
                    _injectSuppressSec = 2f;
                    if (Environment.TickCount - _lastCleanupLogTick > 5000)
                    {
                        _lastCleanupLogTick = Environment.TickCount;
                        DebugLogger.Log($"[SecretLetter] 可见性设置失败（树销毁窗口），自清理: {ex.Message}");
                    }
                    continue;
                }

                // hover 提示（手动 hit-test，ImChatView 缩略模式同款）
                bool over = it.Button.IsVisible && IsPointInRect(Input.MousePositionPixel, it.Button.GlobalPosition, it.Button.Size);
                if (over && _hoverOn != it.Button)
                {
                    if (_hoverOn != null) MBInformationManager.HideInformations();
                    MBInformationManager.ShowHint(GetHintText());
                    _hoverOn = it.Button;
                }
                else if (!over && _hoverOn == it.Button)
                {
                    MBInformationManager.HideInformations();
                    _hoverOn = null;
                }
            }
        }

        private static string GetHintText()
        {
            // 本地化：密信按钮 hover 提示（玩家可见文本）；每次 Resolve（语言切换即时生效，项目惯例）
            return LWNTextHelper.ResolveText(HintKey, "Secret letter: send a private message");
        }

        // ───────────────────────── 点击处理 ─────────────────────────

        private static void OnClicked(Widget w)
        {
            try
            {
                var btn = w as ButtonWidget;
                if (btn == null) return;
                DebugLogger.Log("[SecretLetter] 点击密信按钮");
                var it = _live.FirstOrDefault(x => x.Button == btn);
                if (it == null) { DebugLogger.Log("[SecretLetter] 点击但未找到注入记录"); return; }

                string heroId = ResolveRowHeroId(btn, it.ClanRoot);
                DebugLogger.Log($"[SecretLetter] heroId={(heroId ?? "null")}");
                if (string.IsNullOrEmpty(heroId)) return;
                OpenSecretLetter(heroId);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] 点击处理异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 行→Hero StringId（注入判定与点击共用）：读原版绑定已赋值的 widget 属性（反射，跨版本安全）。
        /// 🔴 队伍行：**不能用 Id 匹配行根**——CustomType 实例化时外层模板 `widget.Id = Id` 覆盖为 null
        ///   （反编译 PrefabSystem CreateWidgets 实锤，`<PartyTroopTuple/>` 引用无 Id → 行根运行时 Id=null）。
        ///   改用类型名 `PartyTroopTupleButtonWidget`（CustomType 下类型保留，每行唯一）→ 读 CharacterID（=Character.StringId）。
        /// 家族行：ClanMembersWidget 根 Id 同样被覆盖且类型是普通 Widget（类型名不唯一）→
        ///   从注入时存的 UIContext.Root DFS 找 CharacterTableauWidget（家族屏全局唯一）→ 读 CharStringId（=character.StringId）。
        /// </summary>
        private static string ResolveRowHeroId(Widget btn, Widget clanRoot)
        {
            try
            {
                if (clanRoot != null)
                {
                    var tableau = FindWidgetByType(clanRoot, "CharacterTableauWidget");
                    var prop = tableau?.GetType().GetProperty("CharStringId", BindingFlags.Instance | BindingFlags.Public);
                    string id = prop?.GetValue(tableau) as string;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        // 🔴 2026-08-21（用户裁定）：tableau 缺失 = 面板刷新/屏幕销毁窗口期的
                        // **正常中间态**（pitfalls.md:622 前置征兆），不是错误，每帧路径不打日志；
                        // 点击失败由 OnClicked 的 heroId=null 日志覆盖（那才是真异常）。
                        return null;
                    }
                    return id.Trim();
                }

                int depth = 0;
                string last = "";
                for (Widget p = btn.ParentWidget; p != null; p = p.ParentWidget)
                {
                    depth++;
                    last = $"{p.GetType().Name}({p.Id})";
                    if (p.GetType().Name == "PartyTroopTupleButtonWidget")
                    {
                        var prop = p.GetType().GetProperty("CharacterID", BindingFlags.Instance | BindingFlags.Public);
                        string id = prop?.GetValue(p) as string;
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            DebugLogger.Log($"[SecretLetter] 行根命中但 CharacterID 读不到: propNull={prop == null} id='{id ?? "null"}' type={p.GetType().Name}");
                            return null;
                        }
                        return id.Trim();
                    }
                    if (depth > 40) break;
                }
                DebugLogger.Log($"[SecretLetter] 行解析失败: 遍历 {depth} 层未命中行根，末层 {last}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] 行映射解析失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 打开密信私聊（探查板 ExecuteSendMessage 同款链路）。
        /// 🔴 2026-08-17（Q2 方案 A'：关屏再开，替换旧「层叠方案」）：**不层叠**——队伍/家族屏
        ///   关闭，目标 Hero 写入 ImChatView pending，下一帧 TopScreen 稳定（回 MapScreen）后
        ///   IM 打开定位私聊（层挂到地图屏，天然避开 PopScreen 过渡期黑屏——旧层叠方案的 ESC 语义
        ///   不干净 + 完整模式滚动缓存失效隐患一并消除）。
        /// 🔴 2026-08-17（黑屏两次复现的根治）：**关闭必须走原版路径**——裸 ScreenManager.PopScreen
        ///   绕过 GameState 栈：CampaignGameState 不恢复 → 地图场景不渲染 → 黑屏（方案 §2 记录 +
        ///   实机复现）。原版实锤：家族屏关闭 = GameStateManager.PopState（GauntletClanScreen
        ///   OnExit/关闭按钮同款）；队伍屏 = PartyScreenHelper.CloseScreen（内部 PopState）。
        ///   队伍屏有未应用变更 → 先走原版 Apply Changes? Inquiry（ExecuteTalk 同款链路，v1.4.8
        ///   反编译实锤）；反射失败 → 降级直接关屏 + 日志（未保存变更会丢——风险表记录）。
        /// </summary>
        private static void OpenSecretLetter(string heroId)
        {
            Hero hero = null;
            try { hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId); } catch { }
            if (hero == null) { DebugLogger.Log($"[SecretLetter] 目标 Hero 不存在: {heroId}"); return; }

            // 前置确认 IM 能开（PlotEnabled 总闸/战斗/系统弹窗）——防点击无效。
            // 🔴 2026-08-25（实机「点击密信后无法呼出 IM」根因修复）：**不能走完整 CanOpen()**——
            // 08-22/23 CanOpen 收紧后，全屏 UI（队伍/家族屏本身）与 Campaign 非地图屏两项闸口
            // 在队伍/家族屏上恒 false → 预检永远失败 → 密信按钮全死（实机日志 [SecretLetter]
            // CanOpen=false 连点无响应）。走 screenStateAgnostic=true：只查静态闸口（配置/战斗/弹窗），
            // 屏状态闸口由关屏后的 pending 处理器（ImChatView.OnScreenFrameTick 关屏完成分支，
            // TopScreen 已是 MapScreen）重新核对完整 CanOpen——最终闸口不丢。
            if (!ImChatView.CanOpen(screenStateAgnostic: true)) { DebugLogger.Log("[SecretLetter] CanOpen=false"); return; }

            var conv = ImChatManager.GetDirectConversation(heroId);
            if (conv == null) { DebugLogger.Log($"[SecretLetter] 会话创建失败: {heroId}"); return; }

            var top = ScreenManager.TopScreen;
            if (top == null) { DebugLogger.Log("[SecretLetter] TopScreen 为空，无法关屏"); return; }
            bool isParty = top.GetType().Name.Contains("PartyScreen");

            // 🔴 队伍屏：未应用变更 → 走原版 Apply Changes? 流程（反射，失败降级）。
            // 确认回调 = 原版关屏 + 写 pending（密信不走 ExecuteOpenConversation——那是原版交谈按钮的路径）。
            if (isParty)
            {
                bool handled = TryApplyPartyChangesThenPop(top, () =>
                {
                    ClosePartyScreenViaHelper();
                    ImChatView.SetPendingSecretLetter(heroId);
                });
                if (handled) return;   // 反射成功：原版 Inquiry 接管（弹窗或已直接确认），密信按钮到此为止
            }

            // 家族屏（无未保存变更概念）：原版关闭路径 = GameStateManager.PopState（GauntletClanScreen
            // OnExit/关闭按钮同款，反编译实锤）——裸 ScreenManager.PopScreen 绕过 GameState 栈 →
            // CampaignGameState 不恢复 → 地图黑屏（两次实机复现）
            CloseCurrentScreenViaGameState(isParty);
            ImChatView.SetPendingSecretLetter(heroId);
            DebugLogger.Log($"[SecretLetter] 关屏（{(isParty ? "队伍屏" : "家族屏")}），pending hero={heroId}");
        }

        /// <summary>队伍屏原版关闭。降级链（🔴 2026-08-17 黑屏教训：必须走 GameState 栈，裸 PopScreen 会黑屏）：
        /// ① PartyScreenHelper.CloseScreen（原版实锤路径，内部 ClosePartyPresentation → GameStateManager.PopState；
        ///    反射调用——v1.2.12 可能无此类/单参签名，Latest 双参）；② GameStateManager.PopState（家族屏同款，
        ///    队伍屏的 PartyState 也是 GameState，直接弹等效）；③ ScreenManager.PopScreen（最后兜底，黑屏风险标注）。</summary>
        private static void ClosePartyScreenViaHelper()
        {
            try
            {
                var helperType = Type.GetType("TaleWorlds.CampaignSystem.PartyScreenHelper, TaleWorlds.CampaignSystem");
                if (helperType == null) { DebugLogger.Log("[SecretLetter] PartyScreenHelper 类型未找到（v1.2.12 兼容）"); }
                else
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    var m2 = helperType.GetMethod("CloseScreen", flags, null, new[] { typeof(bool), typeof(bool) }, null);
                    if (m2 != null) { m2.Invoke(null, new object[] { false, false }); DebugLogger.Log("[SecretLetter] 队伍屏 PartyScreenHelper.CloseScreen 关闭"); return; }
                    var m1 = helperType.GetMethod("CloseScreen", flags, null, new[] { typeof(bool) }, null);
                    if (m1 != null) { m1.Invoke(null, new object[] { false }); DebugLogger.Log("[SecretLetter] 队伍屏 PartyScreenHelper.CloseScreen(单参) 关闭"); return; }
                    DebugLogger.Log("[SecretLetter] CloseScreen 方法未找到（1参/2参均无）");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] PartyScreenHelper.CloseScreen 反射失败: {ex.Message}");
            }
            // ② 降级：GameStateManager.PopState（家族屏已验证不黑屏的原版路径）
            try
            {
                TaleWorlds.Core.Game.Current.GameStateManager.PopState(0);
                DebugLogger.Log("[SecretLetter] 队伍屏降级 PopState 关闭");
                return;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] PopState 降级失败: {ex.Message}");
            }
            // ③ 最后兜底
            DebugLogger.Log("[SecretLetter] 降级 ScreenManager.PopScreen（⚠️ 绕过 GameState 栈，地图可能黑屏）");
            ScreenManager.PopScreen();
        }

        /// <summary>家族/队伍屏原版关闭（GameState 栈路径）：家族 = GameStateManager.PopState（GauntletClanScreen
        /// 关闭按钮同款，反编译实锤）；队伍 = 见 <see cref="ClosePartyScreenViaHelper"/>（本方法 isParty 分支兜底）。
        /// PopState 会同步驱动屏幕栈 Pop——不需要再调 ScreenManager.PopScreen。</summary>
        private static void CloseCurrentScreenViaGameState(bool isParty)
        {
            if (isParty)
            {
                ClosePartyScreenViaHelper();
                return;
            }
            try
            {
                // GauntletClanScreen.OnExit 同款：GameStateManager.PopState(0)（默认参，双版本稳定）
                TaleWorlds.Core.Game.Current.GameStateManager.PopState(0);
                DebugLogger.Log("[SecretLetter] 家族屏 PopState 关闭（原版路径）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] PopState 失败，降级 PopScreen: {ex.Message}");
                ScreenManager.PopScreen();
            }
        }

        /// <summary>
        /// 🔴 Q2（2026-08-17）：队伍屏未应用变更 → 走原版 Apply Changes? Inquiry（反射实现，
        /// ExecuteTalk 反编译链路实锤 v1.4.8：`IsThereAnyChanges && IsDoneActive → ShowInquiry(Apply Changes?)
        /// → 确认后 DoneLogic(isForced:false)`）。反射目标跨版本稳定（PartyVM/PartyScreenLogic 多年未改名），
        /// 失败 → 返回 false 由调用方降级（直接 PopScreen + 日志——未保存变更会丢，风险表记录）。
        /// 确认回调 = 应用成功 → onConfirmed（关屏 + 写 pending）；应用失败 → 原版 Failed 弹窗。
        /// 无变更 → 直接 onConfirmed（不弹窗）。</summary>
        private static bool TryApplyPartyChangesThenPop(ScreenBase top, Action onConfirmed)
        {
            try
            {
                // ① PartyVM 实例：GauntletPartyScreen._dataSource（私有字段，反射读——反编译实锤 v1.4.8）
                var vm = top.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(top);
                if (vm == null) { DebugLogger.Log("[SecretLetter] Apply Changes 反射：PartyVM 获取失败"); return false; }
                var vmType = vm.GetType();

                // ② PartyScreenLogic 实例（PartyVM 属性/字段名不确定——反编译 v1.4.8 为驼峰私有字段
                // partyScreenLogic；按类型名匹配遍历容错，属性优先）
                object logic = null;
                var prop = vmType.GetProperty("PartyScreenLogic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null) logic = prop.GetValue(vm);
                if (logic == null)
                {
                    var field = vmType.GetField("PartyScreenLogic", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null) logic = field.GetValue(vm);
                }
                if (logic == null)
                {
                    foreach (var f in vmType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (f.FieldType.Name == "PartyScreenLogic") { logic = f.GetValue(vm); break; }
                    }
                }
                if (logic == null)
                {
                    foreach (var p in vmType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (p.PropertyType.Name == "PartyScreenLogic") { logic = p.GetValue(vm); break; }
                    }
                }
                if (logic == null) { DebugLogger.Log("[SecretLetter] Apply Changes 反射：PartyScreenLogic 获取失败"); return false; }
                var logicType = logic.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // ③ 无变更 → 不弹窗，直接继续（关屏 + pending）
                if (!((bool?)logicType.GetMethod("IsThereAnyChanges", flags)?.Invoke(logic, null) ?? false))
                {
                    onConfirmed?.Invoke();
                    return true;
                }
                // IsDoneActive false（不能应用）→ Reset 语义复杂，直接降级关屏（原版 ExecuteTalk 的
                // Reset Changes? 分支是「进对话前重置」——密信场景玩家意图是离开，等价关屏）
                if (!((bool?)logicType.GetMethod("IsDoneActive", flags)?.Invoke(logic, null) ?? true))
                {
                    onConfirmed?.Invoke();
                    return true;
                }

                // ④ 原版 Apply Changes? Inquiry（与 ExecuteTalk 完全一致：原版 key + yes/no + 确认回调；
                // GameTexts 在 TaleWorlds.Core（SaveErrorReporter 先例），TextObject 在 TaleWorlds.Localization）
                var yesText = TaleWorlds.Core.GameTexts.FindText("str_yes")?.ToString() ?? "Yes";
                var noText = TaleWorlds.Core.GameTexts.FindText("str_no")?.ToString() ?? "No";
                InformationManager.ShowInquiry(new InquiryData(
                    new TaleWorlds.Localization.TextObject("{=pF0SqQxL}Apply Changes?").ToString(),
                    new TaleWorlds.Localization.TextObject("{=6DuCoCc2}You need to confirm your changes in order to engage in a conversation.").ToString(),
                    true, true, yesText, noText,
                    () =>
                    {
                        try
                        {
                            var doneOk = logicType.GetMethod("DoneLogic", flags)?.Invoke(logic, new object[] { false });
                            if (doneOk is bool b && b)
                                onConfirmed?.Invoke();
                            else
                            {
                                // 原版 Failed to Apply Changes 弹窗
                                var okText = TaleWorlds.Core.GameTexts.FindText("str_ok")?.ToString() ?? "OK";
                                InformationManager.ShowInquiry(new InquiryData(
                                    new TaleWorlds.Localization.TextObject("{=1l4kpBDK}Failed to Apply Changes").ToString(),
                                    new TaleWorlds.Localization.TextObject("{=sFseX1Ka}Could not apply changes.").ToString(),
                                    true, false, okText, string.Empty, null, null));
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[SecretLetter] Apply Changes 确认回调异常: {ex.Message}");
                        }
                    },
                    null));
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SecretLetter] Apply Changes 反射失败，降级直接关屏: {ex.Message}");
                return false;
            }
        }

        // ───────────────────────── 工具 ─────────────────────────

        /// <summary>深度优先按 Id 找 widget（ImChatView.FindWidgetById 同款；家族屏容器/按钮同名，父先命中返回容器）。</summary>
        private static Widget FindWidgetById(Widget root, string id)
        {
            if (root == null) return null;
            if (root.Id == id) return root;
            for (int i = 0; i < root.ChildCount; i++)
            {
                var found = FindWidgetById(root.GetChild(i), id);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>DFS 按类型名找 widget（家族屏 CharacterTableauWidget 定位用，跨版本反射）。</summary>
        private static Widget FindWidgetByType(Widget root, string typeName)
        {
            if (root == null) return null;
            if (root.GetType().Name == typeName) return root;
            for (int i = 0; i < root.ChildCount; i++)
            {
                var found = FindWidgetByType(root.GetChild(i), typeName);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>DFS 收集所有同 Id widget（同一屏多实例：队伍屏左右列表多行）。</summary>
        private static void FindAllWidgetsById(Widget root, string id, List<Widget> outList)
        {
            if (root == null) return;
            if (root.Id == id) outList.Add(root);
            for (int i = 0; i < root.ChildCount; i++)
                FindAllWidgetsById(root.GetChild(i), id, outList);
        }

        private static int IndexOfChild(Widget parent, Widget child)
        {
            for (int i = 0; i < parent.ChildCount; i++)
            {
                if (parent.GetChild(i) == child) return i;
            }
            return -1;
        }

        private static bool IsPointInRect(Vec2 p, Vec2 pos, Vec2 size)
        {
            return p.X >= pos.X && p.X <= pos.X + size.X && p.Y >= pos.Y && p.Y <= pos.Y + size.Y;
        }
    }
}
