# Include other modules when developing

<!-- 源: https://docs.bannerlordmodding.lt/modules/include_other_mods/ | 抓取日期: 2026-09-09 -->

In the file YOUR\_MODULE/Properties/launchSettings.json add the necessary modules.

Example to include BannerKings with all necessary modules:

```
{
  "profiles": {
    "Bannerlord": {
      "commandName": "Executable",
      "executablePath": "$(GameFolder)\\bin\\Win64_Shipping_Client\\Bannerlord.exe",
      "commandLineArgs": "/singleplayer _MODULES_*Bannerlord.Harmony*Bannerlord.UIExtenderEx*Native*SandBoxCore*CustomBattle*Sandbox*StoryMode*RBM*OpenSourceArmory*OpenSourceSaddlery*OpenSourceWeaponry*BannerKings*$(ModuleName)*_MODULES_",
      "workingDirectory": "$(GameFolder)\\bin\\Win64_Shipping_Client"
    }
  }
}
```
