# Check for other mods

<!-- 源: https://docs.bannerlordmodding.lt/modules/check_for_other_mods/ | 抓取日期: 2026-09-09 -->

In SubModule.cs:

```
public override void OnGameInitializationFinished(Game game)    // executes after all mods are loaded
{
    base.OnGameInitializationFinished(game);
    try
    {
        string[] modulesNames = Utilities.GetModulesNames();
        for (int i = 0; i < modulesNames.Length; i++)
        {
            if (modulesNames[i] == "BannerKings")
            {
                // do something about it
            }
        }
    }
    catch (Exception ex)
    {
        // do something about it
    }
}
```
