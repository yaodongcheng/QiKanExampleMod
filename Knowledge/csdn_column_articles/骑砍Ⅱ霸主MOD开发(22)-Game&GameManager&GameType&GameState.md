# 骑砍Ⅱ霸主MOD开发(22)-Game&GameManager&GameType&GameState

> 来源: https://blog.csdn.net/qq_35829452/article/details/142113318
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.第一阶段-Application初始化

    <1.进入到主界面前为RGL引擎应用进程初始化阶段,完成TPAC,Shader等核心文件和环境的初始化.

    <2.全局加载资源TPAC

    <3.全局加载资源Brush

    <4.全局加载资源Shader

    <5.全局加载核心配置文件mbproj文件

#完成核心资源骨骼动画的加载
Module.CreateProcessedActionSetsXMLForNative()

#完成核心资源音频系统的加载
Module.CreateProcessedSoundEventDataXMLForNative()

二.第二阶段-主菜单初始化

#主菜单页面为InitialScreen-InitialState
this.GlobalGameStateManager.CleanAndPushState(
    this.GlobalGameStateManager.CreateState<InitialState>(), 0);

三.第三阶段-选择游戏Game

     沙盒游戏,多人游戏,自定义战斗为不同的Game类型,有不同的数据和保存机制,数据持久化至Game中.

四.第四阶段-初始化游戏

    <1.GameStateMananger

         GameStateMananger负责Loading页面(LoadingState)的调度和监控,完成Loading后触发OnLoadingFinished实现游戏页面初始化.

    <2.GameType

         GameType负责不同游戏模式下初始化数据的保存,例如沙盒游戏需要初始化王国,文化等基础数据,自定义战斗不需要.

    <3.GameModel

         GameModel负责不同游戏下战斗系统,沙盒系统的差异性实现,例如沙盒模式和自定义战斗模式下武器的伤害计算规则不同.

五.第五阶段-屏幕切换GameState

     同一个游戏下可能需要切换至不同的屏幕,完成不同的数据初始化和加载,通过Push或Pop操作完成不同GameState的切换.

                
