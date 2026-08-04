"""
package_mod.py — One-click mod release packager
================================================
Usage: python package_mod.py
Output: release/{ModDir}_mod{ModVersion}_game{GameVersion}.zip

Unzipping creates a single folder named after the mod directory,
containing ModuleData, SubModule.xml, GUI, config.json, bin, Debug.
"""
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

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
game_root = (MOD_ROOT / ".." / "..").resolve()
version_paths = [
    game_root / "bin" / "Win64_Shipping_Client" / "Version.xml",
    game_root / "bin" / "Win64_Shipping_Server" / "Version.xml",
]
version_path = next((vp for vp in version_paths if vp.exists()), None)
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

# Debug folder: skip log files, keep folder itself
DEBUG_SKIP_SUFFIXES = {".log", ".txt"}

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
                # Debug folder: skip logs
                if item == "Debug" and file.suffix.lower() in DEBUG_SKIP_SUFFIXES:
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
print("=" * 40)
