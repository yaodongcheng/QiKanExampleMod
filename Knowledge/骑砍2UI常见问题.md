
1、如果发现某个按钮没有响应鼠标事件，那就看看上下游节点有没有 设置 DoNotAcceptEvents="true" 覆盖了ButtonWidget的事件响应

2、ListPanel / StackLayout 的 VerticalTopToBottom 与 VerticalBottomToTop 子元素排列方向

【结论】两个枚举值都会导致 XML 子元素顺序与屏幕视觉顺序不一致，根源在于引擎从容器边界开始向另一端迭代放置子元素，枚举名只是标定从哪端开始。

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