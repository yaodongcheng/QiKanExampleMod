using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingWorldNpcs.Animation
{
    /// <summary>
    /// **状态机要读的输入事实**（2026-09-25 立）—— 让"按下/松开某个键""动画还剩多久"能被**定义直接写出来**，
    /// 而不是每加一种条件都要在 C# 里登记一个谓词。
    ///
    /// 谁实现它：各系统的动画上下文（范本 <c>Flight/FlightAnimContext</c>）—— 每帧从原始输入读好填上。
    /// 谁消费它：<see cref="AnimPrimitives"/> 造出来的谓词（XML 里 <c>key="W" key-mode="down"</c> 这类）。
    ///
    /// 🔴 键的顺序由 <see cref="AnimPrimitives.Keys"/> 定死，实现方按同一顺序填数组（有一个运行期数量校验）。
    /// </summary>
    public interface IAnimInputFacts
    {
        /// <summary>这个键**这一帧按着**吗（电平）。</summary>
        bool KeyHeld(int keyIndex);

        /// <summary>这个键**这一帧刚按下**吗（沿，只活一帧）。</summary>
        bool KeyDown(int keyIndex);

        /// <summary>这个键**这一帧刚松开**吗（沿，只活一帧）。</summary>
        bool KeyUp(int keyIndex);

        /// <summary>
        /// **当前动画还剩多少秒**（只有一次性动作有意义；循环状态应当填 <c>float.PositiveInfinity</c>，
        /// 这样"剩余 &lt; X"永远不成立 —— 循环没有"播完"这回事）。
        /// </summary>
        float AnimRemaining { get; }
    }

    /// <summary>
    /// **条件原语**（2026-09-25 立）：把 XML 里的 `key=` / `anim=` 这类**固定词汇**翻译成谓词。
    ///
    /// 🔴 为什么这不是"表达式解析器"：词汇是**封闭**的 —— 键只有 <see cref="Keys"/> 那 8 个、
    ///    模式只有 4 个、动画条件只有 2 种。写错在**装载期报错**，不存在"打错了只是不生效"。
    ///    真正的表达式（`A &amp;&amp; B || C`）仍然走 <see cref="AnimConditions.Register"/> 登记命名谓词 ——
    ///    那条边界不放开。
    /// </summary>
    public static class AnimPrimitives
    {
        /// <summary>
        /// **可用的键**（顺序 = <see cref="IAnimInputFacts"/> 里数组的下标，**改顺序会错位，别动**）。
        /// 与飞行那边 <c>FlightInput</c> 读的是同一批原始键。
        /// </summary>
        public static readonly string[] Keys = { "W", "A", "S", "D", "Space", "Shift", "RMB", "LMB" };

        /// <summary>可用的按键模式。</summary>
        public static readonly string[] Modes = { "down", "up", "held", "released" };

        /// <summary>可用的动画条件：`finished` = 演完；`remaining` = 剩余时间小于阈值（要配 <c>anim-lt</c>）。</summary>
        public static readonly string[] Anims = { "finished", "remaining" };

        public const int KeyCount = 8;   // 必须等于 Keys.Length

        /// <summary>键名 → 下标（大小写不敏感；不认得的返回 -1）。</summary>
        public static int KeyIndex(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -1;
            }
            for (int i = 0; i < Keys.Length; i++)
            {
                if (string.Equals(Keys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 造一个"键"谓词。返回 false 时 <paramref name="error"/> 说明哪里不对（装载期直接用）。
        /// </summary>
        public static bool TryBuildKey(string key, string mode, out Func<AnimContext, bool> when, out string error)
        {
            when = null;
            error = null;
            int idx = KeyIndex(key);
            if (idx < 0)
            {
                error = "未知的键 '" + key + "'（可用：" + string.Join(" / ", Keys) + "）";
                return false;
            }
            string m = (mode ?? string.Empty).Trim().ToLowerInvariant();
            switch (m)
            {
                case "down": when = c => Facts(c).KeyDown(idx); break;
                case "up": when = c => Facts(c).KeyUp(idx); break;
                case "held": when = c => Facts(c).KeyHeld(idx); break;
                case "released": when = c => !Facts(c).KeyHeld(idx); break;
                default:
                    error = "未知的按键模式 '" + mode + "'（可用：" + string.Join(" / ", Modes) + "）";
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 造一个"动画时间"谓词。`finished` = 剩余 ≈ 0；`remaining` = 剩余 &lt; <paramref name="lt"/> 秒。
        /// </summary>
        public static bool TryBuildAnim(string anim, string ltRaw, out Func<AnimContext, bool> when, out string error)
        {
            when = null;
            error = null;
            string a = (anim ?? string.Empty).Trim().ToLowerInvariant();
            if (a == "finished")
            {
                when = c => Facts(c).AnimRemaining <= 0.001f;
                return true;
            }
            if (a == "remaining")
            {
                float lt;
                if (!float.TryParse(ltRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out lt))
                {
                    error = "anim=\"remaining\" 需要一个数字阈值 anim-lt（秒），现在给的是 '" + ltRaw + "'";
                    return false;
                }
                when = c => Facts(c).AnimRemaining < lt;
                return true;
            }
            error = "未知的动画条件 '" + anim + "'（可用：" + string.Join(" / ", Anims) + "）";
            return false;
        }

        /// <summary>
        /// 上下文必须实现 <see cref="IAnimInputFacts"/> —— 没实现就是这台机器的上下文没准备好，
        /// 装载期就报出来（而不是运行期每帧静默返回 false）。
        /// </summary>
        private static IAnimInputFacts Facts(AnimContext c)
        {
            IAnimInputFacts f = c as IAnimInputFacts;
            if (f == null)
            {
                throw new InvalidOperationException(
                    "上下文没实现 IAnimInputFacts —— 用 key= / anim= 的机器，其上下文必须实现它");
            }
            return f;
        }
    }
}
