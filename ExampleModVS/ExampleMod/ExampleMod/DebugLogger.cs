using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.Party;

namespace ExampleMod
{
    [HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
    public static class DebugCrashPatch
    {
        // Prefix 在原方法执行前运行
        public static void Prefix(MobileParty __instance, PartyTemplateObject pt, int troopNumberLimit)
        {
            // 检查 pt (部队模板) 是否为空
            if (pt == null)
            {
                string info = "【崩溃预警】检测到 PartyTemplateObject (pt) 为 NULL！\n";
                info += GetDebugInfo(__instance);

                DebugLogger.Log(info);

                // 此时你可以选择在这里打个断点，因为现在是在你自己的代码里，
                // 肯定能看到 info 变量的内容。
            }
            // 检查 pt 存在，但 Stacks 列表为空的情况（极少见但可能）
            else if (pt.Stacks == null)
            {
                string info = $"【崩溃预警】模板 '{pt.StringId}' 存在，但 Stacks 列表为 NULL！\n";
                info += GetDebugInfo(__instance);
                System.Diagnostics.Debug.WriteLine(info);
            }
        }

        private static string GetDebugInfo(MobileParty party)
        {
            if (party == null) return "MobileParty 实例本身也是 NULL (这就太离谱了)";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- 肇事者信息 ---");
            sb.AppendLine($"Party ID: {party.StringId}");
            sb.AppendLine($"Party Name: {party.Name}");

            if (party.LeaderHero != null)
            {
                sb.AppendLine($"Leader: {party.LeaderHero.Name} (ID: {party.LeaderHero.StringId})");
                sb.AppendLine($"Leader Culture: {party.LeaderHero.Culture?.StringId ?? "NULL"}");
                sb.AppendLine($"Leader Clan: {party.LeaderHero.Clan?.Name.ToString() ?? "NULL"}");
            }
            else
            {
                sb.AppendLine($"Leader: 无 (非英雄带领的队伍)");
            }

            if (party.ActualClan != null)
            {
                sb.AppendLine($"Party Clan: {party.ActualClan.Name}");
                sb.AppendLine($"Clan Culture: {party.ActualClan.Culture?.StringId ?? "NULL"}");
            }

            // 检查拥有者原本的文化设置
            var culture = party.Party?.Culture; // MobileParty 包含 PartyBase
            sb.AppendLine($"Party Culture: {culture?.StringId ?? "NULL"}");

            return sb.ToString();
        }
    }
    public static class DebugLogger
    {
        // 静态标志位：默认 true，表示本次游戏运行尚未写入过日志
        private static bool _isFirstWrite = true;

        // 线程锁对象：防止多个逻辑同时调用日志导致文件占用冲突
        private static readonly object _writeLock = new object();

        // 定义日志文件名
        private const string LogFileName = "StoryEngine_RuntimeLog.txt";

        /// <summary>
        /// 通用日志函数。
        /// 第一次调用时会清空旧文件，之后调用会在文件末尾追加。
        /// </summary>
        /// <param name="message">要记录的字符串</param>
        public static void Log(string message)
        {
            try
            {
                // 1. 确定保存路径 (保持与你的示例一致的路径结构)
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                string savePath = Path.Combine(documentsPath, "Mount and Blade II Bannerlord", "Configs", LogFileName);

                // 确保目录存在
                string dir = Path.GetDirectoryName(savePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 2. 格式化内容：加上时间戳，并自动换行
                string logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

                // 3. 写入文件 (加锁保证安全)
                lock (_writeLock)
                {
                    if (_isFirstWrite)
                    {
                        // 如果是本次运行第一次写入：使用 WriteAllText (覆盖模式)
                        // 这会清除上一次游戏留下的旧日志
                        File.WriteAllText(savePath, logEntry, Encoding.UTF8);

                        // 标记为 false，表示文件已初始化，后续全部改为追加模式
                        _isFirstWrite = false;
                    }
                    else
                    {
                        // 如果不是第一次：使用 AppendAllText (追加模式)
                        File.AppendAllText(savePath, logEntry, Encoding.UTF8);
                    }
                }
            }
            catch (Exception)
            {
                // 日志系统出错时通常不做处理，避免导致游戏逻辑崩溃
                // 或者可以使用 System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
    }

}
