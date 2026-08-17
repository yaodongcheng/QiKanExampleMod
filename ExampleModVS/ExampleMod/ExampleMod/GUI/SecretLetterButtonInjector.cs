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
        private const string HintKey = "LWN_im_secret_letter_hint";
        private const float ScanIntervalSec = 0.3f;

        private static float _scanTimer;
        private static Widget _hoverOn;            // 当前 hover 的注入按钮（防 ShowHint 刷屏）
        private static readonly List<Injected> _live = new List<Injected>();

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
                UpdateLive();
                _scanTimer += dt;
                if (_scanTimer >= ScanIntervalSec)
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
        /// 插入点 = talkWidget 所在槽位容器的父级（队伍=ButtonsList / 家族=LastSeenLocationParent），index+1 紧跟交谈按钮。
        /// </summary>
        private static bool InjectNextToSlot(Widget talkWidget, bool isParty, Widget root)
        {
            if (talkWidget == null) return false;
            // 队伍：talkWidget = TalkButton 按钮 → slot = 其父（ButtonsList 的槽位容器）
            // 家族：talkWidget = Id=TalkToHeroButton 的容器（FindWidgetById 父先命中）→ slot = 自身
            Widget slot = isParty ? talkWidget.ParentWidget : talkWidget;
            if (slot == null || slot.ParentWidget == null) return false;
            // 幂等：同一父容器已注入过则跳过（_live 引用判断，不依赖 Id——比 FindWidgetById 可靠）
            foreach (var x in _live)
            {
                if (x.Button.ParentWidget == slot.ParentWidget) return false;
            }

            // 🔴 可见性锚：队伍行 = TalkButton 父容器（@IsTalkableCharacter）；
            //   家族屏不能跟随 TalkToHeroButton 容器（@IsTalkVisible 在非本队成员行 = false——召回按钮显示时交谈按钮隐藏）
            //   → 改为向上找「子树含 CharacterTableauWidget 的最近祖先」= 详情面板容器（@IsAnyValidMemberSelected，选中有效成员即显示）
            Widget anchor = slot;
            if (!isParty)
            {
                for (Widget p = slot.ParentWidget; p != null; p = p.ParentWidget)
                {
                    if (FindWidgetByType(p, "CharacterTableauWidget") != null) { anchor = p; break; }
                }
            }

            var btn = CreateButton(slot.Context);
            int idx = IndexOfChild(slot.ParentWidget, slot);
            if (idx < 0) return false;
            slot.ParentWidget.AddChildAtIndex(btn, idx + 1);
            _live.Add(new Injected { Button = btn, VisibilityAnchor = anchor, ClanRoot = isParty ? null : root, IsHeroTarget = ResolveIsHeroTarget(btn, isParty ? null : root) });
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

        /// <summary>构造密信按钮：原版交谈槽位同款样式（Party.TalkSlot.Background + talk_icon 变体）。</summary>
        private static ButtonWidget CreateButton(UIContext context)
        {
            var btn = new ButtonWidget(context)
            {
                Id = ButtonId,
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
                if (it.Button.ParentWidget == null)
                {
                    // 屏幕/树已销毁 → 自清理
                    if (_hoverOn == it.Button) { MBInformationManager.HideInformations(); _hoverOn = null; }
                    _live.RemoveAt(i);
                    continue;
                }
                // 可见性 = 原版槽位绑定结果（hero 条件）+ PlotEnabled 总闸（密聊开关关闭 → 按钮隐藏）。
                // 🔴 暂恢复跟随 slot 锚（上一版行为）；IsHeroTarget 判定链路待日志验证后再替换
                bool anchorVisible = it.VisibilityAnchor != null && it.VisibilityAnchor.IsVisible;
                it.Button.IsVisible = anchorVisible && Settings.Instance.PlotEnabled;

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
                        DebugLogger.Log($"[SecretLetter] 家族 tableau 定位成功但 CharStringId 读不到: tableau={tableau != null} propNull={prop == null} id='{id ?? "null"}'");
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
        /// 🔴 层叠方案（2026-08-17 实机修复）：**不关屏**——IM 层（层序 400）直接叠在队伍屏/家族屏上。
        ///   之前「PopScreen + 延迟 0.1s 开 IM」实测黑屏：PopScreen 后底层屏激活是帧边界异步的，
        ///   过渡期内 TopScreen 存在但层未渲染，IM 层叠在空屏上（日志实锤：Open 成功+引导提示弹出，屏幕全黑）。
        ///   层叠方案无屏幕切换时序；ESC 双消费（IM 与屏各自处理 ESC）实测后处理。
        /// </summary>
        private static void OpenSecretLetter(string heroId)
        {
            Hero hero = null;
            try { hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId); } catch { }
            if (hero == null) { DebugLogger.Log($"[SecretLetter] 目标 Hero 不存在: {heroId}"); return; }

            // 前置确认 IM 能开（PlotEnabled 总闸/战斗/系统弹窗）——防点击无效
            if (!ImChatView.CanOpen()) { DebugLogger.Log("[SecretLetter] CanOpen=false"); return; }

            var conv = ImChatManager.GetDirectConversation(heroId);
            if (conv == null) { DebugLogger.Log($"[SecretLetter] 会话创建失败: {heroId}"); return; }

            // 🔴 打开前 TouchDirectChat：左栏「最近私聊」列表来自 ImChatStore._directIndex（只有收发过消息才登记），
            // GetDirectConversation 本身不写索引——不 touch 则左栏看不到该私聊频道（实测：完整模式无、发消息后缩略模式才出现）
            ImChatStore.TouchDirectChat(heroId, ImChatManager.NowUnixMs());

            bool opened = ImChatView.Open(conv);
            DebugLogger.Log($"[SecretLetter] ImChatView.Open={opened}");
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
