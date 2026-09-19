using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LivingWorldNpcs.DebugTools
{
    /// <summary>
    /// 「脸部贴图实时换」控制台指令 —— 调自建头肤色用的免重启旋钮。
    ///
    /// # 🔴🔴 2026-09-19 实测标记：【不可信，别用】
    ///
    /// **症状**：指令能跑通（返回 ok、报 `via CreateFromMemory`），但**任何档位都把脸变成蓝色**，
    /// 完全不是"变亮/变暗"。（用户实机 2026-09-19 报）
    ///
    /// **判断**：蓝色 ≈ 引擎拿默认/错误格式兜底 → `Texture.CreateFromMemory(裸 PNG 字节)` **没把像素喂对**
    /// （它大概不认 PNG，或要的是别的布局）。`Material.SetTexture` 本身可能是好的
    /// （`reset` 走 `Texture.GetFromResource` 那条没报错）。
    ///
    /// **要接着修的话**：别再试 `CreateFromMemory`。改走「**把各档位预编译进包**，
    /// 用 `Texture.GetFromResource("<贴图资产名>")` 切」—— 这条路已被 `reset` 验证过；
    /// 代价是每档要占一个贴图资产（可用 `assetclone`/`makepack` 加，或直接出多个包）。
    ///
    /// **在此之前**：调档一律走**离线 `texreplace` 循环**（改包 + 重启，一轮约 1 分钟），
    /// 那是已验证可用的路（§22.3）。
    ///
    /// ---
    /// 以下为实现（症状已记录，逻辑本身没被推翻，只是加载环节不可信）。
    ///
    /// 用法（`~` 控制台）：
    ///   custom.face_tex              列出可用的档位 PNG（扫 Debug\face_tint\）
    ///   custom.face_tex 0.70         切到档位 0.70
    ///   custom.face_tex reset        恢复包内原贴图
    ///   custom.face_tex 0.70 head_xx 指定材质名（默认 head_henry_a）
    ///
    /// 首参可弃：解析不出 → 回落到"列清单"，不报错（骑砍控制台不填参数时可能根本不触发）。
    /// </summary>
    public static class FaceTintCommands
    {
        private const string DefaultMaterial = "head_henry_a";
        private const string TintFolderRel = "Modules/LivingWorldNpcs/Debug/face_tint";

        /// <summary>档位 PNG 目录的**绝对路径**。
        /// 🔴 别用 `PlatformDirectoryPath(...).ToString()` —— 那是相对路径，`Directory.Exists`
        ///    会拿进程工作目录去解析（而骑砍的工作目录不是游戏根）→ 永远找不到。
        ///    游戏根一律走 `BasePath.Name`。</summary>
        private static string TintDirAbs =>
            System.IO.Path.Combine(BasePath.Name, "Modules", "LivingWorldNpcs", "Debug", "face_tint");

        [CommandLineFunctionality.CommandLineArgumentFunction("face_tex", "custom")]
        public static string ExecuteFaceTex(List<string> args)
        {
            try
            {
                // 首参可弃：args[0] 是占位（骑砍控制台命令不填参数可能不触发）
                string want = args != null && args.Count > 0 ? args[0].Trim() : "";
                string matName = args != null && args.Count > 1 ? args[1].Trim() : DefaultMaterial;

                var mat = Material.GetFromResource(matName);
                if (mat == null)
                {
                    return $"error: material '{matName}' not found (is the head module loaded?).";
                }

                // --- reset：恢复包内原贴图 ---
                if (string.Equals(want, "reset", StringComparison.OrdinalIgnoreCase))
                {
                    var orig = Texture.GetFromResource(matName + "_d");
                    if (orig == null) return $"error: original texture '{matName}_d' not found.";
                    mat.SetTexture(Material.MBTextureType.DiffuseMap, orig);
                    return $"ok: '{matName}' diffuse map reset to pack texture.";
                }

                // --- 列清单 ---
                var files = ListTintFiles();
                if (string.IsNullOrEmpty(want) || want == "-" || want == "1" || want == "list")
                {
                    if (files.Count == 0) return $"no tint png found in {TintDirAbs}";
                    return $"available ({files.Count}): " + string.Join(", ", files.Select(f => System.IO.Path.GetFileNameWithoutExtension(f)))
                         + $" | usage: custom.face_tex <name|reset> [material={DefaultMaterial}]";
                }

                // --- 按档位/名字找 PNG ---
                string hit = files.FirstOrDefault(f =>
                    System.IO.Path.GetFileNameWithoutExtension(f).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit == null)
                {
                    return $"error: no tint png matches '{want}'. available: "
                         + string.Join(", ", files.Select(f => System.IO.Path.GetFileNameWithoutExtension(f)));
                }

                var tex = LoadTexture(hit, out string how);
                if (tex == null)
                {
                    return $"error: both loaders failed for '{hit}' (file exists: {File.Exists(hit)}).";
                }

                mat.SetTexture(Material.MBTextureType.DiffuseMap, tex);
                return $"ok: '{matName}' diffuse map -> {System.IO.Path.GetFileNameWithoutExtension(hit)} "
                     + $"({tex.Width}x{tex.Height}) via {how}";
            }
            catch (Exception ex)
            {
                return "error: " + ex.Message;
            }
        }

        /// <summary>
        /// 三重回退加载贴图 —— **绝对路径优先**。
        ///
        /// 🔴 为什么不用 `PlatformFilePath(PlatformFileType.Application, ...)` 打头：实测它解析到的是
        ///    `C:\ProgramData\Mount and Blade II Bannerlord\`（**应用数据目录，不是游戏安装根**）→ 挂。
        ///    游戏安装根只有 `BasePath.Name` 拿得到；而 `PlatformFilePath` 只接受 (类型, 相对路径)，
        ///    没有"绝对路径"那一档。所以先用能吞绝对路径/裸字节的两条。
        /// </summary>
        private static Texture LoadTexture(string absPath, out string how)
        {
            // ① 裸字节进内存解码（完全不碰路径解析，最稳）
            try
            {
                if (File.Exists(absPath))
                {
                    var t = Texture.CreateFromMemory(File.ReadAllBytes(absPath));
                    if (t != null) { how = "CreateFromMemory"; return t; }
                }
            }
            catch { }
            // ② 引擎自己的 (文件名, 目录) 载入
            try
            {
                var t = Texture.LoadTextureFromPath(System.IO.Path.GetFileName(absPath),
                                                    TintFolderRel);
                if (t != null) { how = "LoadTextureFromPath"; return t; }
            }
            catch { }
            how = "none";
            return null;
        }

        /// <summary>扫档位 PNG（按文件名排序，档位号在名字里）</summary>
        private static List<string> ListTintFiles()
        {
            try
            {
                if (!Directory.Exists(TintDirAbs)) return new List<string>();
                return Directory.GetFiles(TintDirAbs, "*.png").OrderBy(f => f).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
