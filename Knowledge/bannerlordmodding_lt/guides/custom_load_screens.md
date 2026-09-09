# Custom Load Screens

<!-- 源: https://docs.bannerlordmodding.lt/guides/custom_load_screens/ | 抓取日期: 2026-09-09 -->

![](https://docs.bannerlordmodding.lt/pics/EiS5Ygt.png)

* [Lykon Development - Bannerlord Modding for Beginners - Custom LoadScreens](https://www.youtube.com/watch?v=rE_fLYCgX-o)

Size: 1920 x 1080

Names: ui\_loading\_1, ui\_loading\_2 ... ui\_loading\_11, ui\_loading\_12

In the Editor import into YOUR\_MODULE/Assets/GauntletUI/

![](https://docs.bannerlordmodding.lt/pics/3D1g8RQ.png)

Set these flags and press Save:

![](https://docs.bannerlordmodding.lt/pics/HGFsx0I.png)

For 1.4

### Using the same load screens for warsails and base game - by anian

How loading screens work is:
base game loading screens are loaded => warsails overwrite the base game files. We can trick the game into thinking it already loaded the war sails loading screens, when it actually hasn't
meaning it will showcase base game load screens, the ones named: ui\_loading\_[x]

```
protected override void OnSubModuleLoad()
{
    if (IsWarSailsLoaded)
    {
        var modules = Module.CurrentModule.CollectSubModules();
        var navalGauntletModule = modules.FirstOrDefault(m => m is NavalDLCGauntletUISubModule);
        FieldInfo category = AccessTools.Field(typeof(NavalDLCGauntletUISubModule), "_initializedLoadingCategory");
        category.SetValue(navalGauntletModule, true);
    }
}
```

```
//where:
public static bool IsWarSailsLoaded => ModuleHelper.IsModuleActive("NavalDLC");
```

put it somewhere global.

How to change the default image on the game start?

This shows on game start when no mod is loaded yet.
Overwrite native tpac files?
