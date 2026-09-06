using HarmonyLib;
using System.IO;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony 补丁：替换启动 Logo 视频（Taleworlds 标志开屏，splash）。
    ///
    /// 机制：引擎在 Module.SetInitialModuleScreenAsRootScreen 中硬编码
    ///   ModuleHelper.GetModuleFullPath("Native") + "Videos/TWLogo_and_Partners.ivf/.ogg"
    /// 并交给 VideoPlaybackState.SetStartingParameters 播放。
    /// SetStartingParameters 是启动视频的唯一播放入口（反编译实证：1.2.12 / 1.5.2 均为唯一调用点，签名一致，无需版本分支）。
    ///
    /// 🔴 替换范围纪律（2026-09-06 用户裁定）：开新战役的开场视频也走 SetStartingParameters，
    /// 但那是原版宣发叙事的一部分——不允许替换。Prefix 只拦截「TWLogo 启动 splash」：
    /// videoPath 不含 TWLogo（如 SandBox/StoryMode 的战役开场视频）→ 原样放行（与原生一致）。
    ///
    /// 用法（config.json 两字段，默认空 = 不启用、行为与原生一致）：
    ///   "SplashVideoModuleId": "Taikou",      // 视频所在模块 Id
    ///   "SplashVideoFileName": "TR5OP_TW"     // Videos/ 下的视频文件名（不含扩展名；ivf + ogg 必须成对）
    /// 内容包零 DLL 依赖：放 Videos/ 文件 + 本 mod 配两个字段即可。
    /// </summary>
    [HarmonyPatch(typeof(VideoPlaybackState), "SetStartingParameters")]
    public static class SplashVideoReplacePatch
    {
        public static void Prefix(ref string videoPath, ref string audioPath, string subtitleFileBasePath, float frameRate, bool canUserSkip)
        {
            // 🔴 只替换启动 splash（TWLogo）；开新战役开场等其它视频 = 原版（2026-09-06 用户裁定）
            if (videoPath == null || !videoPath.Replace('\\', '/').ToLowerInvariant().Contains("twlogo"))
            {
                return;
            }
            string moduleId = Settings.Instance.SplashVideoModuleId;
            string fileName = Settings.Instance.SplashVideoFileName;
            if (string.IsNullOrEmpty(moduleId) || string.IsNullOrEmpty(fileName))
            {
                return; // 未配置：保持原生（TWLogo）
            }
            try
            {
                // 未挂载模块：GetModuleFullPath 是字典直索引（反编译实证 _allFoundModules[id].FolderPath），
                // 没挂载 = 抛 KeyNotFoundException → 落到 catch → 保持原生（TWLogo）。
                string path = ModuleHelper.GetModuleFullPath(moduleId);
                string full = Path.Combine(path, "Videos", fileName);
                string ivfPath = full + ".ivf";
                string oggPath = full + ".ogg";
                // 文件缺失（挂载了但视频被删/改名）：保持原生（TWLogo）——别让引擎落到 File.Exists false 跳过 splash
                if (!File.Exists(ivfPath) || !File.Exists(oggPath))
                {
                    DebugLogger.Log($"[SplashVideo] 挂载但文件缺失，保持原生：{ivfPath} / {oggPath}");
                    return;
                }
                videoPath = ivfPath;
                audioPath = oggPath;
                DebugLogger.Log($"[SplashVideo] 替换成功：{ivfPath} / {oggPath}");
            }
            catch (System.Exception ex)
            {
                // 模块 Id 无效：保持原生路径（引擎侧 File.Exists 检查兜底，splash 仍走原生）
                DebugLogger.Log($"[SplashVideo] 模块未挂载/路径获取失败，保持原生：{moduleId} ({ex.GetType().Name})");
            }
        }
    }
}
