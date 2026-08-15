
1、如果发现某个按钮没有响应鼠标事件，那就看看上下游节点有没有 设置 DoNotAcceptEvents="true" 覆盖了ButtonWidget的事件响应

2、ListPanel / StackLayout 的 VerticalTopToBottom 与 VerticalBottomToTop 子元素排列方向

【结论】两个枚举值都会导致 XML 子元素顺序与屏幕视觉顺序不一致，根源在于引擎从容器边界开始向另一端迭代放置子元素，枚举名只是标定从哪端开始。

【v1.2.12 bug】：VerticalBottomToTop 和 VerticalTopToBottom 实现互换了——BottomToTop 实际从上到下堆，TopToBottom 实际从下到上堆。v1.3.0+ 修复。

【双版本兼容方案】：v1.3.0+ 上 Harmony patch `StackLayout.OnLayout` 对自定义 UI 的 ListPanel 做反向 swap，使行为与 v1.2.12 一致。需要 swap 的 ListPanel 在 XML 中加 `Id="LWN_xxx"`（前缀匹配）。详见 `plans/rules/wheels.md`「双版本 XML 布局兼容」。

【踩坑：Id 不能标在 `<Window>` 上，必须标在 `<ListPanel>` 自身】：`<Window>` 是 GauntletUI 的 CustomWidgetType（从单独 XML 加载），内部结构导致 ParentWidget 链不通。且 GauntletUI XML 不把 `Tag` 属性映射到 `Widget.Tag`。正确做法：`Id="LWN_xxx"` 直接写在目标 `<ListPanel>` 上，patch 里 `widget.Id.StartsWith("LWN")` 直接命中。已验证。

【反编译源码】TaleWorlds.GauntletUI.dll → StackLayout.LayoutLinearVertical()：

    private void LayoutLinearVertical(Widget widget, float left, float bottom, float right, float top)
    {
        float num = 0f;               // top 游标
        float num2 = bottom - top;    // bottom 游标（初始 = 容器高度）

        for (int j = 0; j < widget.ChildCount; j++)
        {
            float h = child.MeasuredSize.Y + margin;

            if (LayoutMethod == VerticalBottomToTop)
                num2 = num + h;       // bottom = top + h → 从顶(0)开始往下堆
            else  // VerticalTopToBottom
                num = num2 - h;       // top = bottom - h → 从底(容器高)开始往上堆

            child.Layout(left, num2, right, num);
            //            ↑left ↑bottom ↑right ↑top   (top < bottom，Y轴向下增长)

            if (VerticalBottomToTop) num = num2;   // 游标下移
            else                    num2 = num;    // 游标上移
        }
    }

【两种枚举的屏幕行为】

  VerticalBottomToTop:
    第0个child → top=0 → 屏幕顶部
    第1个child → 上面那个下面
    ...
    最后一个child → 屏幕底部
    屏幕方向：从上到下（直觉方向），但枚举名叫"BottomToTop" ← 名字误导

  VerticalTopToBottom:
    第0个child → bottom=容器高度 → 屏幕底部
    第1个child → 上面那个上面
    ...
    最后一个child → 屏幕顶部
    屏幕方向：从下到上（反直觉），名和实正好跟 VerticalBottomToTop 对调

【枚举名为什么反直觉】
Gauntlet UI 使用的是标准屏幕坐标系（top < bottom，Y向下增长，见 Widget.SetLayout 源码）。
但枚举名遵循的是"从哪个数值端开始"的逻辑：
  - BottomToTop：从 Y=0（数值上"底"）迭代到 Y=容器高（数值上"顶"）→ 屏幕上就是从上到下
  - TopToBottom：从 Y=容器高（数值上"顶"）迭代到 Y=0（数值上"底"）→ 屏幕上就是从下到上
名字里的 Top/Bottom 对应的是 Y 数值大小，不是屏幕上的视觉上下。

【实操建议】
统一用 VerticalBottomToTop，把 XML 的 <Children> 子元素按屏幕从上到下的直觉顺序书写：
  - 想在屏幕顶部的 → 写第一个 child
  - 想在屏幕底部的 → 写最后一个 child
这样不用每次都"反着来"想一遍。子 ListPanel 同理。



  <!-- 动画眼睛 -->
                                <Widget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed"
                                    SuggestedWidth = "50" SuggestedHeight="50"
                                        HorizontalAlignment="Center"
                                        Sprite="BlankWhiteSquare_9" Color="#8B0000FF" />


报错
System.ArgumentOutOfRangeException
  HResult=0x80131502
  Message=Index and length must refer to a location within the string.
Parameter name: length
  Source=mscorlib
  StackTrace:
   在 System.String.Substring(Int32 startIndex, Int32 length)
   在 TaleWorlds.Library.Color.ConvertStringToColor(String color)
   在 TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate.SetAttributes(WidgetCreationData widgetCreationData, WidgetInstantiationResult widgetInstantiationResult, Dictionary`2 parameters)
   在 TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate.SetAttributes(WidgetCreationData widgetCreationData, WidgetInstantiationResult widgetInstantiationResult, Dictionary`2 parameters)
   在 TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate.SetAttributes(WidgetCreationData widgetCreationData, WidgetInstantiationResult widgetInstantiationResult, Dictionary`2 parameters)
   在 TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate.SetAttributes(WidgetCreationData widgetCreationData, WidgetInstantiationResult widgetInstantiationResult, Dictionary`2 parameters)
   在 TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate.Instantiate(WidgetCreationData widgetCreationData, Dictionary`2 parameters)
   在 TaleWorlds.GauntletUI.Data.GauntletView.AddItemToList(Int32 index)
   在 TaleWorlds.GauntletUI.Data.GauntletView.OnViewModelBindingListChanged(Object sender, ListChangedEventArgs e)
   在 TaleWorlds.Library.MBBindingList`1.OnListChanged(ListChangedEventArgs e)
   在 TaleWorlds.Library.MBBindingList`1.InsertItem(Int32 index, T item)
   在 LivingWorldNpcs.BubbleSayNeaybyVM.AddBubble(BubbleSayVM bubble) 在 H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\Bubble\BubbleSayNeaybyVM.cs 中: 第 39 行
   在 LivingWorldNpcs.BubbleSayMissionView.AddHealthBar(Agent agent) 在 H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\Bubble\BubbleSayMissionView.cs 中: 第 93 行
   在 LivingWorldNpcs.BubbleSayMissionView.ScanForNewAgents() 在 H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\Bubble\BubbleSayMissionView.cs 中: 第 66 行
   在 LivingWorldNpcs.BubbleSayMissionView.OnMissionTick(Single dt) 在 H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\ExampleModVS\ExampleMod\ExampleMod\Bubble\BubbleSayMissionView.cs 中: 第 108 行
   在 TaleWorlds.MountAndBlade.Mission.OnTick(Single dt, Single realDt, Boolean updateCamera, Boolean doAsyncAITick)
   在 TaleWorlds.MountAndBlade.MissionState.TickMission(Single realDt)
   在 TaleWorlds.MountAndBlade.MissionState.OnTick(Single realDt)
   在 TaleWorlds.Core.GameStateManager.OnTick(Single dt)
   在 TaleWorlds.Core.Game.OnTick(Single dt)
   在 TaleWorlds.Core.GameManagerBase.OnTick(Single dt)
   在 TaleWorlds.MountAndBlade.Module.OnApplicationTick(Single dt)
   在 TaleWorlds.DotNet.Managed.ApplicationTick(Single dt)

3、原版 Options 下拉控件（AnimatedDropdownWidget / Standard.DropdownWithHorizontalControl）在自定义 UI 中的移植用法（2026-08-15 实机验证）

【背景】原版选项面板（SPOptions）的多选设置项（如「Control Block Direction 格挡方向」）用的是 `Standard.DropdownWithHorizontalControl` 预制体（`Native/GUI/Prefabs/Standard/Standard.DropdownWithHorizontalControl.xml`），核心控件 = `AnimatedDropdownWidget`。想让自定义 UI 的下拉长得和原版选项一模一样，**不要自己拼笔刷**，直接移植这个控件的结构（本项目缩略模式频道下拉就是这么做的，见 `GUI/Prefabs/ImChatCompact.xml` + `ImChatVM.ImChannelSelectorVM`）。

【数据源 = SelectorVM 形态】控件靠双向绑定驱动，VM 必须长这样（本项目 ImChannelSelectorVM）：

```csharp
public class ImChannelSelectorVM : ViewModel
{
    public MBBindingList<ImChannelOptionVM> ItemList { get; } = new MBBindingList<ImChannelOptionVM>();  // 项数据
    private int _selectedIndex = -1;
    [DataSourceProperty] public int SelectedIndex { get/set }   // setter 里做「选中切换」回调
    public void ExecuteSelectPreviousItem() { ... }   // 左箭头命令
    public void ExecuteSelectNextItem() { ... }       // 右箭头命令
}
// 项数据（SelectorItemVM 形态）：
public class ImChannelOptionVM : ViewModel
{
    [DataSourceProperty] public string StringItem { get/set }   // 显示文本（可拼未读数）
    [DataSourceProperty] public bool CanBeSelected => true;     // 项模板 IsEnabled/@CanBeSelected
    [DataSourceProperty] public object Hint => null;            // 项模板 HintWidget（无提示 = null）
}
```

【XML 关键结构】（原版 OptionItem.xml 里是 `<Standard.DropdownWithHorizontalControl Parameter.SelectorDataSource="{Selector}" />`；要自定义尺寸/方向就手抄结构）：

```xml
<!-- 🔴 DataSource 必须包一层（原版 HorizontalControlParent 同款）：CurrentSelectedIndex 与
     箭头 Command.Click 都解析到 SelectorVM 上下文，不包就解析到主 VM 上找不到属性 -->
<ListPanel DataSource="{ChannelSelector}" StackLayout.LayoutMethod="HorizontalLeftToRight" ...>
  <Children>
    <ButtonWidget Brush="SPOptions.Dropdown.Left.Button" Command.Click="ExecuteSelectPreviousItem" .../>
    <AnimatedDropdownWidget Id="..." WidthSizePolicy="Fixed" HeightSizePolicy="CoverChildren" SuggestedWidth="220"
                            DropdownContainerWidget="DropdownClipWidget\DropdownContainerWidget"
                            ListPanel="DropdownClipWidget\DropdownContainerWidget\ScrollablePanel\ClipRect\LWN_...SelectorList"
                            Button="DropdownButtonContainer\DropdownButton"
                            CurrentSelectedIndex="@SelectedIndex"
                            TextWidget="DropdownButtonContainer\DropdownButton\SelectedTextWidget"
                            DropdownClipWidget="DropdownClipWidget"
                            ScrollbarWidget="DropdownClipWidget\DropdownContainerWidget\VerticalScrollbar">
      <Children>
        <Widget Id="DropdownButtonContainer" ...>   <!-- 中心按钮（SPOptions.Dropdown.Center 笔刷 + SelectedTextWidget） -->
        <Widget Id="DropdownClipWidget" WidthSizePolicy="CoverChildren" HeightSizePolicy="Fixed" ClipContents="true"
                WidgetToCopyHeightFrom="DropdownContainerWidget\ScrollablePanel">  <!-- 🔴 高度随内容自适应 -->
          <Children>
            <BrushWidget Id="DropdownContainerWidget" ... Brush="SPOptions.Dropdown.Extension">  <!-- 列表底 -->
              <Children>
                <ScrollablePanel Id="ScrollablePanel" ... ClipRect="ClipRect" InnerPanel="ClipRect\LWN_...SelectorList"
                                 VerticalScrollbar="..\VerticalScrollbar">
                  <Widget Id="ClipRect" ... MaxHeight="348">
                    <ListPanel Id="LWN_...SelectorList" DataSource="{ItemList}"
                               StackLayout.LayoutMethod="VerticalBottomToTop">   <!-- 🔴 见下方 swap 坑 -->
                      <ItemTemplate>
                        <ButtonWidget ButtonType="Radio" UpdateChildrenStates="true"
                                      Brush="Standard.DropdownItem.SoundBrush" IsEnabled="@CanBeSelected">
                          <Children>
                            <ImageWidget Brush="Standard.DropdownItem" .../>   <!-- 🔴 悬停高亮载体：按钮状态驱动 -->
                            <TextWidget Text="@StringItem" Brush="SPOptions.Dropdown.Item.Text" .../>
                          </Children>
                        </ButtonWidget>
                      </ItemTemplate>
                    </ListPanel>
                  </Widget>
                </ScrollablePanel>
                <ScrollbarWidget Id="VerticalScrollbar" .../>
              </Children>
            </BrushWidget>
          </Children>
        </Widget>
      </Children>
    </AnimatedDropdownWidget>
    <ButtonWidget Brush="SPOptions.Dropdown.Right.Button" Command.Click="ExecuteSelectNextItem" .../>
  </Children>
</ListPanel>
```

【引擎行为（反编译 DropdownWidget/AnimatedDropdownWidget 确认）】
- 列表默认 reparent 到 `EventManager.Root`（`DoNotHandleDropdownListPanel` 不设时），由引擎每帧绝对定位在按钮**正下方**（`UpdateListPanelPosition`）——所以列表天然盖住后续 UI、天然在最上层。
- 展开/收起：点中心按钮切换；选中项后自动收起；**点击控件看得到的区域外自动收起**（`LatestMouseUpWidget != Button` 逻辑）。
- 中心按钮文本 = `ItemList[SelectedIndex].StringItem` 自动跟随，无需手动刷。
- 高度自适应 = `DropdownClipWidget` 的 `WidgetToCopyHeightFrom="DropdownContainerWidget\ScrollablePanel"`（每帧复制 ScrollablePanel 高度）——项少时列表矮，不会留大片空白。

【🔴 三个移植坑】
1. **向下展开**：列表定位在按钮下方（绝对坐标）。底置面板（如贴底缩略面板）要把下拉放在面板**顶部行**，让列表向下展开盖住内容区；放底部行会越出屏幕。
2. **swap 补丁会把列表顺序颠倒**：自定义 prefab 里所有 widget 都有 LWN 祖先（`IsCustomUI` 沿父链匹配），原版列表声明 `VerticalTopToBottom` 会被 swap 成 BottomToTop → 第一项跑到最底下。**声明 `VerticalBottomToTop` + LWN 前缀**（swap 后 = 自上而下，与项目惯例一致）。
3. **绑定上下文**：`CurrentSelectedIndex="@SelectedIndex"` 与箭头 `Command.Click` 都解析在**下拉所在数据上下文**——必须包一层 `DataSource="{ChannelSelector}"` 容器（原版 HorizontalControlParent 就是这么干的），否则解析到主 VM 上属性/方法不存在。

【层盲区收起】非模态层（输入门控靠 hit-test，见 im.md/ui.md）里，点击场景的鼠标层看不到 → 原版自动收起不触发。处理：Tick 里轮询下拉控件 `IsOpen`（`widget is DropdownWidget dw && dw.IsOpen`），鼠标按下在（面板矩形 ∪ 下拉列表矩形）外 → `dw.IsOpen = false`。列表矩形 = reparent 后的 `LWN_...SelectorList` 的 GlobalPosition+Size。

【滚轮位】列表滚动需要层的 `InputUsageMask.MouseWheels` 位——自定义层若常态不带该位（缩略面板为了滚轮穿透场景），要在 Tick 里按下拉 `IsOpen` 动态补上。

【🔴 hover 闪烁坑】下拉项数据每次刷新**全量 Clear+重建** → 项 widget 被销毁重建 → hover 高亮每 0.3s 重置一次 = 底色闪烁。必须**增量刷新**：按唯一键（如 ConversationId）复用既有项 VM 实例，只更新 `StringItem`。消息列表同理。

【笔刷速查】按钮左/中/右 = `SPOptions.Dropdown.Left.Button` / `SPOptions.Dropdown.Center`（中心文本 `SPOptions.Dropdown.Center.Text`）/ `SPOptions.Dropdown.Right.Button`；列表底 = `SPOptions.Dropdown.Extension`；项 = `Standard.DropdownItem.SoundBrush`（空渲染，只发声）+ 内层 `ImageWidget Brush="Standard.DropdownItem"`（hover 高亮载体，Default 透明 / Hovered Alpha 0.2 / Selected 0.5）+ `SPOptions.Dropdown.Item.Text`。
