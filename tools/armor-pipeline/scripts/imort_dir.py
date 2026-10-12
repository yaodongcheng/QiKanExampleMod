# -*- coding: utf-8 -*-
"""imort_dir.py —— 解析「待导入」根目录（`AssetSources` 还是 `AssetSources_disabled`）。

🔴 **为什么有这个文件（2026-10-12 事故，我造成的）**
    模块目录的两种状态各有**两个名字**（与 `Assets` / `Assets_disabled` 同一套机制）：

        编辑器模式   `Assets`          `AssetSources`
        游戏模式     `Assets_disabled` `AssetSources_disabled`

    我写的构建脚本 `OUT_DIR` 直接拼了 `AssetSources`，并且 `os.makedirs(..., exist_ok=True)`
    **静默把它建了出来** —— 后果不是"文件放错地方"那么轻：
    模块当时处于**游戏模式**，`AssetSources_disabled` 才是活的那个；
    我这个新建的 `AssetSources` 让两个目录同时存在 ⇒
    **`to_editor_mode.bat` 第 23 行判定 "BOTH exist -- resolve manually, nothing moved"，切模式直接罢工。**

    纪律（memory `module-asset-dir-two-state-check` 早就写着）：
    **写资产前先解析当前态；工具禁止静默建目录。**

【规则】只有一个名字存在 → 用它；两个都在 或 都不在 → **抛错停下**，绝不猜、绝不建。
    仅当显式给了 `--out` 时才例外（人明确指定了落点，脚本照办，但仍不建顶层目录）。
"""
import os


class ImortDirError(RuntimeError):
    pass


def resolve_imort_root(module_dir, explicit=None):
    """返回「待导入」根目录（`.../<模块>/AssetSources[_disabled]`）。

    module_dir : 模块根（如 `.../Modules/LwnAnim`）
    explicit   : `--out` 显式指定时的**根目录**；给了就直接用（不再校验两态）
    """
    if explicit:
        return explicit

    a = os.path.join(module_dir, "AssetSources")
    b = os.path.join(module_dir, "AssetSources_disabled")
    ea, eb = os.path.isdir(a), os.path.isdir(b)

    if ea and eb:
        raise ImortDirError(
            "AssetSources 与 AssetSources_disabled **同时存在** —— 这是切模式 bat 会罢工的状态。\n"
            "  先人工确认哪个是活的（游戏模式 = _disabled 是活的），删掉多余的那个再跑。\n"
            "  a = %s\n  b = %s" % (a, b))
    if not ea and not eb:
        raise ImortDirError(
            "两个都没有：%s / %s\n"
            "  **本工具不会替你建**（静默建目录正是 2026-10-12 那次事故的根因）。\n"
            "  先跑一次模块的 to_editor_mode.bat / 或在编辑器里 Import 一次让编辑器自己建。" % (a, b))
    return a if ea else b


def imort_subdir(module_dir, name, explicit=None):
    """`<待导入根>/ImortReady/<name>`。🔴 目录名现状拼写是 `ImortReady`（少一个 p，用户既有命名）。"""
    return os.path.join(resolve_imort_root(module_dir, explicit), "ImortReady", name)


def ensure_subdir(path):
    """只允许建 `ImortReady/<名字>` 这一层；上层必须已存在。

    判据：`os.makedirs` 之前，**父目录必须已经在**（父 = `ImortReady`）。
    这样即使误传了一个顶层不存在的路径，也会在这里被拦住，而不是默默造出一条新链。
    """
    parent = os.path.dirname(path)
    if not os.path.isdir(parent):
        raise ImortDirError(
            "父目录不存在，拒绝创建：%s\n  （本工具只建 `<待导入根>/ImortReady/<名字>` 这一层）" % parent)
    os.makedirs(path, exist_ok=True)
    return path
