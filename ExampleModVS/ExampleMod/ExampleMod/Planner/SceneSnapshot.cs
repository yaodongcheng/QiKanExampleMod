using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // SceneSnapshot.cs — 计划期的世界模型（§3）
    //
    // 采集源分两路（引擎两个遍历入口，互不混淆）：
    //   Agents  ← Mission.Agents（人形活体）：角色表 + 路人
    //   Objects ← Mission 可交互对象（MissionObjects：门/箱/灯/桶/床…）
    // 纯装饰 GameEntity 不进快照（全量 = prompt 爆炸）；命令引用时按需语义查询。
    //
    // 视野矩阵复用 NpcSightSystem.CanAgentSeeTarget。
    // ToPromptText() 纯相对语义描述（相对玩家位置/朝向）；执行期读实时更新的同一模型。
    // ═══════════════════════════════════════════════════════════════

    public class SceneSnapshot
    {
        public List<AgentInfo> Agents = new List<AgentInfo>();
        public List<ObjectInfo> Objects = new List<ObjectInfo>();
        public List<ZoneInfo> Zones = new List<ZoneInfo>();

        /// <summary>可见性缓存：observer.Index → subject.Index → 是否可见（懒计算，会话内有效）。</summary>
        private readonly Dictionary<int, Dictionary<int, bool>> _visibilityCache = new Dictionary<int, Dictionary<int, bool>>();
        private readonly HashSet<(int, int)> _visibilityTested = new HashSet<(int, int)>();

        public class AgentInfo
        {
            public Agent Agent;
            public string Role;          // "player" "self" "guard" "chief" "tavernkeeper"…
            public string DisplayName;
            public string FacingDesc;    // 朝向描述（相对玩家）
            public string PositionDesc;  // 位置描述（相对玩家，如"你左侧 5 米"）
            public string State;         // 站立/蹲下/坐/昏迷/战斗中…
            public string Occupation;    // 职业（CharacterObject.Occupation）
            public string PersonalityHint; // 人设 trait 摘要（模板 NPC 按职业默认）
        }

        public class ObjectInfo
        {
            public MissionObject MissionObject;
            public string Id;            // 场景内唯一 id（名称或类型+序号）
            public string Kind;          // chest / door / barrel / chair / table…
            public string DisplayName;
            public string PositionDesc;
        }

        public class ZoneInfo
        {
            public string Id;
            public Vec3 Position;
            public float Radius = 5f;
            public string DisplayName;
        }

        // ═══════════════════════════════════════════════════════════
        // 采集
        // ═══════════════════════════════════════════════════════════

        /// <summary>构建快照。agentLimit = 0 表示全部（几十~几百，超限近玩家优先采样）。</summary>
        public static SceneSnapshot Build(Mission mission, int agentLimit = 0, bool includeObjects = true)
        {
            var snap = new SceneSnapshot();
            if (mission == null) return snap;

            var player = Agent.Main;
            Vec3 playerPos = player?.Position ?? Vec3.Zero;

            // ① Agents ← Mission.Agents
            var agents = new List<Agent>();
            if (mission.Agents != null)
            {
                foreach (var a in mission.Agents)
                {
                    if (a == null || !a.IsActive()) continue;
                    if (!AgentControlHelper.IsHumanOrChild(a)) continue;
                    agents.Add(a);
                }
            }
            // 超限采样：按距玩家距离排序取近的
            if (agentLimit > 0 && agents.Count > agentLimit)
            {
                agents = agents
                    .OrderBy(a => a.Position.DistanceSquared(playerPos))
                    .Take(agentLimit)
                    .ToList();
            }

            foreach (var a in agents)
            {
                snap.Agents.Add(new AgentInfo
                {
                    Agent = a,
                    Role = BuildRole(a, player),
                    DisplayName = BuildDisplayName(a),
                    FacingDesc = BuildFacingDesc(a, player),
                    PositionDesc = BuildPositionDesc(a.Position, playerPos),
                    State = BuildStateDesc(a),
                    Occupation = BuildOccupation(a),
                    PersonalityHint = BuildPersonalityHint(a),
                });
            }

            // ② Objects ← 可交互对象
            if (includeObjects && mission.MissionObjects != null)
            {
                int chestIdx = 0, doorIdx = 0, barrelIdx = 0, chairIdx = 0, otherIdx = 0;
                foreach (var obj in mission.MissionObjects)
                {
                    if (obj == null) continue;
                    Vec3 pos = GetMissionObjectPosition(obj);
                    string kind = ClassifyObject(obj);
                    string id;
                    switch (kind)
                    {
                        case "chest": id = $"chest_{chestIdx++}"; break;
                        case "door": id = $"door_{doorIdx++}"; break;
                        case "barrel": id = $"barrel_{barrelIdx++}"; break;
                        case "chair": id = $"chair_{chairIdx++}"; break;
                        default: id = $"object_{otherIdx++}"; break;
                    }
                    snap.Objects.Add(new ObjectInfo
                    {
                        MissionObject = obj,
                        Id = id,
                        Kind = kind,
                        DisplayName = BuildObjectDisplayName(kind, id),
                        PositionDesc = BuildPositionDesc(pos, playerPos),
                    });
                }
            }

            // ③ Zones：语义 tag 探测——场景作者打的语义 tag（door/gate/entrance 等）可作区域锚点；
            // 原生场景通常只有 sp_/ai_ 系统 tag，没有语义 tag → 探测失败 = 空（LLM 看不到锚点就不会引用）。
            // 铁律 5：不硬编码资源 ID——tag 探测是运行时遍历，被其他 mod 改动场景也安全。
            CollectSemanticZones(mission, snap);

            return snap;
        }

        /// <summary>语义 tag 探测：Scene.FindEntityWithTag 尝试常见语义 tag，命中即注册为区域（半径 8m）。
        /// 查不到 = 区域不存在 → 计划里 zone(名称) 解析失败走失败路径（诚实报告），不硬编码坐标。
        /// 🔴 internal：WorldFactProvider 位置描述（最近语义区域附加）复用同源列表。</summary>
        internal static readonly string[] SemanticZoneTags =
        {
            "door", "gate", "entrance", "exit", "alley", "river", "bridge",
            "meet_point", "watch_point", "market", "well",
        };

        private static void CollectSemanticZones(Mission mission, SceneSnapshot snap)
        {
            if (mission?.Scene == null) return;
            foreach (var tag in SemanticZoneTags)
            {
                try
                {
                    var entity = mission.Scene.FindEntityWithTag(tag);
                    if (entity == null) continue;
                    snap.Zones.Add(new ZoneInfo
                    {
                        Id = tag,
                        Position = entity.GlobalPosition,
                        Radius = 8f,
                        DisplayName = tag,
                    });
                }
                catch { }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 查询
        // ═══════════════════════════════════════════════════════════

        /// <summary>角色自动打标（铁律 5：不硬编码 ID——按 StringId 关键词/职业匹配语义角色）。
        /// 玩家 = "player"；守卫/村民/商人/酒馆老板/村长/醉汉 等按模板 StringId 关键词。</summary>
        private static string BuildRole(Agent a, Agent player)
        {
            if (a == player) return "player";
            try
            {
                string id = a.Character?.StringId ?? "";
                string low = id.ToLowerInvariant();
                if (low.Contains("guard")) return "guard";
                if (low.Contains("tavernkeeper")) return "tavernkeeper";
                if (low.Contains("merchant")) return "merchant";
                if (low.Contains("drunkard")) return "drunkard";
                if (low.Contains("notable") || low.Contains("headman")) return "chief";
                if (low.Contains("villager") || low.Contains("peasant")) return "villager";
                if (low.Contains("lord") || low.Contains("hero")) return "hero";
            }
            catch { }
            return null;
        }

        /// <summary>按角色/名称/职业/子串匹配 Agent（多匹配取离玩家最近）。</summary>
        public AgentInfo FindAgent(string roleOrName)
        {
            if (string.IsNullOrEmpty(roleOrName)) return null;
            var playerPos = Agent.Main?.Position ?? Vec3.Zero;
            AgentInfo best = null;
            float bestDist = float.MaxValue;
            string lower = roleOrName.ToLowerInvariant();
            foreach (var info in Agents)
            {
                bool match = false;
                // ① 角色精确匹配（快照自动打标：guard/villager/merchant…）
                if (info.Role != null && string.Equals(info.Role, roleOrName, StringComparison.OrdinalIgnoreCase))
                    match = true;
                // ② 显示名精确匹配
                if (!match && string.Equals(info.DisplayName, roleOrName, StringComparison.OrdinalIgnoreCase))
                    match = true;
                if (!match && info.Agent?.Character != null)
                {
                    // ③ StringId / 名称精确匹配
                    if (string.Equals(info.Agent.Character.StringId, roleOrName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(info.Agent.Character.Name?.ToString(), roleOrName, StringComparison.OrdinalIgnoreCase))
                        match = true;
                    // ④ 职业关键词子串匹配（"guard" 匹配 "guard_empire_mace"；LLM 引用"守卫"这类语义词）
                    else if (info.Agent.Character.StringId != null
                        && info.Agent.Character.StringId.ToLowerInvariant().Contains(lower))
                        match = true;
                    else if (info.Occupation != null && info.Occupation.ToLowerInvariant().Contains(lower))
                        match = true;
                }
                if (!match) continue;
                float d = info.Agent.Position.DistanceSquared(playerPos);
                if (d < bestDist) { bestDist = d; best = info; }
            }
            return best;
        }

        /// <summary>按名称/类型匹配可交互对象（多匹配取离玩家最近）。</summary>
        public ObjectInfo FindObject(string nameOrKind)
        {
            if (string.IsNullOrEmpty(nameOrKind)) return null;
            var playerPos = Agent.Main?.Position ?? Vec3.Zero;
            ObjectInfo best = null;
            float bestDist = float.MaxValue;
            foreach (var info in Objects)
            {
                bool match = string.Equals(info.Kind, nameOrKind, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(info.Id, nameOrKind, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(info.DisplayName, nameOrKind, StringComparison.OrdinalIgnoreCase)
                    || info.DisplayName.IndexOf(nameOrKind, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) continue;
                float d = GetMissionObjectPosition(info.MissionObject).DistanceSquared(playerPos);
                if (d < bestDist) { bestDist = d; best = info; }
            }
            return best;
        }

        /// <summary>按 Zone id 查找。</summary>
        public ZoneInfo FindZone(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Zones.FirstOrDefault(z => string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>可见性矩阵（懒计算 + 缓存）：observer 能否看到 subject。</summary>
        public bool CanSee(Agent observer, Agent subject)
        {
            if (observer == null || subject == null) return false;
            var key = (observer.Index, subject.Index);
            if (!_visibilityTested.Contains(key))
            {
                _visibilityTested.Add(key);
                try
                {
                    _visibilityCache.GetOrAddDefault(observer.Index)[subject.Index] = NpcSightSystem.CanAgentSeeTarget(observer, subject);
                }
                catch
                {
                    _visibilityCache.GetOrAddDefault(observer.Index)[subject.Index] = false;
                }
            }
            return _visibilityCache.TryGetValue(observer.Index, out var inner)
                && inner.TryGetValue(subject.Index, out var v) && v;
        }

        // ═══════════════════════════════════════════════════════════
        // Prompt 文本
        // ═══════════════════════════════════════════════════════════

        /// <summary>快照 → prompt 纯相对语义文本（角色表 + 区域）。
        /// 注意：可互动物件段已临时去掉——原生场景 121 个匿名「物件（object）」全是噪声，
        /// 等按有意义 tag 重新设计后再恢复（收集逻辑 Objects/ClassifyObject 保留）。</summary>
        public string ToPromptText()
        {
            var sb = new StringBuilder();
            var playerPos = Agent.Main?.Position ?? Vec3.Zero;
            sb.AppendLine($"【场景当前人员】（{Agents.Count} 人）");
            // 同名同职业的模板 NPC 合并成一条（英雄逐条列）——省 token 且信息不丢：
            // 同一名字分布在多处 → "×N：大概方位（最近-最远距离）"
            foreach (var group in Agents.GroupBy(i => $"{i.DisplayName}|{i.Occupation}"))
            {
                var list = group.ToList();
                if (list.Count == 1)
                {
                    var info = list[0];
                    sb.Append("- ");
                    if (info.Role != null) sb.Append($"[{info.Role}] ");
                    sb.Append(info.DisplayName);
                    if (!string.IsNullOrEmpty(info.Occupation)) sb.Append($"（{info.Occupation}）");
                    sb.Append($"：{info.PositionDesc}，{info.FacingDesc}，{info.State}");
                    if (!string.IsNullOrEmpty(info.PersonalityHint)) sb.Append($"（{info.PersonalityHint}）");
                    sb.AppendLine();
                }
                else
                {
                    // 合并行：名字 + 人数 + 方位范围（方向取首条，距离取组内最近/最远）
                    sb.Append("- ");
                    var role = list.FirstOrDefault(i => i.Role != null)?.Role;
                    if (role != null) sb.Append($"[{role}] ");
                    sb.Append(list[0].DisplayName);
                    if (!string.IsNullOrEmpty(list[0].Occupation)) sb.Append($"（{list[0].Occupation}）");
                    var dists = list
                        .Where(i => i.Agent != null)
                        .Select(i => i.Agent.Position.Distance(playerPos))
                        .OrderBy(d => d)
                        .ToList();
                    string range = dists.Count == 1
                        ? $"{dists[0]:F0}米"
                        : $"{dists[0]:F0}-{dists[dists.Count - 1]:F0}米";
                    string dirWord = DirWordOf(list[0].PositionDesc);
                    sb.Append($"×{list.Count}：{dirWord}{range}");
                    sb.AppendLine();
                }
            }
            if (Zones.Count > 0)
            {
                sb.AppendLine($"【场景区域锚点】（{Zones.Count} 个）");
                foreach (var z in Zones)
                    sb.AppendLine($"- {z.DisplayName ?? z.Id}：{BuildPositionDesc(z.Position, Agent.Main?.Position ?? Vec3.Zero)}");
            }
            return sb.ToString();
        }

        /// <summary>位置描述的方向词部分（"你西南侧4米" → "你西南侧"；"你身旁1米" → "你身旁"）。</summary>
        private static string DirWordOf(string posDesc)
        {
            if (string.IsNullOrEmpty(posDesc)) return "";
            int i = 0;
            while (i < posDesc.Length && !char.IsDigit(posDesc[i])) i++;
            return posDesc.Substring(0, i);
        }

        // ═══════════════════════════════════════════════════════════
        // 描述构建
        // ═══════════════════════════════════════════════════════════

        private static string BuildDisplayName(Agent a)
        {
            if (a == Agent.Main) return "玩家";
            try
            {
                var heroObj = (a.Character as CharacterObject)?.HeroObject;
                if (heroObj != null && !string.IsNullOrWhiteSpace(heroObj.Name?.ToString()))
                    return heroObj.Name.ToString();
                if (!string.IsNullOrWhiteSpace(a.Name)) return a.Name;
                return a.Character?.Name?.ToString() ?? "路人";
            }
            catch { return a.Name ?? "路人"; }
        }

        private static string BuildObjectDisplayName(string kind, string id)
        {
            switch (kind)
            {
                case "chest": return "箱子";
                case "door": return "门";
                case "barrel": return "木桶";
                case "chair": return "桌椅";
                default: return "物件";
            }
        }

        private static string BuildPositionDesc(Vec3 pos, Vec3 playerPos)
        {
            float dx = pos.x - playerPos.x;
            float dy = pos.y - playerPos.y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            string dir;
            if (dist < 1.2f) dir = "你身旁";
            else
            {
                float ang = MathF.Atan2(dy, dx);
                float deg = ang * (180f / MathF.PI);
                // 玩家面朝方向为正前——用屏幕方位近似（相对世界轴，简化：以玩家朝向为参考不做旋转，写方位词）
                if (MathF.Abs(deg) < 30f) dir = "你东侧";
                else if (deg >= 30f && deg < 90f) dir = "你东南侧";
                else if (deg >= 90f && deg < 150f) dir = "你南侧";
                else if (deg >= 150f || deg <= -150f) dir = "你西侧";
                else if (deg < -90f) dir = "你北侧";
                else dir = "你西南侧";
            }
            return $"{dir}{dist:F0}米";
        }

        private static string BuildFacingDesc(Agent a, Agent player)
        {
            try
            {
                if (a == null) return "未知朝向";
                if (player != null)
                {
                    Vec2 look = a.LookDirection.AsVec2.Normalized();
                    Vec2 toPlayer = (player.Position - a.Position).AsVec2.Normalized();
                    float dot = Vec2.DotProduct(look, toPlayer);
                    if (dot > 0.6f) return "面朝玩家";
                    if (dot < -0.6f) return "背对玩家";
                    return "侧身对着玩家";
                }
            }
            catch { }
            return "朝向未知";
        }

        private static string BuildStateDesc(Agent a)
        {
            if (a == null || !a.IsActive()) return "不在场";
            try
            {
                var brain = AgentAIController.GetBrainForAgent(a);
                if (brain != null)
                {
                    var intent = brain.CurrentIntent;
                    if (intent != null)
                    {
                        if (intent.Type == NpcIntentType.Fighting) return "战斗中";
                        if (intent.Type == NpcIntentType.KnockedOut) return "昏迷";
                        if (intent.Type == NpcIntentType.Confronting) return "警戒质问中";
                        if (intent.Type == NpcIntentType.Following) return "跟随中";
                        if (intent.Type == NpcIntentType.Interacting) return "互动中";
                        if (intent.Type == NpcIntentType.ExecutingCommand) return "执行命令中";
                        if (intent.Type == NpcIntentType.Surrendering) return "想认输";
                    }
                    var phase = brain.AlertPhase;
                    if (phase == AlarmPhase.Alarmed) return "高度警戒";
                    if (phase == AlarmPhase.Cautious) return "警惕";
                    if (phase == AlarmPhase.Suspicious) return "起疑";
                }
                if (a.CrouchMode) return "蹲着";
                if (a.IsSitting()) return "坐着";
            }
            catch { }
            return "站着";
        }

        private static string BuildOccupation(Agent a)
        {
            try
            {
                var c = a.Character;
                if (c == null) return "";
                if (a == Agent.Main) return "";   // 玩家行已有 [player] 标记，不再打"有名人物"标签
                if ((c as CharacterObject)?.HeroObject != null) return "有名人物";
                string id = c.StringId ?? "";
                if (id.Contains("guard")) return "守卫";
                if (id.Contains("villager")) return "村民";
                if (id.Contains("merchant")) return "商人";
                if (id.Contains("tavernkeeper")) return "酒馆老板";
                if (id.Contains("notable") || id.Contains("headman")) return "村长/乡绅";
                return id.Replace('_', ' ');
            }
            catch { return ""; }
        }

        private static string BuildPersonalityHint(Agent a)
        {
            // 模板 NPC 无记忆档案 → 职业默认提示；Hero 走 NPCProfile（如有）
            try
            {
                var c = a.Character;
                if (c == null) return "";
                if (a == Agent.Main) return "";   // 玩家是命令者，不需要行为提示
                if ((c as CharacterObject)?.HeroObject != null) return "有名人物，行为可观测";
                string id = c.StringId ?? "";
                if (id.Contains("guard")) return "尽职尽责，坚守岗位";
                if (id.Contains("villager")) return "普通村民";
                if (id.Contains("merchant")) return "精于算计";
                if (id.Contains("tavernkeeper")) return "八面玲珑";
                if (id.Contains("drunkard")) return "醉醺醺";
                return "";
            }
            catch { return ""; }
        }

        private static string ClassifyObject(MissionObject obj)
        {
            // 读实体名（双版本 GameEntity/WeakGameEntity）再分类——全按 Kind 关键词
            string name = "";
            try
            {
#if !MB2_V1212
                var wge = obj.GameEntity;
                if (wge.IsValid) name = wge.Name ?? "";
#else
                GameEntity entity = obj.GameEntity;
                if (entity != null) name = entity.Name ?? "";
#endif
            }
            catch { }
            string lower = name.ToLowerInvariant();
            if (lower.Contains("chest") || lower.Contains("box") || lower.Contains("locker")) return "chest";
            if (lower.Contains("door") || lower.Contains("gate")) return "door";
            if (lower.Contains("barrel")) return "barrel";
            if (lower.Contains("chair") || lower.Contains("table")) return "chair";
            return "object";
        }

        /// <summary>MissionObject 位置（双版本：GameEntity / WeakGameEntity）。</summary>
        public static Vec3 GetMissionObjectPosition(MissionObject obj)
        {
            try
            {
#if !MB2_V1212
                var wge = obj.GameEntity;
                if (wge.IsValid) return wge.GlobalPosition;
#else
                GameEntity entity = obj.GameEntity;
                if (entity != null) return entity.GlobalPosition;
#endif
            }
            catch { }
            return Vec3.Zero;
        }
    }

    internal static class DictionaryExtensions
    {
        public static TValue GetOrAddDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key) where TValue : new()
        {
            if (!dict.TryGetValue(key, out var v))
            {
                v = new TValue();
                dict[key] = v;
            }
            return v;
        }
    }
}
