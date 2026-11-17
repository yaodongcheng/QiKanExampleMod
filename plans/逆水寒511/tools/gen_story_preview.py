"""Build the read-only 511 DesignData viewer. No overview/design prose is embedded.

Uses the registered scene_layout_geometry adapter for real Native mesh outlines.
Actor/prop positions come from ScenePlan and StoryPoints, never old audit markers.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
DATA = ROOT / "ModuleData/DesignData"
PAGE = ROOT / "plans/逆水寒511/逆水寒511-玩法演示.html"
TEMPLATE = Path(__file__).with_name("story_preview.template.html")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def get_game_root():
    import winreg
    for hive, key in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, key) as handle:
                return Path(winreg.QueryValueEx(handle, "MB2_PATH")[0])
        except FileNotFoundError:
            continue
    return None


def make_payload(game_root=None):
    sources = {}

    def source(path):
        name = str(path.relative_to(ROOT)).replace("\\", "/")
        content = path.read_text(encoding="utf-8-sig")
        sources[name] = {"sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "text": content}
        return content

    def data(name):
        return json.loads(source(DATA / name))

    cast = data("StoryProbeCast.json")
    plan = data("StoryScenePlan.json")
    tuning = data("StoryProbeTuning.json")
    opening = data("StoryOpeningTimeline.json")
    gate_setup = data("StoryGateSetup.json")
    journal = data("StoryJournal.json")
    points = list(csv.DictReader(source(DATA / "StoryPoints.csv").splitlines()))
    language_path = ROOT / "ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml"
    language = {e.get("id"): e.get("text", "") for e in ET.fromstring(source(language_path)).iter("string")}
    # This shared preview mapping follows the runtime capability registry.
    from gen_dialogue_html import capability_attributes
    capabilities = capability_attributes()
    dialogues = []
    used_keys = set()
    for path in sorted((DATA / "Dialogues").glob("*.dialog.json")):
        definition = json.loads(source(path))
        for node in definition["Nodes"]:
            used_keys.add(node["TextKey"])
            for option in node.get("Options", []):
                used_keys.update([option["PlayerKey"], option["HintKey"]])
                if option.get("Check") and option["Check"]["Capability"] not in capabilities:
                    raise ValueError("Unknown preview capability: " + option["Check"]["Capability"])
        dialogues.append(definition)

    def collect_keys(value):
        if isinstance(value, dict):
            for field, item in value.items():
                if field.endswith("Key") and isinstance(item, str) and item:
                    used_keys.add(item)
                elif field.endswith("Keys") and isinstance(item, list):
                    used_keys.update(item)
                collect_keys(item)
        elif isinstance(value, list):
            for item in value:
                collect_keys(item)

    for definition in (cast, opening, plan, journal):
        collect_keys(definition)
    for capability, attribute in capabilities.items():
        used_keys.update(("LWN_story_capability_" + capability, "LWN_story_ability_" + attribute))
    for key in used_keys:
        if not language.get(key) or "缺中文" in language[key]:
            raise ValueError("Missing Chinese preview text: " + key)
    language = {key: language[key] for key in sorted(used_keys)}

    result = dict(cast=cast, plan=plan, tuning=tuning, opening=opening,
                  gateSetup=gate_setup, journal=journal, points=points,
                  dialogues=dialogues, language=language, capabilities=capabilities,
                  sources=sources, layouts={})
    game_root = game_root or get_game_root()
    scene_path = None if game_root is None else game_root / "Modules/SandBoxCore/SceneObj" / plan["Scene"] / "scene.xscene"
    scene = ET.parse(scene_path).getroot() if scene_path and scene_path.is_file() else None
    # Source scene selection is an explicit reference level, not the live campaign wall state.
    wall_level = int(plan["WallLevel"])
    scene_items = []
    if scene is not None:
        from scene_layout_geometry import active_levels, transform_frame
        for entity in scene.find("entities"):
            levels = {e.get("name") for e in entity.findall("levels/level")}
            if active_levels(levels, wall_level):
                scene_items.append(dict(name=entity.get("name", ""), prefab=entity.get("prefab", ""),
                                        tags=[x.get("name") for x in entity.findall("tags/tag")],
                                        position=transform_frame(entity)[:3, 3].tolist()))
    gate = tuning["Gate"]["Layouts"].get(plan["Scene"])
    if not gate:
        raise ValueError("Scene has no configured gate coordinates")
    outside, gate_center = gate["Outside"], gate["Gate"]
    dx, dy = gate_center[0] - outside[0], gate_center[1] - outside[1]
    length = math.hypot(dx, dy)
    if length == 0:
        raise ValueError("Degenerate gate basis")
    forward = [dx / length, dy / length]

    def nearest(values):
        return min(values, key=lambda x: sum((x["position"][i] - outside[i]) ** 2 for i in (0, 1)))

    for stage_id, stage in plan["Stages"].items():
        layout = plan["Layouts"][stage["Layout"]]
        anchors = {"player": list(outside)}
        unresolved = []
        prop_by_id = {}
        for key, anchor in layout.get("Anchors", {}).items():
            if anchor["Source"] == "player":
                anchors[key] = list(outside)
            elif anchor["Source"] != "prop":
                found = None
                for value in anchor.get("Value", []):
                    candidates = [e for e in scene_items if
                                  (value in e["tags"] if anchor["Source"] == "tag" else
                                   value == e["name"] or value == e["prefab"] or value in e["tags"])]
                    if candidates:
                        found = nearest(candidates) if anchor.get("Pick") == "nearestToPlayer" else candidates[0]
                        break
                if found:
                    anchors[key] = found["position"]
                else:
                    unresolved.append(key)
        anchors.setdefault("gate", [gate_center[0] - forward[0] * tuning["Gate"]["OutsideMeters"],
                                    gate_center[1] - forward[1] * tuning["Gate"]["OutsideMeters"], gate_center[2]])
        anchors["gate_inner"] = [gate_center[0] + forward[0] * tuning["Gate"]["InsideMeters"],
                                 gate_center[1] + forward[1] * tuning["Gate"]["InsideMeters"], gate_center[2]]

        def position(place):
            if "Absolute" in place:
                return list(place["Absolute"])
            anchor_id = place.get("Anchor", "player")
            if anchor_id not in anchors:
                return None
            f = forward
            anchor = layout.get("Anchors", {}).get(anchor_id, {})
            if anchor.get("Source") == "prop" and anchor.get("Value", [None])[0] in prop_by_id:
                yaw = math.radians(prop_by_id[anchor["Value"][0]].get("Yaw", 0))
                f = [math.cos(yaw), math.sin(yaw)]
            a = anchors[anchor_id]
            return [a[0] + f[0] * place.get("Fwd", 0) + f[1] * place.get("Right", 0),
                    a[1] + f[1] * place.get("Fwd", 0) - f[0] * place.get("Right", 0),
                    a[2] + place.get("Up", 0)]

        props = []
        for prop in layout.get("Props", []):
            if prop["Id"] not in stage.get("Props", []):
                continue
            pos = position(prop)
            props.append(dict(id=prop["Id"], position=pos, raw=prop))
            prop_by_id[prop["Id"]] = prop
            if pos is not None:
                for key, anchor in layout.get("Anchors", {}).items():
                    if anchor.get("Source") == "prop" and anchor.get("Value") == [prop["Id"]]:
                        anchors[key] = pos
        actors = []
        for standing in layout.get("Standing", []):
            if standing["Actor"] not in stage["Actors"]:
                continue
            point = next((p for p in points if p["Id"] == standing["Point"]
                          and p["Layout"] == stage["Layout"] and p["Scene"] == plan["Scene"]), None)
            if point is None:
                raise ValueError("Missing standing point: " + standing["Point"])
            place = dict(Anchor=point["Anchor"], Fwd=float(point["X"] or 0),
                         Right=float(point["Y"] or 0), Up=float(point["Z"] or 0))
            pos = position(place)
            facing = float(point["Dir"] or 0)
            angle = math.atan2(outside[1] - pos[1], outside[0] - pos[0]) if pos and facing < -.5 else math.radians(facing)
            actors.append(dict(id=standing["Actor"], point=point["Id"], position=pos,
                               facing=[math.cos(angle), math.sin(angle)], action=point["Action"], raw=point))
        result["layouts"][stage_id] = dict(actors=actors, props=props, anchors=anchors,
                                             unresolved=unresolved, forward=forward,
                                             gate=list(gate_center), player=list(outside),
                                             goal=anchors["gate_inner"])

    all_positions = [x["position"] for view in result["layouts"].values()
                     for x in view["actors"] + view["props"] if x["position"]]
    all_positions += [outside, gate_center, result["layouts"]["second_meet"]["goal"]]
    bounds = [min(p[0] for p in all_positions) - 7, max(p[0] for p in all_positions) + 7,
              min(p[1] for p in all_positions) - 8, max(p[1] for p in all_positions) + 8]
    result["map"] = dict(bounds=bounds, geometry=[], wallLevel=wall_level,
                         sceneAvailable=scene is not None, missingMeshes=[], sourceScene=str(scene_path or ""))
    if scene is not None:
        from scene_layout_geometry import collect_scene_meshes, scene_geometry
        records, prefab_files, skipped = collect_scene_meshes(scene, [game_root / "Modules/Native/Prefabs"], wall_level, bounds)
        asset_dir = ROOT / "Debug/offline"
        available = []
        signature = hashlib.sha256(scene_path.read_bytes() + str(wall_level).encode() + str(bounds).encode())
        for p in sorted(prefab_files):
            signature.update(p.read_bytes())
        for record in records:
            mesh_path = asset_dir / record["mesh"].split("_", 1)[0] / (record["mesh"] + ".obj")
            material_path = mesh_path.with_suffix(".mat_map.json")
            if mesh_path.is_file() and material_path.is_file():
                available.append(record)
                for p in (mesh_path, material_path):
                    signature.update(str(p).encode())
                    signature.update(str((p.stat().st_mtime_ns, p.stat().st_size)).encode())
            else:
                result["map"]["missingMeshes"].append(record["mesh"])
        # Include the reused geometry implementation in the cache identity.
        signature.update(Path(__file__).with_name("scene_layout_geometry.py").read_bytes())
        cache_path = asset_dir / "_511_preview_geometry.json"
        cache = read_json(cache_path) if cache_path.is_file() else {}
        if cache.get("signature") == signature.hexdigest():
            result["map"]["geometry"] = cache["geometry"]
        else:
            geometry, _ = scene_geometry(available, asset_dir, bounds)
            from shapely.geometry import mapping
            result["map"]["geometry"] = [dict(category=record["category"],
                                               shape=mapping(shape.simplify(.08, preserve_topology=True)))
                                          for record, shape, _ in geometry]
            cache_path.write_text(json.dumps(dict(signature=signature.hexdigest(), geometry=result["map"]["geometry"])), encoding="utf-8")
        result["map"]["missingMeshes"] = sorted(set(result["map"]["missingMeshes"]))
        result["map"]["sourceHash"] = hashlib.sha256(scene_path.read_bytes()).hexdigest()
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", type=Path)
    args = parser.parse_args(argv)
    payload = make_payload(args.game_root)
    embedded = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026")
    template = TEMPLATE.read_text(encoding="utf-8")
    if template.count("__DESIGN_DATA__") != 1:
        raise ValueError("Preview template must have exactly one data slot")
    PAGE.write_text(template.replace("__DESIGN_DATA__", embedded), encoding="utf-8", newline="")
    print("DesignData preview: %d dialogues / %d nodes / %d scene outlines / %d missing meshes" %
          (len(payload["dialogues"]), sum(len(x["Nodes"]) for x in payload["dialogues"]),
           len(payload["map"]["geometry"]), len(payload["map"]["missingMeshes"])))


if __name__ == "__main__":
    main()
