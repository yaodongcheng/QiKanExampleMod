using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 伤害模型 Culture-null 空保护（通用方案 X3，2026-09-02 用户裁定）。
    ///
    /// 崩溃现场（实机 2026-09-02）：织丰 Shokuho.CustomCampaign.CustomLocations.models.ShokuhoSandboxAgentApplyDamageModel
    ///   .CalculateDamage 第 288 行 `if (characterObject.Culture.IsBandit)` —— Culture 为 null → NRE 炸穿近战。
    ///   起因：CharacterObject.Culture 在 XML 加载期按 `culture="id"` 字符串解析（ReadObjectReferenceFromXml），
    ///   引用的文化 id 未注册（自定义文化 + 多 mod 加载序/屏蔽）→ null。
    ///   原版 SandBox.SandboxAgentApplyDamageModel 同段代码同款裸解引用（原版世界观下 Culture 恒定非空，
    ///   从不触发）——「文化必非空」假设被任何替换世界观/多 mod 环境打破即是隐患，不是织丰一家的问题。
    ///
    /// 方案（为什么不是「补 get_Culture 回兜底文化」）：崩溃点所需的唯一知识是「角色是不是流寇」，
    ///   不是「他具体属于哪个文化」。回一个文化没有依据（每个 mod 世界观不同，任意兜底 = 隐蔽错配），
    ///   因此不编造文化，只把 `get_Culture + IsBandit` 判定改为「Culture 为 null → false（非流寇）」。
    ///   兜底的只有「非流寇」这一项判断，其余伤害计算语义不变。
    ///
    /// 覆盖策略（无织丰探测、无字符串探针）：枚举所有已加载程序集里 AgentApplyDamageModel 的非抽象
    ///   子类（原版 SandBox.SandboxAgentApplyDamageModel、织丰与任何 mod 的自定义伤害模型），逐个对其
    ///   打 Transpiler（模式匹配按方法名稳定，pattern 未命中则原样透传 = 补丁 no-op，绝不改坏 IL）。
    ///   方法名覆盖两代 API：旧结构 CalculateDamage（1.2.x~1.4.x）；1.5.x 重构后的 ApplyDamageAmplifications
    ///   （dnlib 实况：1.5.2 原版模型 IsBandit 站对在此方法，2 处）。
    ///   挂载时机：LoadSubModules（TaleWorlds.MountAndBlade.dll:102627）先把所有激活模块 DLL
    ///   装配进 AppDomain、全部完毕才回调 OnSubModuleLoad → 本安装点枚举必见全部模型，一次性挂载。
    ///
    /// 修法：两处 `callvirt CharacterObject::get_Culture + callvirt BasicCultureObject::get_IsBandit`
    ///   （攻击者/受害者各一处；dnlib 实况偏移 0xAE3/0xB22 起，无分支目标、不在异常块）
    ///   整体替换为单条 `call SafeIsBandit(BasicCharacterObject)` —— 栈效果一致（[角色] → [bool]），
    ///   替换后除「Culture==null 不再 NRE 而返回 false」外逐字节等价。
    /// </summary>
    public static class AgentDamageModelCultureNullFix
    {
        /// <summary>
        /// 各代伤害模型 API 中「含有 Culture.IsBandit 站对」的方法名并集（旧 CalculateDamage +
        /// 1.5.x 拆分的全部 ApplyDamage* 候选；pattern 不中即 no-op，多列无害）
        /// </summary>
        private static readonly string[] DamageModelMethodNames =
        {
            "CalculateDamage",
            "ApplyDamageAmplifications",
            "ApplyDamageScaling",
            "ApplyDamageReductions",
            "ApplyGeneralDamageModifiers",
        };

        /// <summary>已 patch 的方法（按 类型全名.方法名 去重），防兜底重试双 patch</summary>
        private static readonly HashSet<string> _patchedMethods = new HashSet<string>();

        /// <summary>日志去重：Culture-null 角色只记首次</summary>
        private static readonly HashSet<string> _loggedCharacters = new HashSet<string>();

        private static readonly MethodInfo SafeIsBanditMethod =
            AccessTools.Method(typeof(AgentDamageModelCultureNullFix), nameof(SafeIsBandit));

        /// <summary>
        /// 枚举 + 安装（幂等）。返回本次新 patch 的数量。
        /// 注意：本类不用 [HarmonyPatch] 修饰（目标类型未知，无法编译期 typeof——PatchAll 只管属性挂载），
        /// 由 MySubModule.OnSubModuleLoad 调用，通过运行时 harmony.Patch 反射挂载。
        /// </summary>
        public static int TryInstallPatches(Harmony harmony)
        {
            int patched = 0;
            try
            {
                foreach (Type modelType in EnumerateDamageModelTypes())
                {
                    foreach (string methodName in DamageModelMethodNames)
                    {
                        string patchKey = modelType.FullName + "." + methodName;
                        if (!_patchedMethods.Add(patchKey))
                        {
                            continue;
                        }

                        MethodInfo method = AccessTools.Method(modelType, methodName);
                        if (method == null)
                        {
                            continue; // 该模型没有这个 API 名（版本差异）→ 正常跳过
                        }

                        try
                        {
                            harmony.Patch(method,
                                transpiler: new HarmonyMethod(typeof(AgentDamageModelCultureNullFix), nameof(Transpiler))
                            );
                            patched++;
                            DebugLogger.Log($"[AgentDamageModelCultureNullFix] 已挂载保护 → {modelType.FullName}.{methodName}");
                        }
                        catch (Exception patchEx)
                        {
                            _patchedMethods.Remove(patchKey); // 允许下次重试
                            DebugLogger.Log($"[AgentDamageModelCultureNullFix] {modelType.FullName}.{methodName} patch 失败（跳过）: {patchEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AgentDamageModelCultureNullFix] 枚举异常: {ex}");
            }

            return patched;
        }

        /// <summary>枚举所有已加载程序集里 AgentApplyDamageModel 的非抽象子类（不含接口/抽象/泛型定义）</summary>
        private static IEnumerable<Type> EnumerateDamageModelTypes()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // 程序集加载失败的（缺失依赖）会抛 ReflectionTypeLoadException —— 逐类型吞掉
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (Type type in types)
                {
                    if (type != null && type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters &&
                        typeof(AgentApplyDamageModel).IsAssignableFrom(type))
                    {
                        yield return type;
                    }
                }
            }
        }

        /// <summary>
        /// 替换两处 `get_Culture + get_IsBandit` 为单条 SafeIsBandit(角色)。
        /// 匹配靠方法名 + 声明类名（跨版本稳定），pattern 未命中则原样透传（补丁失效但绝不改坏 IL）。
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            try
            {
                List<CodeInstruction> code = instructions.ToList();
                List<CodeInstruction> result = new List<CodeInstruction>(code.Count);
                int patchedSites = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (IsCultureGetterCall(code[i]) && i + 1 < code.Count && IsBanditGetterCall(code[i + 1]))
                    {
                        // 栈效果：原 [角色] → get_Culture → [文化] → get_IsBandit → [bool]
                        //        新 [角色] → call SafeIsBandit → [bool]（逐字节等价，无标签无分支）
                        result.Add(new CodeInstruction(OpCodes.Call, SafeIsBanditMethod));
                        patchedSites++;
                        i++;
                    }
                    else
                    {
                        result.Add(code[i]);
                    }
                }
                if (patchedSites > 0)
                {
                    DebugLogger.Log($"[AgentDamageModelCultureNullFix] Transpiler 完成：匹配 {patchedSites} 处 Culture.IsBandit（每模型期望 2）");
                }
                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AgentDamageModelCultureNullFix] Transpiler 异常，放弃补丁（原样返回）: {ex}");
                return instructions;
            }
        }

        /// <summary>调用点识别一：`call/callvirt CharacterObject::get_Culture`（按方法名+声明类名匹配，跨版本稳定）</summary>
        private static bool IsCultureGetterCall(CodeInstruction instr)
        {
            if ((instr.opcode != OpCodes.Call && instr.opcode != OpCodes.Callvirt) ||
                !(instr.operand is MethodInfo mi) || mi.Name != "get_Culture" || mi.DeclaringType == null)
            {
                return false;
            }

            string declaringName = mi.DeclaringType.Name;
            return declaringName == "CharacterObject" || declaringName == "BasicCharacterObject";
        }

        /// <summary>调用点识别二：`callvirt BasicCultureObject::get_IsBandit`（紧随 get_Culture 之后）</summary>
        private static bool IsBanditGetterCall(CodeInstruction instr)
        {
            if ((instr.opcode != OpCodes.Call && instr.opcode != OpCodes.Callvirt) ||
                !(instr.operand is MethodInfo mi) || mi.Name != "get_IsBandit" || mi.DeclaringType == null)
            {
                return false;
            }

            string declaringName = mi.DeclaringType.Name;
            return declaringName == "BasicCultureObject" || declaringName == "CultureObject";
        }

        /// <summary>
        /// 原 get_Culture→get_IsBandit 链的等价替换：Culture 为 null → false（非流寇）并记一次诊断。
        /// 任何意外都吞掉按 false 处理 —— 诊断绝不能反噬战斗。
        /// </summary>
        public static bool SafeIsBandit(BasicCharacterObject character)
        {
            try
            {
                if (character == null)
                {
                    return false;
                }

                object culture = character.Culture;
                if (culture == null)
                {
                    LogNullCulture(character.StringId ?? "?", character.Name?.ToString() ?? "?");
                    return false;
                }

                // Culture 属性的版本差异（CultureObject/BasicCultureObject）由反射吸收：
                // 各版本 IsBandit 挂类虽不同，但属性名一致且 getter 公开
                PropertyInfo banditProp = culture.GetType()
                    .GetProperty("IsBandit", BindingFlags.Public | BindingFlags.Instance);
                object raw = banditProp?.GetValue(culture, null);
                return raw is bool b && b;
            }
            catch (Exception ex)
            {
                try
                {
                    DebugLogger.Log($"[AgentDamageModelCultureNullFix] SafeIsBandit 异常（按非流寇处理）: {ex}");
                }
                catch { }

                return false;
            }
        }

        private static void LogNullCulture(string stringId, string name)
        {
            string key = stringId ?? "<null>";
            if (!_loggedCharacters.Add(key))
            {
                return;
            }

            DebugLogger.Log($"[AgentDamageModelCultureNullFix] 拦截：战斗角色 Culture 为 null（StringId={key}, 名称={name}）" +
                            "→ 按非流寇处理继续战斗。该角色模板引用的文化 id 未在游戏注册表" +
                            "（自定义文化 / 加载序 / 其他 mod 屏蔽），伤害模型在此处裸解引用 `.Culture.IsBandit` 会 NRE，本补丁兜底。");
        }
    }
}
