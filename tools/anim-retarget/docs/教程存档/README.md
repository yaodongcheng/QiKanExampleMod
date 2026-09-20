# 教程存档 —— 仅供参考，不要照抄

这里是**原始素材 + 归档件**，不属于当前管线，也不要在新导出里引用。

| 文件 | 说明 |
|---|---|
| `骑砍Ⅱ霸主MOD开发-使用ModdingKit+OpenTrf导入导出骨骼动画SkeletonAnimation.mp4` | 教程原片。**5:53–5:55** 那一小段是作者下拉展示 Blender 导出脚本的镜头 |
| `trf_skeleton_animation_exporter.py` | 把上面那 5:53–5:55 的镜头**逐帧还原**出来的脚本，133 行，与视频完全一致（未改动） |

## ⚠️ 为什么不能照抄这份脚本

它写出去的是：

```python
pose_bone.rotation_quaternion      # ← 相对静止姿势的「增量」
pose_bone.location
```

而 TRF 的**旋转轨**要的是**绝对局部变换**（等价于 FBX 节点的 `Lcl Rotation`）；
**平移轨**要的是**纯增量**（`rest_rot @ location`，不叠加 `rest.translation`）。
🔴 **两条轨道语义不一样**，本页正文只讲了旋转那一半，别以为补上静止朝向就万事大吉。

骑砍 `human_skeleton` 是 A-pose、每根骨静止朝向都不同 —— **增量当绝对用，等于把每根骨强行摆到零旋转**，实机表现就是人趴在地上、四肢乱折。

这份脚本只有在「骨骼静止朝向非常规整」的情况下才和绝对语义等价（作者的测试骨架可能正好是那种），换成骑砍骨架就不成立。

## 正确做法

见工具说明书 `../../README.md`：

- 正确脚本：`../../pipeline/common/fbx_to_trf.py`
- 正确产物（**实机验证通过的黄金样本**）：`tools/OpenTrf/out/sw2_gunner_p006_alig_abs.trf`
  —— md5 `b78076e31c1e5f38…`，位置轨首帧 `(0, 0, 0)`。同一份已进 `TaikouAnim/AssetSources/animations/gun/`
- 判定它对不对：**位置轨首帧 ≈ 0**（纯增量），且脚本打印 `CHECK_OK`；
  **量级是米（≈0.86）反而是错的**（那是平移误用了绝对语义，实机整体抬高约 6cm）

> 完整语义说明见 `../../README.md` §三 与 `../../docs/TRF规范.md` §1。
