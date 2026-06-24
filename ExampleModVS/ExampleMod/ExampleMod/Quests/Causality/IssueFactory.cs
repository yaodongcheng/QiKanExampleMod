using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 原版 Issue 工厂——统一反射，为 ScheduleIssue 因果链创建原版 Issue。
    ///
    /// 核心思路：不依赖原版 IssueBehavior 的私有工厂委托，直接通过反射
    /// 构造 IssueBase 实例，包装为自定义 PotentialIssueData.StartIssueDelegate，
    /// 然后调用 IssueManager.CreateNewIssue()。
    ///
    /// 与 InvokeQuestAcceptedConsequences 同模式——三个委托名逐个试：
    /// OnStartIssue / OnSelected / OnIssueSelected
    /// </summary>
    public static class IssueFactory
    {
        /// <summary>
        /// Issue 类型名 → Type 缓存（避免重复 Assembly 扫描）。
        /// </summary>
        private static readonly Dictionary<string, Type> _issueTypeCache = new Dictionary<string, Type>();

        /// <summary>
        /// IssueBehavior 类型 → 工厂委托方法名 缓存。
        /// 每个 IssueBehavior 的 OnCheckForIssue 中使用的方法名是固定的。
        /// </summary>
        private static readonly string[] FactoryMethodNames = { "OnStartIssue", "OnSelected", "OnIssueSelected" };

        /// <summary>
        /// 为指定 Hero 创建原版 Issue，通过 IssueManager.CreateNewIssue 正式注册。
        /// 用于 ScheduleIssue 因果链后续——绕过随机调度，直接给特定 NPC 发布特定 Issue。
        /// </summary>
        /// <param name="vanillaQuestId">VANILLA_* ID（如 "VANILLA_EscortMerchantCaravan"）</param>
        /// <param name="hero">目标 NPC</param>
        /// <param name="relatedObject">可选的关联对象（藏身处/定居点/敌对 Hero 等）</param>
        /// <returns>创建的 IssueBase 实例，失败返回 null</returns>
        public static IssueBase CreateVanillaIssue(string vanillaQuestId, Hero hero, object relatedObject = null)
        {
            if (string.IsNullOrEmpty(vanillaQuestId) || hero == null) return null;

            // 1. VANILLA_* ID → Issue 类型名
            string issueTypeName = VanillaQuestMapping.GetIssueTypeNameForId(vanillaQuestId);
            if (string.IsNullOrEmpty(issueTypeName))
            {
                DebugLogger.Log($"[IssueFactory] 未知 VANILLA ID: {vanillaQuestId}");
                return null;
            }

            // 2. 查找 Issue Type
            Type issueType = FindIssueType(issueTypeName);
            if (issueType == null)
            {
                DebugLogger.Log($"[IssueFactory] 找不到 Issue 类型: {issueTypeName}");
                return null;
            }

            // 3. NPC 已有 Issue → 跳过（不覆盖）
            if (hero.Issue != null)
            {
                DebugLogger.Log($"[IssueFactory] {hero.Name} 已有 Issue({hero.Issue.GetType().Name})，跳过 {issueTypeName}");
                return null;
            }

            // 4. 尝试构造 IssueBase
            IssueBase issue = ConstructIssue(issueType, hero, relatedObject);
            if (issue == null)
            {
                DebugLogger.Log($"[IssueFactory] 构造 {issueTypeName} 失败（无匹配构造函数）");
                return null;
            }

            // 5. 构造 PotentialIssueData + 调 CreateNewIssue
            if (!TryRegisterIssue(issue, issueType, hero))
            {
                DebugLogger.Log($"[IssueFactory] CreateNewIssue 失败: {issueTypeName} → {hero.Name}");
                return null;
            }

            DebugLogger.Log($"[IssueFactory] 成功创建原版 Issue: {issueTypeName} → {hero.Name}");
            return issue;
        }

        /// <summary>
        /// 尝试创建 Issue 实例。按构造函数复杂度从简到繁逐个试。
        /// </summary>
        private static IssueBase ConstructIssue(Type issueType, Hero hero, object relatedObject)
        {
            // ① 尝试从 IssueBehavior 拿到原版工厂委托（最优——保留 RelatedObject 等捕获状态）
            var result = TryConstructViaBehaviorDelegate(issueType, hero);
            if (result != null) return result;

            // ② Activator 直接构造（次优——不依赖 Behavior 实例）
            // 构造参数组合（从简到繁）
            var paramSets = new List<object[]>
            {
                new object[] { hero },                          // (Hero)
            };

            if (relatedObject != null)
            {
                paramSets.Add(new object[] { hero, relatedObject });
            }

            foreach (var args in paramSets)
            {
                try
                {
                    var issue = Activator.CreateInstance(issueType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, args, null) as IssueBase;
                    if (issue != null) return issue;
                }
                catch
                {
                    // 参数不匹配，继续试下一组
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试从原版 IssueBehavior 实例获取工厂委托并调用。这是最优路径，
        /// 因为工厂委托内部可能捕获了额外状态（如特定 Hideout、关联 NPC 等）。
        /// 行为对齐 InvokeQuestAcceptedConsequences：三个方法名逐个试。
        /// </summary>
        private static IssueBase TryConstructViaBehaviorDelegate(Type issueType, Hero hero)
        {
            // Issue 是嵌套类 → 父类 = IssueBehavior
            var behaviorType = issueType.DeclaringType;
            if (behaviorType == null || !typeof(CampaignBehaviorBase).IsAssignableFrom(behaviorType))
                return null;

            // 从 Campaign 获取运行中的 Behavior 实例
            var behaviorInstance = GetBehaviorInstance(behaviorType);
            if (behaviorInstance == null) return null;

            foreach (var methodName in FactoryMethodNames)
            {
                var method = behaviorType.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) continue;

                try
                {
                    // 构造 PotentialIssueData（相关对象传 null，工厂内部可能不需要）
                    var pid = new PotentialIssueData(
                        issueType,
                        IssueBase.IssueFrequency.Common);

                    // 反射调用：method.Invoke(behavior, pid, hero)
                    // 注意：签名是 (in PotentialIssueData pid, Hero issueOwner)
                    var issue = method.Invoke(behaviorInstance,
                        new object[] { pid, hero }) as IssueBase;
                    if (issue != null)
                        return issue;
                }
                catch (TargetInvocationException)
                {
                    // 这个工厂的条件不满足（如 RequiredObject 为 null），试下一个方法名
                }
                catch
                {
                    // 签名不匹配等
                }
            }

            return null;
        }

        /// <summary>
        /// 通过 IssueManager.CreateNewIssue 注册 Issue。
        /// 构造自定义 PotentialIssueData，工厂委托直接返回已构造好的 IssueBase。
        /// </summary>
        private static bool TryRegisterIssue(IssueBase issue, Type issueType, Hero hero)
        {
            try
            {
                // 工厂委托：直接返回已构造的 IssueBase（无需再生产）
                IssueBase Factory(in PotentialIssueData pid, Hero owner) => issue;

                var pid = new PotentialIssueData(
                    Factory,
                    issueType,
                    IssueBase.IssueFrequency.Common);

                return Campaign.Current.IssueManager.CreateNewIssue(pid, hero);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[IssueFactory] CreateNewIssue 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从 CampaignBehaviorManager 拿运行中的 Behavior 实例。
        /// 走 GetCampaignBehavior&lt;T&gt; 泛型反射。
        /// </summary>
        private static CampaignBehaviorBase GetBehaviorInstance(Type behaviorType)
        {
            try
            {
                var getBehaviorMethod = typeof(Campaign)
                    .GetMethod("GetCampaignBehavior", Type.EmptyTypes)
                    ?.MakeGenericMethod(behaviorType);
                if (getBehaviorMethod == null) return null;

                return getBehaviorMethod.Invoke(Campaign.Current, null) as CampaignBehaviorBase;
            }
            catch
            {
                // 该 Behavior 未注册到 Campaign
                return null;
            }
        }

        /// <summary>
        /// 按 Issue 类型名查找 Type。搜索 CampaignSystem + SandBox 两个程序集。
        /// </summary>
        private static Type FindIssueType(string issueTypeName)
        {
            if (_issueTypeCache.TryGetValue(issueTypeName, out var cached))
                return cached;

            // 搜索所有已加载的程序集
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // 只搜 TaleWorlds 和 SandBox 相关程序集（跳过系统程序集）
                string asmName = asm.GetName().Name;
                if (!asmName.StartsWith("TaleWorlds") && asmName != "SandBox")
                    continue;

                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == issueTypeName && typeof(IssueBase).IsAssignableFrom(type))
                        {
                            _issueTypeCache[issueTypeName] = type;
                            return type;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // 程序集加载失败，跳过
                }
            }

            return null;
        }
    }
}
