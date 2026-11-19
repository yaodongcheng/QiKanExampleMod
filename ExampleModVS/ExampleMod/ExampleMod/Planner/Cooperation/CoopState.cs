using System;
using Newtonsoft.Json;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// 协作业务记录的存盘入口：只保存尝试、收据、关系与未了事项，
    /// 不保存 Agent、Brain、活动执行栈或搜索树（§4.2）。读档后活动不自动续跑。
    /// </summary>
    public static class CoopState
    {
        public const string SaveKey = "lwn_coop_ledger";
        private static CoopLedger _ledger = new CoopLedger();

        public static CoopLedger Ledger { get { return _ledger; } }

        public static string Serialize()
        {
            try
            {
                var data = CoopLedgerData.From(_ledger);
                return JsonConvert.SerializeObject(data);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                ResetAll();
                return;
            }
            try
            {
                var data = JsonConvert.DeserializeObject<CoopLedgerData>(json);
                _ledger = data != null ? data.ToLedger() : new CoopLedger();
            }
            catch (Exception)
            {
                // 旧档缺字段不崩：退回空账本，未了事项另行提示。
                _ledger = new CoopLedger();
            }
        }

        public static void ResetAll()
        {
            _ledger = new CoopLedger();
        }

        public static void MarkUnresolved(string text)
        {
            if (string.IsNullOrEmpty(text) || _ledger.Unresolved.Contains(text))
            {
                return;
            }
            _ledger.Unresolved.Add(text);
            if (_ledger.Unresolved.Count > 20)
            {
                _ledger.Unresolved.RemoveAt(0);
            }
        }
    }
}