using HarmonyLib;
using System.Linq;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.GauntletUI.SceneNotification;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 过场动画（SceneNotification）日志观察补丁，记录两个站点的信息：
    /// 1. [SceneNotification:REQUEST] — 请求入口：所有过场（原生 25+ 个 + mod 自定义）都经
    ///    MBInformationManager.ShowSceneNotification(data) 发出；
    /// 2. [SceneNotification:PLAY]   — 真正开播：GauntletSceneNotification.CreateSceneNotification，
    ///    上下文门控（RelevantContext）通过后入队出队、开播的那一刻。
    /// 搜索标签：[SceneNotification:REQUEST] / [SceneNotification:PLAY]
    /// 注：部分信息按版本裁剪——GetShips 为 v1.3.15+ 新增（#if MB2_GE_130）。
    /// </summary>
    public static class SceneNotificationLogHelper
    {
        public static string BuildInfo(string stage, SceneNotificationData data)
        {
            var sb = new StringBuilder();
            sb.Append($"[SceneNotification:{stage}] ")
              .Append($"type={data.GetType().Name} ")
              .Append($"scene={data.SceneID ?? "(none)"} ")
              .Append($"title={(data.TitleText?.ToString() ?? "(none)")} ")
              .Append($"context={data.RelevantContext} ")
              .Append($"pause={data.PauseActiveState} ")
              .Append($"sound={data.SoundEventPath ?? "(none)"} ")
              .Append($"affirmBtn={(data.IsAffirmativeOptionShown ? (data.AffirmativeText?.ToString() ?? "OK") : "hidden")} ")
              .Append($"negBtn={(data.IsNegativeOptionShown ? (data.NegativeText?.ToString() ?? "Cancel") : "hidden")} ");

            var chars = data.GetSceneNotificationCharacters();
            sb.Append($"chars={(chars == null ? 0 : chars.Count())} ");

            var banners = data.GetBanners();
            sb.Append($"banners={(banners == null ? 0 : banners.Count())} ");

#if MB2_GE_130
            var ships = data.GetShips();
            sb.Append($"ships={(ships == null ? 0 : ships.Length)}");
#endif
            return sb.ToString();
        }
    }

    /// <summary>
    /// 请求入口补丁。所有过场动画的第一个汇聚点（TaleWorlds.Core.dll，跨版本签名一致）。
    /// 记录信息：切场景 ID / 标题 / 播放上下文（大地图 or mission）/ 按钮 / 角色数 / 旗帜数 / 船数。
    /// </summary>
    [HarmonyPatch(typeof(MBInformationManager), nameof(MBInformationManager.ShowSceneNotification))]
    public static class SceneNotificationReqLoggerPatch
    {
        [HarmonyPostfix]
        public static void Prefix(SceneNotificationData data)
        {
            try
            {
                if (data == null)
                {
                    DebugLogger.Log("[SceneNotification:REQUEST] data = NULL");
                    return;
                }
                DebugLogger.Log(SceneNotificationLogHelper.BuildInfo("REQUEST", data));
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }

    /// <summary>
    /// 真正开播补丁。Harmony 按参数名 "data" 匹配（v1.2.12 与 v1.5.x 的参数名一致，仅参数个数
    /// 不同：1.2.12 为 (data, pauseGameActiveState) 双参，1.5.x 为单参——未声明的参数不表态，
    /// 全版本通用）。
    /// </summary>
    [HarmonyPatch(typeof(GauntletSceneNotification), "CreateSceneNotification")]
    public static class SceneNotificationPlayLoggerPatch
    {
        [HarmonyPostfix]
        public static void Prefix(SceneNotificationData data)
        {
            try
            {
                if (data == null)
                {
                    DebugLogger.Log("[SceneNotification:PLAY] data = NULL");
                    return;
                }
                DebugLogger.Log(SceneNotificationLogHelper.BuildInfo("PLAY", data));
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }
}
