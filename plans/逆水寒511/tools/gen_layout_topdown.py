"""511 layout audit: real terrain.bin height tiles + existing Stage placement.

Read-only for game assets. Output goes to the module's Debug/offline directory.
This is an offline layout audit, not an engine screenshot or approved new layout.
Uses the existing tpaccli OBJ/shape exports; never invents model dimensions.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

import lz4.block
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource, LinearSegmentedColormap
from matplotlib.font_manager import FontProperties
from matplotlib.patches import Circle, Polygon, Patch, PathPatch
from matplotlib.path import Path as PlotPath
import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates
from scipy.spatial import ConvexHull
import shapely
from shapely.geometry import LineString, Point
from shapely.geometry.polygon import orient

from scene_layout_geometry import collect_scene_meshes, scene_geometry, export_meshes

ROOT = Path(__file__).resolve().parents[3]
DATA = ROOT / "ModuleData/DesignData"
NAMES = {"pawnbroker": "老周", "widow": "寡妇", "son": "小贼", "butcher": "屠户",
         "blocker": "铁头", "thug": "打手", "debtor": "债务人", "rival": "情敌", "outlaw": "恶霸"}
PROP_NAMES = {"pawnbroker_desk": "当铺长桌", "pawnbroker_scale": "当铺桶",
              "widow_stage_a": "台桌A", "widow_stage_b": "台桌B", "widow_carpet": "地毯",
              "butcher_block": "肉案", "butcher_barrel": "肉案桶"}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_terrain(scene_dir):
    scene = ET.parse(scene_dir / "scene.xscene").getroot()
    attr = scene.find("terrain").attrib
    nx, ny = int(attr["node_dimension_x"]), int(attr["node_dimension_y"])
    size = float(attr["node_size"])
    data = (scene_dir / "terrain.bin").read_bytes()
    if data[:8] != b"ZGR6RTRN" or struct.unpack_from("<I", data, 8)[0] != 2:
        raise ValueError("Unsupported terrain container; use existing custom.export_heightmap instead")
    directory = {}
    for off in range(12, 112, 20):
        tag = data[off:off + 4].decode("ascii")
        flag, start, length, rawlen = struct.unpack_from("<IIII", data, off + 4)
        if flag != 1:
            raise ValueError("Unsupported terrain directory flag")
        directory[tag] = (start, length, rawlen)
    start, length, rawlen = directory["HGHT"]
    count = nx * ny
    offsets = struct.unpack_from("<" + "I" * count, data, start)
    section = data[start + 4 * count:start + 4 * count + length]
    # This native format has a 40-byte stream descriptor, then an LZ4 block.
    # Refuse unknown descriptors instead of guessing a different container.
    if struct.unpack_from("<I", section)[0] != 3:
        raise ValueError("Unsupported HGHT stream descriptor")
    raw = lz4.block.decompress(section[40:], uncompressed_size=rawlen)
    if len(raw) != rawlen:
        raise ValueError("HGHT decoded length mismatch")
    tiles, cursor, sources = [], 0, []
    for index, expected in enumerate(offsets):
        if cursor != expected:
            raise ValueError("HGHT node directory mismatch")
        length = struct.unpack_from("<I", raw, cursor)[0]
        png = raw[cursor + 4:cursor + 4 + length]
        cursor += 4 + length
        Image.open(io.BytesIO(png)).verify()  # includes PNG chunk CRC checks
        tile = np.asarray(Image.open(io.BytesIO(png)))
        if tile.ndim != 2 or not np.issubdtype(tile.dtype, np.integer):
            raise ValueError("HGHT is not an integer grayscale image")
        if png[24:26] != bytes([16, 0]):
            raise ValueError("This decoder requires native 16-bit gray HGHT nodes")
        tiles.append(tile.astype(np.float64))
        sources.append({"index": index, "bytes": length, "sha256": hashlib.sha256(png).hexdigest()})
    if cursor != len(raw) or len({t.shape for t in tiles}) != 1:
        raise ValueError("HGHT trailing bytes or inconsistent node dimensions")
    edge = tiles[0].shape[0]
    if tiles[0].shape != (edge, edge):
        raise ValueError("Non-square HGHT node")
    stride = edge - 1
    grid = np.full((ny * stride + 1, nx * stride + 1), np.nan)
    seam_max = 0.0
    for index, tile in enumerate(tiles):
        y, x = divmod(index, nx)
        dst = grid[y * stride:y * stride + edge, x * stride:x * stride + edge]
        shared = np.isfinite(dst)
        if shared.any():
            seam_max = max(seam_max, float(np.max(np.abs(dst[shared] - tile[shared]))))
        dst[:] = tile
    if np.isnan(grid).any() or seam_max > 1:
        raise ValueError("HGHT holes or inconsistent shared node edges")
    low, high = float(attr["min_height"]), float(attr["max_height"])
    grid = low + grid * (high - low) / 65535.0
    meta = {"nodes": [nx, ny], "node_size_m": size, "world_size_m": [nx * size, ny * size],
            "grid_size": list(grid.shape[::-1]), "spacing_m": size / stride,
            "height_range_m": [low, high], "shared_edge_max_raw_delta": seam_max,
            "png_nodes": sources, "world_origin_xy": [0, 0],
            "axis_mapping": "array[row=worldY, col=worldX]; nodes are Y-major"}
    return scene, grid, meta


def sample(grid, points, spacing):
    points = np.asarray(points)
    return map_coordinates(grid, [points[:, 1] / spacing, points[:, 0] / spacing], order=1, mode="nearest")


def mesh_vertices(asset_dir, name):
    path = asset_dir / name.split("_", 1)[0] / (name + ".obj")
    mapping = json.loads(path.with_suffix(".mat_map.json").read_text(encoding="utf-8"))
    lod0 = {m["name"] for m in mapping["submeshes"] if m["lod"] == 0}
    current, verts = None, []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("o "):
            current = line[2:]
        elif line.startswith("v ") and current in lod0:
            verts.append([float(n) for n in line.split()[1:4]])
    if not verts:
        raise ValueError("No LOD0 vertices for " + name)
    return np.asarray(verts), path


def prop_basis(yaw):
    angle = math.radians(yaw)
    # Matches decompiled Mat3.CreateMat3WithForward: s=cross(f, +Z).
    forward = np.array([math.cos(angle), math.sin(angle)])
    side = np.array([forward[1], -forward[0]])
    return np.column_stack((side, forward))


def draw_geometry(ax, geometry, **style):
    if geometry.is_empty:
        return
    if geometry.geom_type in {"MultiPolygon", "GeometryCollection"}:
        for part in geometry.geoms:
            draw_geometry(ax, part, **style)
    elif geometry.geom_type == "Polygon":
        geometry = orient(geometry, sign=1)
        verts, codes = [], []
        for ring in [geometry.exterior, *geometry.interiors]:
            points = np.asarray(ring.coords)
            verts.extend(points)
            codes.extend([PlotPath.MOVETO] + [PlotPath.LINETO] * (len(points) - 2) + [PlotPath.CLOSEPOLY])
        ax.add_patch(PathPatch(PlotPath(verts, codes), **style))


def scale_bar(ax, extent, length):
    x0, x1, y0, y1 = extent
    x, y = x0 + .06 * (x1 - x0), y0 + .065 * (y1 - y0)
    ax.plot([x, x + length], [y, y], color="#1e293b", lw=3, zorder=20)
    for xx in (x, x + length / 2, x + length):
        ax.plot([xx, xx], [y - .3, y + .3], color="#1e293b", lw=1, zorder=20)
    ax.text(x + length / 2, y + .65, f"{length}米", ha="center", fontsize=9,
            bbox={"facecolor": "white", "edgecolor": "none", "alpha": .8}, zorder=21)
    ax.annotate("+Y", (x1 - .04 * (x1 - x0), y1 - .04 * (y1 - y0)),
                (x1 - .04 * (x1 - x0), y1 - .18 * (y1 - y0)),
                arrowprops={"arrowstyle": "->", "color": "#334155"}, fontsize=9, ha="center", zorder=20)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--scene-dir", type=Path, required=True)
    ap.add_argument("--asset-dir", type=Path, default=ROOT / "Debug/offline")
    ap.add_argument("--wall-level", type=int, choices=[1, 2, 3], default=1,
                    help="Explicit civilian reference level; actual campaign level must be captured in game")
    ap.add_argument("--export-assets", action="store_true", help="Export selected architecture using existing tpaccli")
    args = ap.parse_args()
    out = ROOT / "Debug/offline"
    if not out.is_dir():
        raise ValueError("Existing Debug/offline output directory required")
    scene, grid, terrain = read_terrain(args.scene_dir)
    scene_name = scene.attrib["name"]
    bounds = (396, 482, 390, 460)
    game_root = args.scene_dir.resolve().parents[3]
    version_path = game_root / "bin/Win64_Shipping_Client/Version.xml"
    game_version = ET.parse(version_path).getroot().find("Singleplayer").get("Value")
    records, prefab_files, skipped = collect_scene_meshes(scene, [game_root / "Modules/Native/Prefabs"], args.wall_level, bounds)
    if args.export_assets:
        export_meshes(game_root, {r["mesh"] for r in records}, args.asset_dir,
                      ROOT / "tools/tpactool/TpacToolCLI/bin/Release/net9.0/tpaccli.exe")
    architecture, mesh_files = scene_geometry(records, args.asset_dir, bounds)
    stage = json.loads((DATA / "StoryOpeningTimeline.json").read_text(encoding="utf-8"))
    beats = sorted(stage["Beats"], key=lambda beat: beat.get("At", 0))
    action_beats = {beat["Actor"]: beat for beat in beats if beat["Cmd"] == "actor_action"}
    tuning = json.loads((DATA / "StoryProbeTuning.json").read_text(encoding="utf-8"))
    gate = tuning["Gate"]["Layouts"][scene_name]
    outside, center = np.array(gate["Outside"], dtype=float), np.array(gate["Gate"], dtype=float)
    forward = center[:2] - outside[:2]
    forward /= np.linalg.norm(forward)
    right = np.array([forward[1], -forward[0]])
    gate_cfg = tuning["Gate"]
    blocker = center.copy()
    blocker[:2] -= forward * gate_cfg["OutsideMeters"]
    goal = center.copy()
    goal[:2] += forward * gate_cfg["InsideMeters"]
    anchors = {"player": outside, "gate": blocker, "gate_inner": goal}

    def placement(beat):
        return anchors[beat["Anchor"]] + np.r_[forward, 0] * beat.get("Fwd", 0) + np.r_[right, 0] * beat.get("Right", 0) + [0, 0, beat.get("Up", 0)]

    props, actors, files = [], [], [args.scene_dir / "terrain.bin", args.scene_dir / "scene.xscene",
                                  DATA / "StoryOpeningTimeline.json", DATA / "StoryProbeTuning.json",
                                  version_path, Path(__file__), Path(__file__).with_name("scene_layout_geometry.py"),
                                  *prefab_files, *mesh_files]
    for beat_index, beat in enumerate(beats):
        if beat["Cmd"] == "prop_spawn":
            vertices, path = mesh_vertices(args.asset_dir, beat["Prefab"])
            files += [path, path.with_suffix(".mat_map.json")]
            local_hull = vertices[ConvexHull(vertices[:, :2]).vertices, :2]
            basis = prop_basis(beat.get("Yaw", 0))
            pos = placement(beat)
            footprint = local_hull @ basis.T * beat.get("Scale", 1) + pos[:2]
            props.append({"id": beat["Id"], "name": PROP_NAMES[beat["Id"]], "prefab": beat["Prefab"],
                          "position": pos.tolist(), "yaw": beat.get("Yaw", 0), "basis": basis.tolist(),
                          "local_min": vertices.min(0).tolist(), "local_max": vertices.max(0).tolist(),
                          "mesh_size_m": np.ptp(vertices, axis=0).tolist(), "footprint": footprint.tolist(),
                          "local_hull": local_hull.tolist(), "scale": beat.get("Scale", 1),
                          "number": f"P{len(props) + 1}", "beat_index": beat_index, "spawn_at_sec": beat["At"],
                          "anchor": beat["Anchor"], "offset_fru": [beat.get(k, 0) for k in ("Fwd", "Right", "Up")],
                          "status": "Measured same-name LOD0 mesh projection; compiled prefab root binding unverified"})
        elif beat["Cmd"] == "actor_place":
            pos = placement(beat)
            angle = math.atan2(*(outside[:2] - pos[:2])[::-1]) if beat.get("Facing", 0) < -.5 else math.radians(beat.get("Facing", 0))
            actors.append({"id": beat["Actor"], "name": NAMES[beat["Actor"]], "position": pos.tolist(),
                           "forward": [math.cos(angle), math.sin(angle)],
                           "number": f"A{len(actors) + 1}", "beat_index": beat_index, "place_at_sec": beat["At"],
                           "anchor": beat["Anchor"], "offset_fru": [beat.get(k, 0) for k in ("Fwd", "Right", "Up")],
                           "facing_degrees": math.degrees(angle) % 360, "civilian": beat.get("Civilian", True),
                           "opening_action": action_beats.get(beat["Actor"], {}).get("Action"),
                           "action_at_sec": action_beats.get(beat["Actor"], {}).get("At"),
                           "reservation_radius_m": .45, "status": "Requested placement; 0.45m design reservation is not a measured Agent capsule"})

    points = []
    for entity in scene.find("entities"):
        transform = entity.find("transform")
        if entity.get("prefab", "").startswith("sp_") and transform is not None:
            pos = [float(n) for n in transform.attrib["position"].split(",")]
            if 0 <= pos[0] <= terrain["world_size_m"][0] and 0 <= pos[1] <= terrain["world_size_m"][1]:
                points.append(pos)
    error = np.abs(sample(grid, points, terrain["spacing_m"]) - np.asarray(points)[:, 2])
    calibration = {"source_spawn_points": len(points), "median_abs_z_error_m": float(np.median(error)),
                   "within_1m": int(np.sum(error < 1)), "note": "Ground-near references; floors/platforms can differ from terrain"}
    for item in props + actors:
        item["terrain_height_m"] = float(sample(grid, [item["position"]], terrain["spacing_m"])[0])
        item["requested_z_minus_terrain_m"] = item["position"][2] - item["terrain_height_m"]

    by_id = {p["id"]: p for p in props}
    issues = []
    for actor_id, prop_id in [("pawnbroker", "pawnbroker_desk"), ("butcher", "butcher_block")]:
        actor = next(a for a in actors if a["id"] == actor_id)
        prop = by_id[prop_id]
        delta = np.array(prop["position"][:2]) - actor["position"][:2]
        angle = math.degrees(math.acos(np.clip(np.dot(delta / np.linalg.norm(delta), actor["forward"]), -1, 1)))
        issues.append({"actor": actor_id, "prop": prop_id, "angle_to_prop_degrees": angle,
                       "result": "Prop origin behind initial actor facing" if angle > 90 else "Prop origin in front"})

    # A convex visual envelope is a conservative warning, not a collision verdict.
    hulls = [(p, ConvexHull(np.asarray(p["footprint"]))) for p in props]
    overlaps = []
    for i, (a, ha) in enumerate(hulls):
        for b, hb in hulls[i + 1:]:
            pa, pb = np.asarray(a["footprint"]), np.asarray(b["footprint"])
            axes = np.r_[ha.equations[:, :2], hb.equations[:, :2]]
            if all(min(np.max(pa @ axis), np.max(pb @ axis)) > max(np.min(pa @ axis), np.min(pb @ axis)) for axis in axes):
                overlaps.append([a["id"], b["id"]])

    plt.rcParams["font.family"] = FontProperties(fname="C:/Windows/Fonts/msyh.ttc").get_name()
    plt.rcParams["axes.unicode_minus"] = False
    width, height = terrain["world_size_m"]
    spacing = terrain["spacing_m"]
    terrain_cmap = LinearSegmentedColormap.from_list("survey", ["#f6f4e9", "#e7e4d6", "#c9c6b9"])
    hill = LightSource(azdeg=315, altdeg=45).shade(grid, cmap=terrain_cmap,
                vert_exag=1, dx=spacing, dy=spacing, blend_mode="soft")
    hill = .55 + .45 * hill
    structural = shapely.union_all([s for i, p, s in architecture])
    actor_structure_candidates = []
    for actor in actors:
        reservation = Point(actor["position"][:2]).buffer(actor["reservation_radius_m"])
        hits = sorted({i["root_prefab"] for i, p, s in architecture if s.intersects(reservation)})
        if hits:
            actor_structure_candidates.append({"actor": actor["id"], "structures": hits,
                                                "status": "Mesh section/reservation overlap; support/collision must be verified"})
    structural_actor_ids = {a["actor"] for a in actor_structure_candidates}
    route = LineString([outside[:2], center[:2], goal[:2]])
    route_crossings = structural.intersection(route)
    gatehouse_section = shapely.union_all([s for i, p, s in architecture if "gatehouse" in i["root_prefab"]])
    opening_cross = LineString([center[:2] - right * 15, center[:2] + right * 15]).difference(gatehouse_section)
    segments = list(opening_cross.geoms) if opening_cross.geom_type == "MultiLineString" else [opening_cross]
    opening = next((s for s in segments if s.geom_type == "LineString" and s.distance(Point(center[:2])) < 1e-5), None)
    # Design area, derived from current cast positions. Not a runtime roam rule.
    street_xy = np.array([a["position"][:2] for a in actors if a["id"] != "blocker"])
    activity_area = shapely.MultiPoint(street_xy).convex_hull.buffer(2).difference(structural)
    # One map only. Point cards are part of the same sheet, never extra map panels.
    companion = outside + np.r_[-forward, 0] * 1.2 + np.r_[-right, 0] * 1.5
    key_points = [
        {"number": "K1", "name": "玩家出生 / player基准", "position": outside.tolist()},
        {"number": "K2", "name": "配置门中心", "position": center.tolist()},
        {"number": "K3", "name": "守卫岗位 / gate锚点 = A5", "position": blocker.tolist()},
        {"number": "K4", "name": "门内目标 / gate_inner", "position": goal.tolist()},
        {"number": "K5", "name": "额外同伴请求点（有可用Hero才生成）", "position": companion.tolist()},
    ]
    files.append(ROOT / "ExampleModVS/ExampleMod/ExampleMod/Interaction/StoryProbe/StoryProbeMissionBehavior.cs")
    fig = plt.figure(figsize=(32, 25), facecolor="#faf9f5")
    overview = fig.add_axes([.035, .318, .595, .557])
    extent = bounds
    ax = overview
    xs = np.arange(grid.shape[1]) * spacing
    ys = np.arange(grid.shape[0]) * spacing
    yy, xx = np.gradient(grid, spacing)
    slope = np.degrees(np.arctan(np.hypot(xx, yy)))
    ax.imshow(hill, extent=[-spacing / 2, width + spacing / 2, -spacing / 2, height + spacing / 2], origin="lower")
    ax.contourf(xs, ys, slope, levels=[35, 90], colors=["#9f8777"], alpha=.2, zorder=1)
    ax.set(xlim=extent[:2], ylim=extent[2:], xlabel="世界X（米）", ylabel="世界Y（米）", aspect="equal")
    ax.set_title("唯一主地图｜名称就近标注，编号对应本张图的点位资料", fontsize=16, pad=15, loc="left", weight="bold")
    ax.grid(alpha=.14, color="#64748b", linewidth=.5)
    contours = ax.contour(xs, ys, grid, levels=np.arange(60, 90, 2), colors="#9b987f", linewidths=.45, alpha=.6, zorder=1)
    ax.clabel(contours, fontsize=8, inline=True, fmt="%dm")
    for item, plan, section in architecture:
        category = item["category"]
        if category == "door":
            draw_geometry(ax, plan, facecolor="none", edgecolor="#a14b69", lw=.8, linestyle="--", zorder=5)
        elif category == "road":
            draw_geometry(ax, plan, facecolor="#e0c79f", edgecolor="#ad946e", lw=.25, alpha=.65, zorder=2)
        else:
            draw_geometry(ax, plan, facecolor="#c7cdd3", edgecolor="#7e8894", lw=.4, alpha=.65, zorder=3)
            draw_geometry(ax, section, facecolor="#5f6b77", edgecolor="#465361", lw=.5, alpha=.85, zorder=4)
    draw_geometry(ax, activity_area, facecolor="#7dd3c5", edgecolor="#12826a", lw=1, linestyle="--", alpha=.13, zorder=4)
    draw_geometry(ax, route.buffer(.8).difference(structural), facecolor="#68b6db", edgecolor="none", alpha=.3, zorder=5)
    coords = np.array(route.coords)
    ax.plot(coords[:, 0], coords[:, 1], color="#087caa", linestyle="--", lw=1.7, zorder=7)
    ax.annotate("", center[:2] + forward * 5, center[:2] - forward * 5,
                arrowprops={"arrowstyle": "->", "color": "#087caa", "lw": 1.7}, zorder=9)

    label_boxes = []
    def point_label(label, point, color, offset=(0, 0), align="center", size=9, leader=False):
        # Offset is in display points, so the label stays beside its actual shape.
        style = {"arrowprops": {"arrowstyle": "-", "color": color, "lw": .7}} if leader else {}
        vertical = "bottom" if offset[1] > 0 else "top" if offset[1] < 0 else "center"
        artist = ax.annotate(label, point, xytext=offset, textcoords="offset points",
                             ha=align, va=vertical, fontsize=size, color=color,
                             bbox={"facecolor": "#fffef9", "edgecolor": "none", "alpha": .91,
                                   "pad": 1.2}, zorder=25, **style)
        label_boxes.append(artist)

    # Default to text immediately beside actors; only dense prop clusters use short leaders.
    actor_labels = {"pawnbroker": ((10, 4), "left"), "widow": ((-9, 9), "right"),
                    "son": ((10, 4), "left"), "butcher": ((10, 7), "left"),
                    "blocker": ((0, 13), "center"), "thug": ((10, 7), "left"),
                    "debtor": ((-9, -9), "right"), "rival": ((-9, 9), "right"),
                    "outlaw": ((0, -14), "center")}
    for actor in actors:
        pos = np.array(actor["position"][:2]); af = np.array(actor["forward"])
        ax.add_patch(Circle(pos, .45, facecolor="#a2ded0", edgecolor="#126a59", linestyle=":", lw=1, zorder=9))
        color = "#a73535" if actor["id"] in structural_actor_ids else "#074c3b"
        if actor["id"] in structural_actor_ids:
            ax.add_patch(Circle(pos, .75, fill=False, edgecolor=color, lw=1.4, zorder=12))
        ax.annotate("", pos + af, pos, arrowprops={"arrowstyle": "->", "color": "#126a59", "lw": 1.2}, zorder=11)
        label = actor["number"] + " " + actor["name"]
        if actor["id"] == "blocker": label += "\nK3岗位"
        if actor["id"] in structural_actor_ids: label += " !"
        offset, align = actor_labels[actor["id"]]
        point_label(label, pos, color, offset, align)
    prop_labels = {"pawnbroker_desk": ("P1\n当铺\n长桌", (0, 0), "center", False),
                   "pawnbroker_scale": ("P2 当铺桶", (5, -14), "center", True),
                   "widow_stage_a": ("P3 台桌A", (0, 0), "center", False),
                   "widow_stage_b": ("P4 台桌B", (0, 0), "center", False),
                   "widow_carpet": ("P5 地毯", (12, -14), "left", True),
                   "butcher_block": ("P6\n肉案", (0, 0), "center", False),
                   "butcher_barrel": ("P7 肉案桶", (10, 11), "left", True)}
    for prop in props:
        hull = np.asarray(prop["footprint"])
        ax.add_patch(Polygon(hull, facecolor="#eab56f" if prop["id"] != "widow_carpet" else "#ebd2b7",
                             edgecolor="#8b5925", lw=.8, alpha=.9, zorder=8))
        pos = np.array(prop["position"][:2]); pf = np.array(prop["basis"])[:, 1]
        ax.scatter(*pos, marker="s", s=13, color="#8b5925", zorder=12)
        ax.annotate("", pos + pf * .8, pos, arrowprops={"arrowstyle": "->", "color": "#8b5925", "lw": 1.1}, zorder=11)
        label, offset, align, leader = prop_labels[prop["id"]]
        label_at = np.array(shapely.Polygon(hull).centroid.coords[0]) if not leader else pos
        point_label(label, label_at, "#8b5925", offset, align, size=8, leader=leader)
    for key, offset, align in [(key_points[0], (0, 10), "center"), (key_points[1], (0, -11), "center"),
                               (key_points[3], (0, -11), "center"), (key_points[4], (6, -10), "left")]:
        point = np.array(key["position"][:2])
        ax.scatter(*point, marker="D" if key["number"] != "K5" else "*", s=48,
                   color="#087caa", edgecolors="white", lw=.7, zorder=13)
        short_name = {"K1": "玩家出生", "K2": "门中心", "K4": "门内目标", "K5": "条件同伴"}[key["number"]]
        point_label(key["number"] + " " + short_name, point, "#087caa", offset, align)
    opening_text = "门洞截面开口待核" if opening is None else f"截面模型开口 {opening.length:.2f}m"
    for label, at in [
        ("城门楼 / 门洞\n" + opening_text + "\n（非通行净宽）", (425.5, 430)),
        ("原城墙", (427, 447)),
        ("城内铺装道路", (416, 438)),
        ("庭院围墙", (409, 418)),
        ("原场景围栏", (450, 406)),
        ("原粮仓\nA7/A9重叠候选", (468, 408.5)),
    ]:
        point_label(label, at, "#526070", size=9)
    scale_bar(ax, extent, 10)
    ax.text(399, 457, "城内", fontsize=15, color="#466079", weight="bold", zorder=20)
    ax.text(449, 457, "城外市井", fontsize=15, color="#466079", weight="bold", zorder=20)

    fig.text(.035, .966, "511 城门与市井｜单图点位、布景与开场执行说明", fontsize=27, weight="bold", color="#243444")
    fig.text(.035, .942, f"真实高度图 + 原场景建筑道路 + 现有第二阶段台本    {game_version} / {scene_name}    参考层：civilian / level_{args.wall_level}", fontsize=13, color="#526070")
    fig.text(.035, .918, "当前布局复核：9名台本演员 + 7件道具 + 条件同伴；原版居民仍会生成。首遇只铁头，双守卫及演员专用入口尚未实施。", fontsize=13, color="#9a553d")
    fig.text(.65, .882, "A1–A9 演员资料｜箭头是初始朝向", fontsize=16, weight="bold", color="#074c3b")
    fig.text(.65, .865, "同次Begin创建/复用9人；0.5s同帧摆位，0.7s同帧发起动作。", fontsize=10, color="#526070")
    for i, actor in enumerate(actors):
        y = .846 - i * .052
        pos = actor["position"]
        color = "#a73535" if actor["id"] in structural_actor_ids else "#074c3b"
        fig.text(.65, y, f"{actor['number']}  {actor['name']}   /   {actor['id']}   /   {'平民装' if actor['civilian'] else '战斗装'}", fontsize=12, weight="bold", color=color)
        fig.text(.65, y - .011, f"XYZ=({pos[0]:.3f}, {pos[1]:.3f}, {pos[2]:.3f})m   朝向={actor['facing_degrees']:.1f}°", fontsize=10, color="#334155")
        f, r, u = actor["offset_fru"]
        fig.text(.65, y - .022, f"锚点={actor['anchor']}  前/右/上=({f:g}, {r:g}, {u:g})m   地形Z={actor['terrain_height_m']:.3f}m", fontsize=10, color="#526070")
        action = actor["opening_action"] or "（无开场动作）"
        if len(action) > 45: action = action[:action.rfind('_', 0, 42) + 1] + "\n" + action[action.rfind('_', 0, 42) + 1:]
        note = "粮仓模型截面重叠候选" if actor["id"] in structural_actor_ids else "身体/动作空间待实机核"
        if actor["id"] in {"pawnbroker", "butcher"}: note = "桌案在初始朝向后侧；工作朝向待调整"
        fig.text(.65, y - .028, "0.7s动作：" + action, fontsize=9, color="#334155", linespacing=1.05, va="top")
        fig.text(.65, y - .045, note, fontsize=9, color="#9a553d")
    fig.text(.65, .365, "K1–K5 关键点｜XYZ为请求值，非脚下实测值", fontsize=13, weight="bold", color="#087caa")
    for i, key in enumerate(key_points):
        pos = key["position"]
        fig.text(.65, .350 - i * .011, f"{key['number']} {key['name']}   ({pos[0]:.3f}, {pos[1]:.3f}, {pos[2]:.3f})m", fontsize=9, color="#334155")

    fig.text(.035, .288, "P1–P7 道具资料｜实测尺寸按模型本地X×Y×Z；转向后占地见地图。Yaw从世界+X向+Y计，不是演员相对方向。", fontsize=13, weight="bold", color="#8b5925")
    for i, prop in enumerate(props):
        column, row = i % 4, i // 4
        x, y = .035 + column * .240, .267 - row * .076
        pos = prop["position"]; size = prop["mesh_size_m"]
        fig.text(x, y, f"{prop['number']}  {prop['name']} / {prop['id']}", fontsize=12, weight="bold", color="#8b5925")
        fig.text(x, y - .013, f"Prefab={prop['prefab']}  /  #{prop['beat_index']} @ {prop['spawn_at_sec']:g}s", fontsize=10, color="#334155")
        fig.text(x, y - .026, f"XYZ=({pos[0]:.3f}, {pos[1]:.3f}, {pos[2]:.3f})m", fontsize=10, color="#334155")
        fig.text(x, y - .039, f"尺寸={size[0]:.3f}×{size[1]:.3f}×{size[2]:.3f}m  Yaw={prop['yaw']:g}°  Scale={prop['scale']:g}", fontsize=10, color="#526070")
        f, r, u = prop["offset_fru"]
        fig.text(x, y - .052, f"{prop['anchor']} 前/右/上=({f:g}, {r:g}, {u:g})m   地形Z={prop['terrain_height_m']:.3f}m", fontsize=10, color="#526070")
        note = {"pawnbroker_desk": "与P2投影重叠；A1朝向待调整", "pawnbroker_scale": "与P1投影重叠候选",
                "butcher_block": "与P7重叠；请求Z低于地形约0.78m", "butcher_barrel": "与P6重叠；请求Z低于地形约1.01m"}.get(prop["id"], "桌/地毯重叠可能合理；支撑面及碰撞待核")
        fig.text(x, y - .065, note, fontsize=9, color="#9a553d")
    fig.text(.755, .191, "点位读法与实施边界", fontsize=12, weight="bold", color="#243444")
    fig.text(.755, .178, "A=演员，P=道具，K=关键点；同号对应同一坐标。\nK3与A5重合：gate锚点是岗位，不是门中心。\nK5在Stage之外由C#条件生成，身份运行时确定。\n第二名守卫尚无正式站位，图中不虚构该点。\n演员圆半径0.45m是设计预留；箭头不代表完整动作。", fontsize=10, color="#526070", linespacing=1.65, va="top")

    handles = [Patch(facecolor="#c7cdd3", edgecolor="#7e8894", label="建筑上部投影"),
               Patch(facecolor="#5f6b77", label="根Z+0.3/0.9/1.6m结构截面"),
               Patch(facecolor="#e0c79f", label="原场景铺装道路"),
               Patch(facecolor="none", edgecolor="#a14b69", linestyle="--", label="门扇静态候选轮廓"),
               Patch(facecolor="#eab56f", edgecolor="#8b5925", label="台本道具投影"),
               Patch(facecolor="#a2ded0", edgecolor="#126a59", linestyle=":", label="演员设计预留 / 初始朝向"),
               Patch(facecolor="#7dd3c5", alpha=.3, edgecolor="#12826a", label="拟活动审核区（未部署AI）"),
               Patch(facecolor="#68b6db", label="设计入城路线（未证可达）")]
    fig.legend(handles=handles, loc="lower left", bbox_to_anchor=(.032, .080), ncol=4, frameon=False, fontsize=11)
    fig.text(.035, .067, "当前顺序：打开原城镇 → 解算门区/传送玩家 → Begin预切镜头/创建9演员/申请控制 → 全员可控后走30拍 → 结束归还 → 导演日常/事件。", fontsize=12, color="#243444")
    fig.text(.035, .050, "帧内执行：同At按文件顺序连续调用；9人创建同次Begin，7道具按0.20/0.24/0.28s三组。同次Tick跨过多组时间时，会在同帧执行。", fontsize=12, color="#243444")
    fig.text(.035, .033, "离线来源可追溯；参考墙级、门扇运行态、人物身体、支撑面及NavMesh未闭合实机验证。截面/投影交叉是疑点，不是已确认碰撞。", fontsize=11, color="#9a553d")
    fig.text(.035, .017, "横纵等比例；世界+Y朝图上方，不冒称地理北。浅色地形不保证可站立。门内目标仍位于门楼投影；此图不宣称现有布局已准确或全部可玩。", fontsize=11, color="#526070")
    fig.canvas.draw()
    renderer = fig.canvas.get_renderer()
    # A practical delivery check: point/context labels must fit and must not cover each other.
    label_bounds = [a.get_bbox_patch().get_window_extent(renderer) for a in label_boxes]
    for i, box in enumerate(label_bounds):
        if not ax.get_window_extent(renderer).contains(box.x0, box.y0) or not ax.get_window_extent(renderer).contains(box.x1, box.y1):
            raise ValueError("Map label outside map: " + label_boxes[i].get_text())
        for j in range(i):
            if box.overlaps(label_bounds[j]):
                raise ValueError("Map labels overlap: " + label_boxes[i].get_text() + " / " + label_boxes[j].get_text())
    fig.savefig(out / "511-关卡俯视图.png", dpi=180)
    fig.savefig(out / "511-关卡俯视图.pdf")
    plt.close(fig)

    report = {"scene": scene_name, "source_scene_dir": str(args.scene_dir.resolve()), "source_game_version": game_version, "terrain": terrain,
              "calibration": calibration, "sources": {str(p): digest(p) for p in dict.fromkeys(files)},
              "sheet": {"map_count": 1, "point_label_style": "on-shape or adjacent; short leaders only for three dense prop points", "point_labels": [a["number"] for a in actors] + [p["number"] for p in props] + [k["number"] for k in key_points]},
              "key_points": key_points, "actors": actors, "props": props, "facing_issues": issues, "visual_envelope_overlap_candidates": overlaps,
              "actor_structure_overlap_candidates": actor_structure_candidates,
              "architecture": {"selected_levels": ["civilian", f"level_{args.wall_level}"],
                               "actual_campaign_wall_level": "unverified", "audit_bounds_xy": bounds,
                               "root_selection_margin_m": 30, "mesh_instance_count": len(architecture),
                               "geometry": [i for i, p, s in architecture], "skipped_roots": skipped,
                               "mode_filter": "civilian and selected wall group; descendants inherit levels unless overridden",
                               "body_section_basis": "root Z + 0.3/0.9/1.6m mesh contours; not PhysicsShape/NavMesh",
                               "dynamic_gates": "normal state only; static mesh envelope, live pose unverified"},
              "design_overlay": {"activity_area": shapely.geometry.mapping(activity_area),
                                 "entry_route": shapely.geometry.mapping(route),
                                 "route_structure_slice_crossings": shapely.geometry.mapping(route_crossings),
                                 "gatehouse_opening_width_m": None if opening is None else opening.length,
                                 "gatehouse_opening_cross_section": None if opening is None else shapely.geometry.mapping(opening),
                                 "status": "Review overlays only, not deployed roam/pathing rules"},
              "limitations": ["Offline terrain/layout audit; no in-game verification",
                              "Same-name metamesh projection does not prove compiled prefab hierarchy",
                              "Actor reservation is a design allowance; Agent scale/body/capsule/animation remain unmeasured",
                              "Architecture comes from Native XML/LOD0 meshes, not an engine snapshot; runtime layers/overrides remain unverified",
                              "Structure slices are visual geometry, not measured PhysicsShape, exact body sweep or NavMesh",
                              "Dynamic gate skeletal pose is not reconstructed; static closed-pose envelope is shown separately",
                              "Terrain does not determine actual support height on paving, stairs or bridges",
                              "Current Stage only; proposed two-guard layout not implemented"]}
    (out / "511-空间复核数据.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    Image.fromarray(np.rint((grid - terrain["height_range_m"][0]) * 65535 / (terrain["height_range_m"][1] - terrain["height_range_m"][0])).astype(np.uint16)).save(out / "511-场景高度图16bit.png")
    print(f"PASS: {scene_name}; grid={grid.shape}; nodes={len(terrain['png_nodes'])}; seam_delta={terrain['shared_edge_max_raw_delta']}")
    print(f"Calibration: {calibration['source_spawn_points']} points; median_abs_z_error_m={calibration['median_abs_z_error_m']:.4f}")
    print(f"Layout: actors={len(actors)} props={len(props)}; visual overlap candidates={overlaps}")
    print(f"Architecture: {len(architecture)} mesh instances; civilian/level_{args.wall_level}; route slice crossing length={route_crossings.length:.3f}m (not a NavMesh verdict)")
    print("Wrote one map sheet: 511-关卡俯视图.png/pdf; data: 511-场景高度图16bit.png, 511-空间复核数据.json")


if __name__ == "__main__":
    main()
