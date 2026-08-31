using System;
using System.Collections.Generic;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 事件上下文（Ctx 槽，16 §一 三档的「事件内局部」）：触发时初始化、事件结束清理，**不存档**。
    /// 分支选择默认写这里（choice → ctx_set）；跨事件读取 = Variable/GlobalSlot（禁止跨事件读 Ctx）。
    /// Instance = 当前执行事件上下文；W4 执行器设置/清理。
    /// </summary>
    public class ScenarioContext
    {
        public static ScenarioContext Instance = new ScenarioContext();

        private readonly Dictionary<string, string> _slots = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>语义槽自动赋值（01：發生據點 = event_settlement / 發生人物 = event_hero）</summary>
        public string EventSettlement { get; set; }
        public string EventHero { get; set; }

        public void Set(string slot, string value)
        {
            if (string.IsNullOrEmpty(slot)) return;
            if (string.IsNullOrEmpty(value)) { _slots.Remove(slot); return; }
            _slots[slot] = value;
        }

        public string Get(string slot) => slot != null && _slots.TryGetValue(slot, out var v) ? v : null;

        public void Clear()
        {
            _slots.Clear();
            EventSettlement = null;
            EventHero = null;
        }
    }
}
