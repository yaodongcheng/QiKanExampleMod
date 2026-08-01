
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
