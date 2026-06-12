using System.Collections.Generic;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 恶名系统：记录玩家在委托系统中的恶名值。
    /// - 拒还定金：恶名 +1
    /// - 高恶名：诚实 NPC 不给委托，灰色 NPC 更愿意给高风险高回报委托
    /// - 完成高难度委托可逐步消除恶名
    /// 持久化经 MyBehavior.SyncData（JSON 序列化）。
    /// </summary>
    public static class InfamySystem
    {
        private static int _infamy = 0;

        public static int Infamy => _infamy;

        public static void AddInfamy(int delta)
        {
            _infamy = System.Math.Max(0, _infamy + delta);
        }

        public static void ReduceInfamy(int delta)
        {
            _infamy = System.Math.Max(0, _infamy - delta);
        }

        /// <summary>获取恶名描述</summary>
        public static string GetDescription()
        {
            if (_infamy >= 10) return "臭名昭著 — 诚实之人避之不及，但灰色地带的人视你为同道";
            if (_infamy >= 5) return "声名狼藉 — 正经委托减少，高风险委托增多";
            if (_infamy >= 2) return "略有微词 — 部分 NPC 对你有所顾虑";
            return "清清白白";
        }

        /// <summary>检查某委托是否受恶名影响而不可接</summary>
        /// <returns>true = 恶名阻止了此委托</returns>
        public static bool IsBlockedByInfamy(CommissionCategory category, int heroRelation)
        {
            // 灰色类委托（悬赏、地下拳赛、越狱等）在恶名高时反而更开放
            // 经济/护送类委托在恶名高时受限制
            if (_infamy >= 5)
            {
                // 高恶名时，正经商人/要人不给委托
                bool isGreyCategory = category == CommissionCategory.BountyHunt ||
                                      category == CommissionCategory.UndergroundFight ||
                                      category == CommissionCategory.PrisonBreak ||
                                      category == CommissionCategory.SupplyIntercept ||
                                      category == CommissionCategory.DecoyMission;

                if (!isGreyCategory && heroRelation < 20)
                    return true; // 正经委托在高恶名 + 低关系时不可接
            }

            return false;
        }

        #region Persistence

        public static string Serialize()
        {
            return _infamy.ToString();
        }

        public static void Deserialize(string data)
        {
            if (int.TryParse(data, out int v))
                _infamy = System.Math.Max(0, v);
            else
                _infamy = 0;
        }

        #endregion
    }
}
