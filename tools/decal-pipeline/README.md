# decal-pipeline —— 贴花形状贴图生成（2026-10-06 立）

> **干什么**：生成贴花用的**形状贴图**（形状 + alpha 坡），让贴花能"从边缘往中心平滑消失"。
> 与 `tools/particle-pipeline`（粒子）、`tools/armor-pipeline`（装备）并列，专管**贴花**这一条。

## 为什么需要它

贴花的形状与淡出**完全由贴图的 alpha 决定**，而硬裁（`assets.md` 里的 `alpha_test`）下这件事有数学约束 ——
正向拍脑袋调不出来（实机踩了三轮才反推明白）。这个脚本把**调通的配方**固化下来。

**起点公式**：`像素被画出来 ⟺ 贴图alpha ≥ 阈值 ÷ factor.a`（商 = **有效阈值**）。
淡出 = 有效阈值从「阈值」扫到 ∞。三条结论：

| # | 结论 | 公式 |
|---|---|---|
| 1 | 阈值同时管「满亮覆盖」与「尾段平滑」，互相拉扯（8 位量化 + 倒数） | 0.0235 → 尾段只剩 3 档；**0.2 → 23 档（推荐）** |
| 2 | **补偿曲线**：让有效阈值线性推进（已实现于 `SurfaceDecalFx.SetColor`） | `kAlpha = 阈值 ÷ (阈值 + (1−阈值)(1−a))` |
| 3 | 贴图 alpha 随半径成**抛物面**：面积线性缩 + 满亮不被啃 | `alpha(r) = 1 − (1−阈值)·(r/半径)²` |

**守恒律**：alpha 同一个值的像素必然同时过线 ⇒ 只能整块跳。「能缩掉的行程」与「满亮时被啃多少」
是同一个数 ⇒ 唯一出路 = **没有平台**（抛物面天然满足）。

## 用法

```bash
python tools/decal-pipeline/scripts/gen_decal_shape.py <痕迹源png> <输出png> [起伏幅度=0.10] [阈值=0.20]
```

- **源 png 用 ModKit 工程 `AssetSources/` 里的原始贴图**（别用从 tpac 导出的解码版 —— 是二次压缩品）
- **阈值参数必须与材质里的 `alphaTest` 一致**（抛物面按它归一化，"满亮卡在裁剪线"靠这个）
- 输出后写回发布包：

```bash
tpaccli texreplace --packdir <含包的目录> --filter <包名> --mapping <manifest.json> --out <目录> --alpha
```

## 两个坑（都踩过）

- 🔴 **`texreplace` 默认写死 DXT1（无 alpha）** —— 贴花必须加 `--alpha` 走 BC3/DXT5。
- 🔴 **`SystemFlags` 的 `has_alpha` 绝不能清** —— 清了引擎按不透明处理，`alpha_test` 永远不裁，
  画面表现是"整块方形盖住地面"，而材质与贴图逐字段回读全都正常，极难查。

## 与工程源的关系

⚠️ 这条链是**离线改发布产物** ⇒ **每次在 ModKit 里 Publish 都会冲掉**（贴图、`alpha_test` flag、阈值全部回到工程源状态）。
要长期保留：把生成的 PNG 放回 `AssetSources/` 覆盖源图 + 在材质面板里填 `alphaTest` 值；
**`alpha_test` flag 面板里没有勾选项，只能每次 Publish 后用 `tpaccli matflags --add alpha_test` 补**。

## 出处

- 原理与推导：[Knowledge/骑砍2贴花系统.md](../../Knowledge/骑砍2贴花系统.md) 卷首「2026-10-06 定案（续）」
- 轮子速查：[plans/rules/wheels.d/assets.md](../../plans/rules/wheels.d/assets.md) §21.9 ~ §21.11
- 地表系统设计：[plans/元素地表系统.md](../../plans/元素地表系统.md)
