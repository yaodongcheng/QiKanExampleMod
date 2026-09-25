using System;
using System.Collections.Generic;

namespace LivingWorldNpcs.Animation
{
    /// <summary>
    /// **条件谓词 + 命名标量 的注册表**（2026-09-25 立）—— 状态机定义转 XML 的配套件。
    ///
    /// 为什么要有它：转 XML 之后，"什么时候切"这句话必须能在 XML 里写下来，但**判据本体**不能是
    /// 字符串表达式（自己写表达式解析器 = 失去编译期检查 ⇒ 多一种"打错了不报错、只是不生效"的静默失败）。
    /// 折中办法：**结构进 XML，判据留 C# 但给个名字** ——
    /// <code>
    /// &lt;edge from="UprightFamily" to="fastmoveStart" when="sprinting"/&gt;      ← XML 里只写名字
    /// AnimConditions.Register("sprinting", c =&gt; ((FlightAnimContext)c).Boost &amp;&amp; ...);  ← 真身在 C#
    /// </code>
    ///
    /// 于是：**加状态 / 改指向 / 调 blend / 改族归属 = 改 XML（不用重编译）**；
    /// **新增一种判据 = 加一行 C#**（受编译器保护）。名字写错在**加载期就报错**，不会静默。
    ///
    /// 命名：谓词用 **kebab-case**（`sprinting` / `not-moving` / `boost-bank-left`），
    /// 标量用 **camelCase**（`boostStartSeconds`）—— 一眼能分开"这是条件"还是"这是个数"。
    /// </summary>
    public static class AnimConditions
    {
        private static readonly Dictionary<string, Func<AnimContext, bool>> _preds =
            new Dictionary<string, Func<AnimContext, bool>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Func<float>> _params =
            new Dictionary<string, Func<float>>(StringComparer.Ordinal);

        /// <summary>登记一个条件谓词（重名 = 覆盖，方便热改；会报一行提醒）。</summary>
        public static void Register(string name, Func<AnimContext, bool> when)
        {
            if (string.IsNullOrEmpty(name) || when == null)
            {
                throw new ArgumentException("AnimConditions.Register 需要 name 与 when");
            }
            if (_preds.ContainsKey(name))
            {
                DebugLogger.Log($"[Anim] 条件谓词 '{name}' 被重复登记（覆盖旧的）");
            }
            _preds[name] = when;
        }

        /// <summary>取一个条件谓词（没登记就返回 false —— 调用方负责报错）。</summary>
        public static bool TryGet(string name, out Func<AnimContext, bool> when)
        {
            return _preds.TryGetValue(name, out when);
        }

        /// <summary>
        /// 登记一个**命名标量**（给 XML 里的 <c>duration="boostStartSeconds"</c> 这类用）。
        /// 用委托是为了**热调生效** —— 指向 `FlightTuning.BoostStartSeconds` 这种字段，改值立刻反映。
        /// </summary>
        public static void RegisterParam(string name, Func<float> value)
        {
            if (string.IsNullOrEmpty(name) || value == null)
            {
                throw new ArgumentException("AnimConditions.RegisterParam 需要 name 与 value");
            }
            _params[name] = value;
        }

        /// <summary>取一个命名标量（没登记就返回 false）。</summary>
        public static bool TryGetParam(string name, out float value)
        {
            value = 0f;
            Func<float> f;
            if (!_params.TryGetValue(name, out f))
            {
                return false;
            }
            try { value = f(); }
            catch { return false; }
            return true;
        }

        /// <summary>已登记的条件谓词名（体检/日志用）。</summary>
        public static IEnumerable<string> PredicateNames => _preds.Keys;

        /// <summary>已登记的命名标量名（体检/日志用）。</summary>
        public static IEnumerable<string> ParamNames => _params.Keys;
    }
}
