"""
package_mod.py — One-click mod release packager
================================================
Usage: python package_mod.py
Output: release/{ModDir}_mod{ModVersion}_game{GameVersion}.zip

Unzipping creates a single folder named after the mod directory,
containing ModuleData, SubModule.xml, GUI, config.json, bin, Debug.
"""
import os
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


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
    ITEMS = ["ModuleData", "SubModule.xml", "GUI", "config.json", "bin", "Debug"]

    existing = [item for item in ITEMS if (MOD_ROOT / item).exists()]
    for item in ITEMS:
        if item not in existing:
            print(f"WARNING: Skipping non-existent item: {item}")

    if not existing:
        print("ERROR: Nothing to package")
        sys.exit(1)

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

    # Debug folder: whitelist — 只保留 StoryEngine_RuntimeLog.txt 运行时日志，
    # 其余（回归测试日志、LLM 样本、崩溃转储、归档）都是临时文件，不打包
    DEBUG_WHITELIST = {"StoryEngine_RuntimeLog.txt"}
    # bin folder: skip debug symbol files (.pdb 仅供断点调试，发布包不需要)
    BIN_SKIP_SUFFIXES = {".pdb"}
    skipped_pdb = 0
    skipped_debug = 0

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for item in existing:
            item_path = MOD_ROOT / item
            if item_path.is_dir():
                # Always add the directory entry itself
                dir_arcname = f"{MOD_DIR_NAME}/{item}/"
                zf.writestr(zipfile.ZipInfo(dir_arcname), "")

                for file in item_path.rglob("*"):
                    if not file.is_file():
                        continue
                    # Debug folder: only keep the runtime log (whitelist)
                    if item == "Debug" and file.name not in DEBUG_WHITELIST:
                        skipped_debug += 1
                        continue
                    # bin folder: skip .pdb debug symbols
                    if item == "bin" and file.suffix.lower() in BIN_SKIP_SUFFIXES:
                        skipped_pdb += 1
                        continue
                    arcname = f"{MOD_DIR_NAME}/{file.relative_to(MOD_ROOT).as_posix()}"
                    zf.write(file, arcname)
            else:
                arcname = f"{MOD_DIR_NAME}/{item_path.name}"
                zf.write(item_path, arcname)

    # 6. Print result
    size_mb = zip_path.stat().st_size / (1024 * 1024)
    print()
    print("=" * 40)
    print("  Package complete!")
    print(f"  {zip_path}")
    print(f"  Size: {size_mb:.2f}MB")
    if skipped_pdb:
        print(f"  Skipped {skipped_pdb} .pdb file(s)")
    if skipped_debug:
        print(f"  Skipped {skipped_debug} temp file(s) in Debug/")
    print("=" * 40)


if __name__ == "__main__":
    main()
