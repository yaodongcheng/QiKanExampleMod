# 自定义 PNG 导入 GauntletUI — 通用方案

> **状态：已验证。** 2026-07-03 端到端验证，PNG 正常显示、颜色正确。
> **适用场景**：NPC 头像、警戒条、图标、装饰、立绘——任何需要自制贴图的 GauntletUI 界面。

---

## 一、背景

骑砍2 GauntletUI 的纹理系统只从 `.tpac` 包加载（`Texture.GetFromResource` 查 C++ native 资源池）。Taleworlds 未公开 `.tpac` 编译器，mod 无法打包自定义图片，只能引用引擎内置 Sprite（如 `BlankWhiteSquare_9`）。

本方案通过 **Harmony 补丁 + 正确的 SpriteData XML + PNG 格式转换** 打通了自制 PNG 进入 GauntletUI 的完整链路，**无需 `.tpac`、无需引擎工具、全版本兼容**。

---

## 二、原理

### 2.1 纹理加载链路

```
SpriteCategory.Load()
  └── TwoDimensionEngineResourceContext.LoadTexture(depot, "SpriteSheets\\{Category}\\{Category}_{N}")
      └── Texture.GetFromResource("FileName")   ← 只搜 .tpac → null
```

### 2.2 拦截点

Harmony Prefix 挂在 `TwoDimensionEngineResourceContext` 的显式接口方法上：

```
LoadTexture 请求 → .tpac 命中？→ 走原逻辑
                    ↓ 未命中
                   搜 GUI/SpriteParts/{name}.png → 转换 → 加载
```

### 2.3 通道转换

PNG 存储格式是 RGBA，引擎期望 BGRA。直接加载会红蓝对调。解决：`System.Drawing.Bitmap` 加载 PNG → 另存为 BMP（BGRA 原生布局）→ 引擎加载 BMP。生成 `.bmp.cache` 避免重复转换。

---

## 三、三部曲：注册 → 放图 → 引用

### 第一步：注册 Sprite（必须）

**文件**：`GUI/{任意前缀}SpriteData.xml`（**文件名必须以 `SpriteData.xml` 结尾**）

**格式规则（全子元素，无属性，无注释）**：

```xml
<?xml version="1.0" encoding="utf-8"?>
<SpriteData>
  <SpriteCategories>
    <SpriteCategory>
      <Name>my_sprites</Name>                  <!-- 类别名，对应 PNG 文件名前缀 -->
      <SpriteSheetCount>1</SpriteSheetCount>
      <AlwaysLoad>true</AlwaysLoad>
      <SpriteSheetSize ID="1" Width="256" Height="256" />  <!-- 纹理实际尺寸，必须准确 -->
    </SpriteCategory>
    <!-- 可加更多 SpriteCategory -->
  </SpriteCategories>

  <SpriteParts>
    <SpritePart>
      <Name>my_image_part</Name>               <!-- Part 名，随意取 -->
      <Width>256</Width>
      <Height>256</Height>
      <CategoryName>my_sprites</CategoryName>  <!-- 关联到上面的 Category -->
      <SheetID>1</SheetID>
      <SheetX>0</SheetX>
      <SheetY>0</SheetY>
    </SpritePart>
  </SpriteParts>

  <Sprites>
    <GenericSprite>                            <!-- ⚠️ 必须是 GenericSprite，不能是 Sprite -->
      <Name>my_image</Name>                    <!-- 最终引用名，Prefab 里用这个 -->
      <SpritePartName>my_image_part</SpritePartName>
    </GenericSprite>
  </Sprites>
</SpriteData>
```

**致命规则**：

| ❌ 禁止 | ✅ 正确 |
|--------|--------|
| `<!-- 注释 -->` | **零注释** |
| `<SpritePart Name="x">` (属性) | `<SpritePart><Name>x</Name>` (子元素) |
| `<Sprite Name="x">` | `<GenericSprite><Name>x</Name>` |
| 缺少 `<SpriteSheetSize>` | 必须加，填 PNG 实际宽高 |

### 第二步：放置 PNG

**文件**：`GUI/SpriteParts/{CategoryName}_{SheetID}.png`

例如 `CategoryName = "my_sprites"`，`SheetID = 1`：
→ `GUI/SpriteParts/my_sprites_1.png`

尺寸必须等于 `<SpriteSheetSize>` 的 `Width × Height`。

### 第三步：Prefab 引用

```xml
<Widget Sprite="my_image" Color="#FFFFFFFF" />
```

直接用 `Widget` + `Sprite` 属性，不需要 Brush。

---

## 四、Harmony 补丁

### `Patch_TextureLoadFallback.cs`

核心逻辑：
1. `.tpac` 命中 → 放行（不干预原版资源）
2. `.tpac` 未命中 → 搜所有模块的 `GUI/SpriteParts/{name}.png`（含子目录）→ PNG→BMP 转换 → `Texture.CreateTextureFromPath` 加载 → 缓存

### `.csproj` 加引用

```xml
<Reference Include="System.Drawing" />
```

加在 `<Reference Include="System" />` 后面。

### 加载触发

**`AlwaysLoad="true"` 的 SpriteCategory 会在引擎初始化 UI 时自动加载纹理**——无需手动干预。运行时日志已验证：`test_sprite` category 在进入 Mission 前就已 `IsLoaded=True`，Harmony 补丁在 `SpriteCategory.Load()` → `LoadTexture()` 调用链中自动拦截并加载 PNG。

如果遇到 category 未自动加载的情况（如 `AlwaysLoad="false"` 或加载时机问题），可以手动触发作为兜底：

```csharp
var cat = UIResourceManager.GetSpriteCategory("my_sprites");
if (cat != null && !cat.IsLoaded)
    cat.Load(UIResourceManager.ResourceContext, UIResourceManager.ResourceDepot);
```

**最佳实践**：SpriteData XML 里设 `AlwaysLoad="true"`，MissionView 里不加手动 Load 调用。运行时日志已证明这完全够用。

---

## 五、从 Prefab 到屏幕（最小示例）

### VM

```csharp
public class MyVM : ViewModel
{
    private bool _isVisible = true;
    [DataSourceProperty]
    public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
}
```

### Prefab XML

```xml
<Prefab>
  <Window>
    <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent" DoNotAcceptEvents="true">
      <Children>
        <Widget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed"
                SuggestedWidth="256" SuggestedHeight="256"
                HorizontalAlignment="Center" VerticalAlignment="Center"
                Sprite="my_image" Color="#FFFFFFFF"
                IsVisible="@IsVisible" />
      </Children>
    </Widget>
  </Window>
</Prefab>
```

### MissionView

```csharp
public class MyMissionView : MissionView
{
    private GauntletLayer _layer;
    private MyVM _vm;

    public override void OnMissionScreenInitialize()
    {
        base.OnMissionScreenInitialize();

        _vm = new MyVM();
        _layer = V.NewLayer(100);
        _layer.LoadMovie("MyPrefab", _vm);
        (ScreenManager.TopScreen as MissionScreen)?.AddLayer(_layer);
    }

    public override void OnMissionScreenFinalize()
    {
        base.OnMissionScreenFinalize();
        MissionScreen.RemoveLayer(_layer);
        _layer = null;
    }
}
```

### 注册

```csharp
// MySubModule.OnMissionBehaviorInitialize
mission.AddMissionBehavior(new MyMissionView());
```

---

## 六、扩展：在已有的 BubbleSay 管线上叠加 Sprite

如果要**复用现有的 View/VM**（比如 BubbleSayVM + BubbleSayMissionView 的每帧 WorldPointToScreenPoint 跟踪），只需：

1. `BubbleSayVM` 加 `SpriteName` 属性 → 绑定到 Widget
2. BubbleSay 的 Prefab XML 里加 `<Widget Sprite="@SpriteName" />` 堆叠在血条旁边
3. `MissionView.OnMissionScreenInitialize` 里加载 SpriteCategory
4. `MissionView.OnMissionTick` 里更新 `PosX/PosY`（和现有血条一样）

不需要新建 MissionView。

---

## 七、踩过的坑（❌ 避免 → ✅ 正确）

| # | ❌ 错误做法 | ✅ 正确做法 | 后果 |
|---|-----------|-----------|------|
| 1 | XML 里写 `<!-- 注释 -->` | **零注释** | 解析器遍历子节点时遇到注释，`item["Name"]` 返回 null → NRE → 整文件丢弃 |
| 2 | `<SpritePart Name="x">`（属性） | `<SpritePart><Name>x</Name>`（子元素） | 解析器用 `item["Name"]` 读子元素，不是属性 → 全部 SpritePart 丢失 |
| 3 | `<Sprite>` | `<GenericSprite>` | 解析器硬编码只处理 `GenericSprite` 和 `NineRegionSprite` |
| 4 | 不加 `<SpriteSheetSize>` | 必须加，填 PNG 实际宽高 | `UpdateInitValues()` 用 (0,0) 算 UV → 渲染出半透明纯色垃圾 |
| 5 | 直接让引擎加载 PNG | PNG→`System.Drawing.Bitmap`→另存为 BMP→引擎加载 BMP | PNG 是 RGBA，引擎当 BGRA 读 → 红蓝对调 → 整体偏蓝 |
| 6 | 不处理 `Texture` 命名冲突 | `using NativeTex = TaleWorlds.Engine.Texture;` | `TaleWorlds.Engine.Texture` vs `TaleWorlds.TwoDimension.Texture` 同名冲突，编译失败 |
| 7 | 不处理 `Path` 命名冲突 | 显式用 `System.IO.Path` | `System.IO.Path` vs `TaleWorlds.Engine.Path` 同名冲突 |
| 8 | 使用 `[^1]` 索引 | `parts[parts.Length - 1]` | .NET Framework 4.7.2 没有 `System.Index` 类型 |
| 9 | 普通 `[HarmonyPatch(typeof(...))]` | `TargetMethod()` + `GetInterfaceMap().TargetMethods` | `LoadTexture` 是显式接口实现，常规 Patch 找不到方法 |
| 10 | 等引擎自动加载纹理 | **手动调 `cat.Load(ctx, depot)`** | `RefreshSpriteData()` 首次只解析 XML，不调 `SpriteCategory.Load()` |

## 八、新增文件清单

| 文件 | 必须 | 作用 |
|------|------|------|
| `Core/Patch_TextureLoadFallback.cs` | ✅ | Harmony 补丁 |
| `GUI/{Mod}SpriteData.xml` | ✅ | 注册 SpriteCategory/Sprite |
| `GUI/SpriteParts/{Category}_{N}.png` | ✅ | 原始贴图 |
| `GUI/Prefabs/{Your}.xml` | 按需 | GauntletUI 布局 |
| `Debug/{Your}VM.cs` | 按需 | ViewModel |
| `Debug/{Your}MissionView.cs` | 按需 | 或复用已有 View |
| `.csproj` | ✅ | 加 `System.Drawing` 引用 |
