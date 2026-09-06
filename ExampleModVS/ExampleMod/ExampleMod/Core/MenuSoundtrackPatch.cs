using System.IO;
using TaleWorlds.ModuleManager;
using psai.net;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 主菜单 BGM 内容包注入：把 Psai 音乐工程重载为内容包模块的 music/soundtrack.xml。
    ///
    /// 背景（2026-09-06 反编译实证）：
    /// 1. 1.2.12 引擎 Psai 音频只相对【工程文件目录】解析（ConvertFilePathForPlatform →
    ///     "PC/&lt;名&gt;.ogg"，无模块搜索），模块级 music/PC 同名覆盖（织丰式）不生效；
    /// 2. Psai 工程在游戏启动【最早期】加载（MBMusicManager 构造于后台线程，
    ///    引擎 EnsureAsyncJobsAreFinished 死等其创建完成）——早于任何模块 OnSubModuleLoad，
    ///    因此 Harmony 拦截 LoadSoundtrackFromProjectFile 已太晚（prefix 不会执行）。
    /// 3. 本类由 MySubModule.OnSubModuleLoad（PatchAll 完成之后）主动调用 TryApply，
    ///    用内容包工程【重载】Psai；主菜单 BGM 尚未进入 MenuMode，重载安全。
    ///
    /// 配置：Settings.MenuSoundtrackModuleId（默认空 = 不接管，行为与原生一致）。
    /// 版本：仅 1.2.12 需要（1.5.x 引擎原生支持模块工程 mbproj soln_soundtrack）。
    /// </summary>
    public static class MenuSoundtrackReload
    {
        public static void TryApply()
        {
#if MB2_V1212
            string moduleId = Settings.Instance.MenuSoundtrackModuleId;
            if (string.IsNullOrEmpty(moduleId))
            {
                return; // 未配置：保持原生（游戏根 music/soundtrack.xml）
            }
            try
            {
                string target = ModuleHelper.GetModuleFullPath(moduleId) + "music/soundtrack.xml";
                if (!File.Exists(target))
                {
                    DebugLogger.Log($"[MenuSoundtrack] 工程文件缺失，保持原生：{target}");
                    return;
                }
                PsaiCore.Instance.LoadSoundtrackFromProjectFile(target);
                DebugLogger.Log($"[MenuSoundtrack] 主菜单 BGM 已重载为内容包工程：{target}");
            }
            catch (System.Exception ex)
            {
                DebugLogger.Log($"[MenuSoundtrack] 重载失败，保持原生：{moduleId} ({ex.GetType().Name})");
            }
#else
            // 1.5.x：引擎原生支持模块音乐工程，无需重载（模块 Id 走 Taikou/project.mbproj 的 soln_soundtrack）
#endif
        }
    }
}
