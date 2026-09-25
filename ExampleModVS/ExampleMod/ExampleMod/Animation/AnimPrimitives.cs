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
    /// 谁消费它：<see cref="AnimPrimitives"/> 造出来的谓词（XML 里 <c>key="W" key-mode="held"</c> 这类）。
    ///
    /// 🔴 键的顺序由 <see cref="AnimPrimitives.Keys"/> 定死，实现方按同一顺序填数组（有一个运行期数量校验）。
    /// </summary>
    public interface IAnimInputFacts
    {
        /// <summary>这个键**这一帧按着**吗（电平）。</summary>
        bool KeyHeld(int keyIndex);

        // 🔴 **"刚按下 / 刚松开"（沿）已经在 2026-09-25 删除**（用户裁定：「只需要区分按住还是没按住」）。
        //    依据：① `key-mode` 在 XML 里**只用过 `held`**，`down`/`up` 一处都没用过；
        //          ② 这台机当初正是因为"**沿会被吞**"（起飞/落地 Hold 期间、施法让位期间）才从沿改成电平的
        //             —— 见 XML `<edges>` 里"进趴姿唯一的门"那段注释。
        //    少一种词汇 = 少一种"写错了只是不生效"的静默失败。

        /// <summary>
        /// **当前动画还剩多少**，用**占整条 clip 的比例**表示（0~1：1 = 刚开头，0 = 播完）。
        /// 只有一次性动作有意义；循环状态填 <c>float.PositiveInfinity</c>，
        /// 这样"剩余 &lt; X%"永远不成立 —— 循环没有"播完"这回事。
        ///
        /// 🔴 **为什么是比例不是秒**：长度由 **clip 自己带**（引擎给的是 0~1 的播放进度），
        ///    所以"重导 clip 换了帧数"不用去改任何配置 —— 配置里**永远不写秒数**。
        /// </summary>
        float AnimRemainFrac { get; }
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

        // 🔴 **按键**只有一个词汇：`keys="A+B"`（`+` 连接 = **同时按着**，项间 AND）+ `not="true"`（整条取反）。
        //    · 「刚按下 / 刚松开」（沿）已删 —— 会被起飞/落地 Hold、施法让位吞掉。
        //    · 「没按住」= `keys="A" not="true"`（不再有 `key-mode`）。
        //    ⇒ 组合与否定**都能写**，而且**不是自由文本**：每个键都能在装载期校验（写错就整台不注册）。

        /// <summary>可用的动画条件：`finished` = 演完；`remaining` = 剩余不足阈值（要配 <c>anim-rem-pct</c>，**百分比 0~100**）。</summary>
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
        /// 造一个"键"谓词 —— 🔴 **支持组合**：`keysRaw = "A+B"` 表示这几个键**同时按着**（项间 AND）。
        /// 取反**不在这里**做（装载器统一用 `not="true"` 包一层，对 anim / 谓词也一样适用）。
        ///
        /// 返回 false 时 <paramref name="error"/> 说明哪里不对（装载期直接用 —— 写错就**整台不注册**，不静默）。
        /// </summary>
        public static bool TryBuildKeys(string keysRaw, out Func<AnimContext, bool> when, out string error)
        {
            when = null;
            error = null;
            if (string.IsNullOrWhiteSpace(keysRaw))
            {
                error = "keys= 不能为空（写法：keys=\"A+B\" = 同时按着 A 和 B）";
                return false;
            }
            var idx = new List<int>();
            foreach (string part in keysRaw.Split('+'))
            {
                string k = part.Trim();
                if (k.Length == 0)
                {
                    error = "keys=\"" + keysRaw + "\" 里有空的键（多打了一个 + ？）";
                    return false;
                }
                int i = KeyIndex(k);
                if (i < 0)
                {
                    error = "未知的键 '" + k + "'（可用：" + string.Join(" / ", Keys) + "）";
                    return false;
                }
                if (idx.Contains(i))
                {
                    error = "keys=\"" + keysRaw + "\" 里 '" + k + "' 重复了";
                    return false;
                }
                idx.Add(i);
            }
            when = c =>
            {
                IAnimInputFacts f = Facts(c);
                for (int i = 0; i < idx.Count; i++)
                {
                    if (!f.KeyHeld(idx[i]))
                    {
                        return false;
                    }
                }
                return true;
            };
            return true;
        }

        /// <summary>
        /// 造一个"动画时间"谓词。`finished` = 剩余 ≈ 0；`remaining` = 剩余 &lt; <paramref name="pctRaw"/> **百分比**（0~100）。
        /// 🔴 阈值是**百分比**不是秒 —— 长度由 clip 自己带，配置里不写秒数（见 <see cref="IAnimInputFacts.AnimRemainFrac"/>）。
        /// </summary>
        public static bool TryBuildAnim(string anim, string pctRaw, out Func<AnimContext, bool> when, out string error)
        {
            when = null;
            error = null;
            string a = (anim ?? string.Empty).Trim().ToLowerInvariant();
            if (a == "finished")
            {
                when = c => Facts(c).AnimRemainFrac <= 0.001f;
                return true;
            }
            if (a == "remaining")
            {
                float pct;
                if (!float.TryParse(pctRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
                {
                    error = "anim=\"remaining\" 需要一个**百分比**阈值 anim-rem-pct（0~100），现在给的是 '" + pctRaw + "'";
                    return false;
                }
                if (pct < 0f || pct > 100f)
                {
                    error = "anim=\"remaining\" 的 anim-rem-pct=" + pctRaw + " 超出范围（是**百分比** 0~100，不是秒）";
                    return false;
                }
                when = c => Facts(c).AnimRemainFrac * 100f < pct;
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
