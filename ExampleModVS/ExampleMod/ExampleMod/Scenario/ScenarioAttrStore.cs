using System;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本外置属性仓门面（16b §3.1）：对象上骑砍2 没有的字段，逐实体 key = 「域:StringId」。
    /// 物理存储 = GlobalVariableBehavior._extendedProperties（存档键 lwn_scn_attr，Story/StoryContext.cs）。
    /// 🔴 写入口唯一 = set_attr（对应 TK5 更新命令）；类型检查在加载期由 validator 做，本体不解释值。
    /// 🔴 值统一字符串；读不到 = null（调用方 `?.` 传播，铁律 2）；孤儿键（对象不在）保留不删（16b §3.1）。
    /// </summary>
    public static class ScenarioAttrStore
    {
        private static GlobalVariableBehavior Sink => GlobalVariableBehavior.Instance;

        /// <summary>写入（key 自带域前缀：Hero::lord_1_oda / Settlement::town_CHUB11 …；值 = 字符串，数字/布尔/引用按 16a 值类型列序列化）</summary>
        public static void SetAttr(string domainKey, string field, string value)
        {
            if (string.IsNullOrEmpty(domainKey) || string.IsNullOrEmpty(field)) return;
            try { Sink?.SetExtendedProperty(NormalizeKey(domainKey), field, value); }
            catch (Exception e) { DebugLogger.Log($"[Scenario] SetAttr 失败 {domainKey}.{field}: {e.Message}"); }
        }

        /// <summary>读取；无值 = null（调用方按属性默认值/数据包默认值兜底）</summary>
        public static string GetAttr(string domainKey, string field)
        {
            if (string.IsNullOrEmpty(domainKey) || string.IsNullOrEmpty(field)) return null;
            try { return Sink?.GetExtendedProperty(NormalizeKey(domainKey), field); }
            catch (Exception e) { DebugLogger.Log($"[Scenario] GetAttr 失败 {domainKey}.{field}: {e.Message}"); return null; }
        }

        /// <summary>key 归一：统一「域:StringId」形态（顺手清理全角冒号/空白——16 纪律 12）</summary>
        public static string NormalizeKey(string domainKey)
        {
            return (domainKey ?? "").Replace("：", ":").Replace(" ", "").Trim();
        }
    }
}
