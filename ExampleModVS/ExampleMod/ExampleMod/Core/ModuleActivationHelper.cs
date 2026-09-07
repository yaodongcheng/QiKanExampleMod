using System;
using System.Linq;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 模块启用判定（2026-09-07）：判断「内容包是否被 launcher 勾选」的唯一入口。
    ///
    /// 为什么必须走这一个 API（1.2.12 踩坑实证）：
    ///   ModuleHelper.GetModuleFullPath / GetModuleInfo 在 1.2.12 查的是 _allFoundModules——
    ///   由 GetPhysicalModules() 扫描 Modules/ 目录建成的【物理字典】（只排除目录不存在），
    ///   与 launcher 勾选无关。Modules\Taikou 目录在 = 未勾选也查得到 → 资源替换三通道
    ///   （SplashVideo / MenuSoundtrack / DesignData）的"模块未挂载兜底"全部失效，
    ///   没挂 Taikou 也播放太阁片头/太阁 BGM（2026-09-07 实机）。
    ///   唯一可信判据 = Utilities.GetModulesNames()：引擎 IUtil.GetModulesCode()（launcher 传入
    ///   的启用列表，LoadSubModules 装配 DLL 同源）；1.2.12 ~ 1.5.1 四版本同签名。
    /// </summary>
    public static class ModuleActivationHelper
    {
        /// <summary>模块是否在本场启动的启用列表中（大小写不敏感；任何异常保守判"未启用"）。</summary>
        public static bool IsModuleEnabled(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId))
            {
                return false;
            }
            try
            {
                string[] names = TaleWorlds.Engine.Utilities.GetModulesNames();
                if (names == null)
                {
                    return false;
                }
                return names.Any(name => name.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ModuleActivation] 取启用列表失败，判定未启用：{moduleId} ({ex.GetType().Name})");
                return false;
            }
        }
    }
}
