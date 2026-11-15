"""Thin xscene/Prefab adapter for the existing tpaccli OBJ exports.

Follows scene_rebuild's prefab -> mesh and parent @ local workflow. Adds
indexed instance overrides and level selection needed by the 511 audit.
Does not read TPAC, run a Mission, or claim engine collision verification.
"""
from copy import deepcopy
import json
import math
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

import numpy as np
import shapely
from shapely.geometry import LineString, Polygon, mapping
from shapely.ops import polygonize, unary_union


ARCHITECTURE = re.compile(r"^(empire_(castle_|road_|garden_wall|house_|bridge|stairs)|european_castle_gate|fence_empire)")
MODES = {"civilian", "siege", "sally", "battle", "multiplayer"}
WALL_LEVELS = {"level_1", "level_2", "level_3"}


def transform_frame(node):
    t = node.find("transform")
    a = {} if t is None else t.attrib
    pos = np.fromstring(a.get("position", "0,0,0"), sep=",")
    x, y, z = np.fromstring(a.get("rotation_euler", "0,0,0"), sep=",")
    scale = np.fromstring(a.get("scale", "1,1,1"), sep=",")
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    frame = np.eye(4)
    # Decompiled Mat3.ApplyEulerAngles: Up(z), Side(x), Forward(y).
    frame[:3, :3] = rz @ rx @ ry @ np.diag(scale)
    frame[:3, 3] = pos
    return frame


def merge_override(base, override):
    """Merge sparse scene overrides, preserving original child index/order."""
    result = deepcopy(base)
    result.attrib.update(override.attrib)
    for part in override:
        target = result.find(part.tag)
        if part.tag == "children":
            if target is None:
                result.append(deepcopy(part))
                continue
            for child in part:
                if "_index_" not in child.attrib:
                    target.append(deepcopy(child))
                    continue
                index = int(child.get("_index_"))
                if not 0 <= index < len(target):
                    raise ValueError(f"Prefab child override out of range: {index}, {base.attrib}")
                merged = merge_override(target[index], child)
                target.remove(target[index])
                target.insert(index, merged)
        elif part.tag == "transform" and target is not None:
            target.attrib.update(part.attrib)
        else:
            if target is not None:
                result.remove(target)
            result.append(deepcopy(part))
    return result


def prefab_index(prefab_dirs):
    index, sources, ambiguous = {}, {}, set()
    for directory in prefab_dirs:
        for path in sorted(directory.glob("*.xml")):
            # Only top-level definitions. A same-name nested child is not a prefab.
            for entity in ET.parse(path).getroot().findall("game_entity"):
                name = entity.get("name")
                if name in index:
                    ambiguous.add(name)
                    continue
                index[name], sources[name] = entity, path
    return index, sources, ambiguous


def active_levels(levels, wall_level):
    return ((not (levels & MODES) or "civilian" in levels)
            and (not (levels & WALL_LEVELS) or f"level_{wall_level}" in levels))


def collect_scene_meshes(scene, prefab_dirs, wall_level, bounds):
    index, sources, ambiguous = prefab_index(prefab_dirs)
    records, used_sources, skipped = [], set(), []
    x0, x1, y0, y1 = bounds

    def expand(node, stack=()):
        name = node.get("prefab")
        if not name:
            result = deepcopy(node)
            children = result.find("children")
            if children is not None:
                for j, child in enumerate(list(children)):
                    children.remove(child)
                    children.insert(j, expand(child, stack))
            return result
        if name in stack:
            raise ValueError("Cyclic prefab: " + name)
        if name not in index:
            raise ValueError("Missing prefab: " + name)
        if name in ambiguous:
            raise ValueError("Ambiguous referenced prefab definition: " + name)
        used_sources.add(sources[name])
        result = merge_override(index[name], node)
        result.attrib.pop("prefab", None)
        children = result.find("children")
        if children is not None:
            for j, child in enumerate(list(children)):
                children.remove(child)
                children.insert(j, expand(child, stack + (name,)))
        return result

    def walk(node, parent, levels, visible, root_name, root_index, root_z, chain):
        own_levels = node.find("levels")
        if own_levels is not None:
            levels = {e.get("name") for e in own_levels}
        visible = visible and node.get("visible", "true") != "false"
        tags = {e.get("name") for e in node.findall("tags/tag")}
        editor = any(e.get("name") == "visible_only_when_editing" and e.get("value") == "true"
                     for e in node.findall("visibility_masks/visibility_mask"))
        damaged = any(t.startswith("state_") and t != "state_0" for t in tags)
        if editor or damaged:
            return
        frame = parent @ transform_frame(node)
        dynamic_gate = "gate" in root_name and "gatehouse" not in root_name
        if visible and active_levels(levels, wall_level):
            components = node.findall("components/meta_mesh_component") + node.findall("skeleton/components/meta_mesh_component")
            for component in components:
                name = component.get("name")
                if name.startswith(("icon_", "debug_")):
                    continue
                records.append({"mesh": name, "root_prefab": root_name, "scene_entity_index": root_index,
                                "child_path": chain, "frame": frame.tolist(), "levels": sorted(levels),
                                "root_z": root_z, "dynamic_gate": dynamic_gate,
                                "category": "door" if dynamic_gate else "road" if "road_" in root_name else
                                            "fence" if root_name.startswith("fence_") else
                                            "wall" if "wall" in root_name or "gatehouse" in root_name else "building"})
        # Descendant levels can override the parent's selection. Inherit when absent.
        for j, child in enumerate(node.findall("children/game_entity")):
            walk(child, frame, levels, visible, root_name, root_index, root_z, chain + [j])

    for j, source in enumerate(scene.find("entities")):
        name = source.get("prefab", source.get("name", ""))
        if not ARCHITECTURE.match(name):
            continue
        pos = transform_frame(source)[:3, 3]
        # Explicit bounded audit, with 30m root margin for large building pieces.
        if not (x0 - 30 <= pos[0] <= x1 + 30 and y0 - 30 <= pos[1] <= y1 + 30):
            continue
        before = len(records)
        walk(expand(source), np.eye(4), set(), True, name, j, float(pos[2]), [])
        if len(records) == before:
            skipped.append({"index": j, "prefab": name, "reason": "no visible mesh in selected civilian/wall levels"})
    if len({r["mesh"] for r in records}) > 240:
        raise ValueError("Local architecture export exceeds the 240-mesh budget")
    return records, used_sources, skipped


def obj_geometry(asset_dir, name):
    path = asset_dir / name.split("_", 1)[0] / (name + ".obj")
    info = json.loads(path.with_suffix(".mat_map.json").read_text(encoding="utf-8"))
    lod0 = {s["name"] for s in info["submeshes"] if s["lod"] == 0}
    vertices, faces, current = [], [], None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("o "):
            current = line[2:]
        elif line.startswith("v "):
            vertices.append([float(v) for v in line.split()[1:4]])
        elif line.startswith("f ") and current in lod0:
            ids = [int(v.split("/")[0]) for v in line.split()[1:]]
            ids = [v - 1 if v > 0 else len(vertices) + v for v in ids]
            faces.extend([[ids[0], ids[i], ids[i + 1]] for i in range(1, len(ids) - 1)])
    if not faces:
        raise ValueError("Missing LOD0 faces: " + name)
    return np.asarray(vertices), np.asarray(faces), path


def export_meshes(game_root, names, asset_dir, cli):
    # Argument array; no shell interpolation. Uses the existing asset exporter.
    result = subprocess.run([str(cli), "dump", "--packdir", str(game_root / "Modules/Native/AssetPackages"),
                             "--filter", ",".join(sorted(names)), "--format", "obj", "--out", str(asset_dir)],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    (asset_dir / "511-建筑模型导出日志.txt").write_text(result.stdout + result.stderr, encoding="utf-8")
    if result.returncode:
        raise ValueError("tpaccli export failed; see 511-建筑模型导出日志.txt")


def projection(vertices, faces):
    triangles = vertices[faces, :2]
    polygons = shapely.polygons(triangles)
    polygons = polygons[shapely.area(polygons) > 1e-8]
    return shapely.union_all(polygons, grid_size=.01)


def horizontal_section(vertices, faces, z):
    """Closed contour polygons at a world height; preserves openings, no convex hull."""
    triangles = vertices[faces]
    selected = triangles[(triangles[:, :, 2].min(1) < z) & (triangles[:, :, 2].max(1) > z)]
    segments = []
    for tri in selected:
        points = []
        for a, b in zip(tri, np.roll(tri, -1, axis=0)):
            if (a[2] < z <= b[2]) or (b[2] < z <= a[2]):
                p = a + (b - a) * ((z - a[2]) / (b[2] - a[2]))
                points.append(np.round(p[:2], 4))
        if len(points) == 2 and np.linalg.norm(points[0] - points[1]) > 1e-6:
            segments.append(LineString(points))
    if not segments:
        return Polygon()
    contours = list(polygonize(unary_union(segments)))
    # polygonize emits nested rings too. Odd/even shells retain holes.
    result = Polygon()
    for p in contours:
        result = result.symmetric_difference(Polygon(p.exterior))
    return result


def scene_geometry(records, asset_dir, bounds):
    cache, files, result = {}, set(), []
    crop = shapely.box(bounds[0], bounds[2], bounds[1], bounds[3])
    for record in records:
        name = record["mesh"]
        if name not in cache:
            vertices, faces, path = obj_geometry(asset_dir, name)
            cache[name] = vertices, faces
            files.update([path, path.with_suffix(".mat_map.json")])
        vertices, faces = cache[name]
        frame = np.array(record["frame"])
        world = vertices @ frame[:3, :3].T + frame[:3, 3]
        if not shapely.box(*np.r_[world[:, :2].min(0), world[:, :2].max(0)]).intersects(crop):
            continue
        plan = projection(world, faces).intersection(crop)
        if plan.is_empty:
            continue
        # Three samples of a body-height band. This is a visual structure audit,
        # not PhysicsShape or NavMesh, and not an exact swept collision volume.
        section = Polygon()
        if record["category"] not in {"road", "door"}:
            section = shapely.union_all([horizontal_section(world, faces, record["root_z"] + h)
                                         for h in (.3, .9, 1.6)]).intersection(crop)
        item = dict(record, z_range=world[:, 2].min().item())
        item["z_range"] = [float(world[:, 2].min()), float(world[:, 2].max())]
        item["section_heights_m"] = [record["root_z"] + h for h in (.3, .9, 1.6)]
        item["projection"], item["section"] = mapping(plan), mapping(section)
        result.append((item, plan, section))
    return result, files
