# 骑砍2霸主MOD开发(1)-RGL配置&RGL控制台&RGL日志

> 来源: https://blog.csdn.net/qq_35829452/article/details/135687636
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.RGL配置

     C:\Users\Administrator\Documents\Mount and Blade II Bannerlord\Configs\engine_config.txt

     engine_config.txt对应配置项值通过TaleWorlds.Engine.IConfig获取

#获取cheat_mode对应配置项
[EngineMethod("get_cheat_mode", false)]
bool GetCheatMode();

#获取窗口分辨率
[EngineMethod("get_desktop_resolution", false)]
void GetDesktopResolution(ref int width, ref int height);

     <1.作弊

          cheat_mode = 0 → cheat_mode = 1 开启作弊模式,大地图传送,秒杀

作弊按键:
   Ctrl Left Click—传送地图的任意点。

   Ctrl H—主角满血。

   CTRL Shift H—主角全是恭维话。

   Ctrl F4 -在战场上使敌人昏迷。

   Ctrl Shift F4—使战场上所有敌人昏迷。

     <2.分辨率(修改全屏或窗口显示模式)

          display_width = 2560

          display_height = 1440

二.RGL控制台

    <1.呼出控制台

         方法一:进入游戏后Alt+~组合键呼出RGL Command控制台

         方法二:使用MBDebug主动呼出控制台

MBDebug.EchoCommandWindow()



     <2.控制台指令回调事件

[LibraryCallback]
internal static string CallCommandlineFunction(string functionName, string arguments)
{
	bool flag;
	return CommandLineFunctionality.CallFunction(functionName, arguments, out flag);
}

     <3.控制台常用指令

crafting.unlock_all_parts 锻造内的全部零件解锁。

campaign.add_gold_to_hero 数字获得指定数量的第纳尔。

campaign.add_renown_to_clan 获得数字指定数量的声望。

campaign.add_influence 数字获得指定数量的影响力。

campaign.changehero_relation100all 获得all好感度100all所有NPC都可以指定其中一个名字。

campaign.add_companion 可以随机获得1名伙伴NPC再次使用。

三.RGL日志

    C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log.txt

    输出各种不同级别的日志至rgl_log.txt中:

#输出常规日志
Debug.Print()

#输出警告日志
Debug.PrintWarning()

#输出错误日志
Debug.PrintError()

四.RGL维测

    RGL进程异常捕获,消息断言,消息弹框等功能

MBDebug.ShowWarning()

MBDebug.ShowMessageBox()

MBDebug.Assert()

MBDebug.FailAssert()

MBDebug.AssertMemoryUsage()

五.RGL进程管理

    获取RGL进程ID,杀死&启动进程,设备&内存信息

Utilities.GetMemoryUsageOfCategory()

Utilities.QuitGame()

Utilities.ExitProcess()

六.RGLMOD管理

    获取进程所在文件夹目录,挂载MOD文件夹目录

Utilities.GetFullModulePath()

七.RGL启动&接口映射

     通过C#-DllImport将RGL中的接口映射为C#接口,完成RGL的拉起和C#运行时环境的初始化.

static class MBDotNet
{
	[SuppressUnmanagedCodeSecurity]
    [DllImport("TaleWorlds.Native.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "WotsMainSDLL")]
	public static extern int WotsMainDotNet(string args);
}


 

                
