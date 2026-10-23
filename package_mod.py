"""
package_mod.py — One-click mod release packager
================================================
Usage: python package_mod.py            # 出包
       python package_mod.py --dry-run  # 只列出会打进包里的东西（不写 zip，不删旧包）
Output: release/{ModDir}_mod{ModVersion}_game{GameVersion}.zip

Unzipping creates a single folder named after the mod directory,
containing ModuleData (+动作接线/物品/语言), SubModule.xml, GUI, config.json,
bin, Prefabs, AssetPackages (*.tpac：动画/网格/材质/粒子), ModuleSounds (wav), Debug.
"""
import os
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

# 备份文件一律不进包：ModKit 每搬一次 `lwn_core.tpac` 就留一份 `.bak-<时间戳>`（13MB/个），
# 2026-10-23 实测 AssetPackages/ 下已屯 17 个 = 189MB —— 旧脚本会把它们全打进 zip（包体 200MB+）。
# 口径与 .gitignore 一致：.bak / .bak- / .bak_ / .bak. / .bak20260831 全算，大小写不分。
BACKUP_RE = re.compile(r"\.bak(?:[._-]|\d|$)", re.IGNORECASE)


def _user_env_var(name):
    r"""读取用户级持久环境变量（注册表 HKCU\Environment，即 setx 的写入位置）。

    进程级 os.environ 是终端启动时的快照——setx 改完后旧终端里仍是旧值；
    直接读注册表拿到的是"真实"持久值，不受残留终端影响。
    """
    try:
        import winreg
    except ImportError:  # 非 Windows 平台
        return None
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
            value, _ = winreg.QueryValueEx(key, name)
            return value if isinstance(value, str) else None
    except OSError:
        return None


def main():
    # 1. Script directory = Mod root
    MOD_ROOT = Path(__file__).resolve().parent
    MOD_DIR_NAME = MOD_ROOT.name  # e.g. "LivingWorldNpcs"
    print(f"Mod root: {MOD_ROOT}")

    # 2. Parse SubModule.xml
    submodule_path = MOD_ROOT / "SubModule.xml"
    if not submodule_path.exists():
        print(f"ERROR: SubModule.xml not found in {MOD_ROOT}")
        sys.exit(1)

    tree = ET.parse(submodule_path)
    root = tree.getroot()

    # SubModule.xml uses lowercase "value" attributes: <Name value="..." />
    mod_name = root.find("Name").get("value", "").strip()
    mod_version = root.find("Version").get("value", "").strip().lstrip("v")
    print(f"Mod: {mod_name}  (v{mod_version})")

    # 3. Parse game Version.xml for Bannerlord version
    # 版本来源优先级：① 注册表用户级 MB2_PATH（真实持久值，setx 写入处，与 csproj 编译目标一致）
    #                ② 进程级 os.environ（旧终端里可能是快照）→ ③ 相对路径 ..\..（mod 装在游戏 Modules 下）
    env_root = _user_env_var("MB2_PATH")
    if not env_root:
        env_root = os.environ.get("MB2_PATH", "")
    env_root = str(env_root).strip().strip('"')
    candidates = []
    if env_root:
        candidates.append((Path(env_root), "MB2_PATH 用户环境变量"))
    candidates.append(((MOD_ROOT / ".." / "..").resolve(), "相对路径 ..\\.."))

    version_path = None
    for game_root, source in candidates:
        version_path = next(
            (
                p
                for p in (
                    game_root / "bin" / "Win64_Shipping_Client" / "Version.xml",
                    game_root / "bin" / "Win64_Shipping_Server" / "Version.xml",
                )
                if p.exists()
            ),
            None,
        )
        if version_path:
            print(f"Game root ({source}): {game_root}")
            break

    if version_path is None:
        print("ERROR: Version.xml not found — cannot determine game version")
        sys.exit(1)

    vtree = ET.parse(version_path)
    # Version.xml uses capital "Value" attribute: <Singleplayer Value="v1.2.12" />
    game_version = vtree.find("Singleplayer").get("Value", "").strip().lstrip("v")
    print(f"Game version: v{game_version}")

    # 4. Items to package
    # 🔴 2026-10-09 新增 AssetPackages / Prefabs：通用玩法（钩索/飞行/处决/法术/贴花）的资产随本模块发布
    #    —— 编辑器工程在沙箱 Modules/LwnAnim/，产物 Publish 后拷进 AssetPackages/（详见 ModuleData/project.mbproj）。
    # 🔴 2026-10-10 新增 ModuleSounds：钩索绳声 + 飞行风噪的 wav（引擎标准音效目录，见 ModuleData/module_sounds.xml）
    #    —— 不列在这里 = 发布包里音效文件整个丢失（本机测试不受影响，玩家装了没声）。
    ITEMS = ["ModuleData", "SubModule.xml", "GUI", "config.json", "bin", "Prefabs", "AssetPackages", "ModuleSounds", "Debug"]
    # 🔴 2026-10-23：下面几个是「玩家侧缺了就坏」的交付物，缺任何一个直接中止（不再只是警告）——
    #    AssetPackages = 动画/网格/材质/粒子（lwn_core.tpac）；ModuleSounds = 音效 wav；Prefabs = 预制体。
    #    它们都不在本仓库主分支里（AssetPackages 走 assets 分支、ModuleSounds 被 .gitignore 排除）
    #    ⇒ 换机器 / 新克隆后最容易**静默缺失**，打出来的包看着正常、进游戏没动画没声音。
    REQUIRED = {"SubModule.xml", "bin", "ModuleData", "AssetPackages", "ModuleSounds", "Prefabs"}
    # 开发素材子目录，整体排除（ModuleData/ScenarioData = 剧本源数据 pack.json + story/*.jsonc，不发布）
    SKIP_DIR_NAMES = {"ScenarioData"}

    existing = [item for item in ITEMS if (MOD_ROOT / item).exists()]
    for item in ITEMS:
        if item not in existing:
            print(f"WARNING: Skipping non-existent item: {item}")

    missing_required = [item for item in ITEMS if item in REQUIRED and item not in existing]
    if missing_required:
        print(f"ERROR: required item(s) missing: {', '.join(missing_required)}")
        sys.exit(1)

    # Debug folder: whitelist — 只保留 StoryEngine_RuntimeLog.txt 运行时日志，
    # 其余（回归测试日志、LLM 样本、崩溃转储、归档）都是临时文件，不打包
    DEBUG_WHITELIST = {"StoryEngine_RuntimeLog.txt"}
    # bin folder: skip debug symbol files (.pdb 仅供断点调试，发布包不需要)
    BIN_SKIP_SUFFIXES = {".pdb"}
    skipped = {"pdb": 0, "debug": 0, "dev_dir": 0, "backup": 0}

    def iter_item_files(item):
        """Yield (源文件, zip 内路径) —— 打包与 --dry-run 共用这一份过滤逻辑。"""
        item_path = MOD_ROOT / item
        if not item_path.is_dir():
            yield item_path, f"{MOD_DIR_NAME}/{item_path.name}"
            return
        for file in item_path.rglob("*"):
            if not file.is_file():
                continue
            # 排除开发素材目录（如 ModuleData/ScenarioData：剧本源数据，不随发布）
            if any(part in SKIP_DIR_NAMES for part in file.relative_to(MOD_ROOT).parts):
                skipped["dev_dir"] += 1
                continue
            # Debug folder: only keep the runtime log (whitelist)
            if item == "Debug" and file.name not in DEBUG_WHITELIST:
                skipped["debug"] += 1
                continue
            # bin folder: skip .pdb debug symbols
            if item == "bin" and file.suffix.lower() in BIN_SKIP_SUFFIXES:
                skipped["pdb"] += 1
                continue
            # 备份文件（lwn_core.tpac.bak-* 等）不进包 —— 见文件头 BACKUP_RE 注释
            if BACKUP_RE.search(file.name):
                skipped["backup"] += 1
                continue
            yield file, f"{MOD_DIR_NAME}/{file.relative_to(MOD_ROOT).as_posix()}"

    # 4b. --dry-run：只报清单（每个顶层条目的文件数 / 体积），不建 release/、不删旧包
    if "--dry-run" in sys.argv or "-n" in sys.argv:
        print()
        print(f"{'ITEM':<16}{'FILES':>7}{'SIZE':>12}")
        print("-" * 36)
        total_size = 0
        for item in existing:
            count = 0
            item_size = 0
            for file, _ in iter_item_files(item):
                count += 1
                try:
                    item_size += file.stat().st_size
                except OSError:
                    pass
            total_size += item_size
            print(f"{item:<16}{count:>7}{item_size / 1048576:>10.2f}MB")
        print("-" * 36)
        print(f"{'TOTAL (raw)':<16}{'':>7}{total_size / 1048576:>10.2f}MB")
        if skipped["backup"]:
            print(f"Skipped {skipped['backup']} backup file(s) (.bak*)")
        if skipped["pdb"]:
            print(f"Skipped {skipped['pdb']} .pdb file(s)")
        if skipped["debug"]:
            print(f"Skipped {skipped['debug']} temp file(s) in Debug/")
        if skipped["dev_dir"]:
            print(f"Skipped {skipped['dev_dir']} file(s) in dev-only dir(s): {', '.join(sorted(SKIP_DIR_NAMES))}")
        print("Dry run — nothing written.")
        return

    # 5. Create release directory
    release_dir = MOD_ROOT / "release"
    release_dir.mkdir(exist_ok=True)

    zip_name = f"{MOD_DIR_NAME}_v{mod_version}_for_MB2_v{game_version}.zip"
    zip_path = release_dir / zip_name

    if zip_path.exists():
        zip_path.unlink()
        print(f"Removed old archive: {zip_name}")

    print("Packaging...")
    print(f"  Items: {', '.join(existing)}")

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for item in existing:
            if (MOD_ROOT / item).is_dir():
                # Always add the directory entry itself
                zf.writestr(zipfile.ZipInfo(f"{MOD_DIR_NAME}/{item}/"), "")
            for file, arcname in iter_item_files(item):
                zf.write(file, arcname)

    # 6. Print result
    size_mb = zip_path.stat().st_size / (1024 * 1024)
    print()
    print("=" * 40)
    print("  Package complete!")
    print(f"  {zip_path}")
    print(f"  Size: {size_mb:.2f}MB")
    if skipped["backup"]:
        print(f"  Skipped {skipped['backup']} backup file(s) (.bak*)")
    if skipped["pdb"]:
        print(f"  Skipped {skipped['pdb']} .pdb file(s)")
    if skipped["debug"]:
        print(f"  Skipped {skipped['debug']} temp file(s) in Debug/")
    if skipped["dev_dir"]:
        print(f"  Skipped {skipped['dev_dir']} file(s) in dev-only dir(s): {', '.join(sorted(SKIP_DIR_NAMES))}")
    print("=" * 40)


if __name__ == "__main__":
    main()
