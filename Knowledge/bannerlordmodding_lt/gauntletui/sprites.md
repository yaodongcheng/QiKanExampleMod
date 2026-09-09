# Sprites

<!-- 源: https://docs.bannerlordmodding.lt/gauntletui/sprites/ | 抓取日期: 2026-09-09 -->

## Downloads

* [All native sprites extracted with BannerEdge](https://drive.google.com/drive/folders/1gfE8ERq6hzKGy6Ya_RbpaUPVy3DXsP6u)

## Tutorials

* [Generating and Loading UI Sprite Sheets](https://moddocs.bannerlord.com/asset-management/generating_and_loading_ui_sprite_sheets/)
* [Lesser Scholar - Ep 11: Loading UI Sprites. GauntletUI Layouting](https://www.youtube.com/watch?v=TGJe6Ia7_0g&list=PLzebdAxJeltRwfJ8jzsNolgHkRvLjoCRC&index=12)
* [Carolina Warlord Gaming - Adding Custom Sprites](https://www.youtube.com/watch?v=a-UAdVPUimE&list=PLjnD9iTZKI9yWnn10FcHImWeYbqbqLIbg&index=4)

## Import

Put your sprites (PNG files) into folder:

```
Modules\YOUR_MODULE_NAME\GUI\SpriteParts\ui_{YOUR_CATEGORY_NAME}
```

In file:

```
Modules\YOUR_MODULE_NAME\GUI\Config.xml
```

add

```
<Config>
    <SpriteCategory Name="ui_{YOUR_CATEGORY_NAME}">
        <AlwaysLoad/>
    </SpriteCategory>
</Config>
```

to always load your sprite category. [Read here](https://moddocs.bannerlord.com/asset-management/generating_and_loading_ui_sprite_sheets/) if you want to manage loading manually.

Run:

```
INSTALLATION_PATH\Mount & Blade II Bannerlord\bin\Win64_Shipping_wEditor\TaleWorlds.TwoDimension.SpriteSheetGenerator.exe
```

Do not forget to press ENTER to finish the script and unlock the PNG, otherwise it will not be accessible.

Run Mount & Blade II: Bannerlord - Modding Kit from Steam.  
Make sure your module is selected in the Mods section of the Launcher, then hit “Play”.  
Alt + `

```
resource.show_resource_browser
```

Collapse the folder named Native on the left of the resource browser to see your module easily. Then, select your module.

Open the GauntletUI folder in your module.

![](https://docs.bannerlordmodding.lt/pics/G3Xd5ts.png)

press the “Scan new asset files” button which is pointed with a red arrow below

![](https://docs.bannerlordmodding.lt/pics/rFDTvn5.png)

If nothing happens

1. Make sure you ran TaleWorlds.TwoDimension.SpriteSheetGenerator.exe

   SpriteSheetGenerator.exe will create two folders named Assets and AssetSources under Modules\YOUR\_MODULE\_NAME.  
   It will also create a SpriteData.xml file (with a prefix of your module name) under Modules\YOUR\_MODULE\_NAME\GUI.

   Check these folders/files to make sure they were generated properly.
2. If you are adding new sprites for the existing category or updating the old ones - make sure you did proper reimport as described below.

![](https://docs.bannerlordmodding.lt/pics/PwZr8OQ.png)

Make sure your files are selected then press the Import button. Then, you should see something similar to this:

![](https://docs.bannerlordmodding.lt/pics/T5QH7qk.png)

Close the resource browser and the Editor(game). You should now see a new file named ui\_{YOUR\_CATEGORY\_NAME}\_1\_tex.tpac under Modules\YOUR\_MODULE\_NAME\Assets\GauntletUI.

## Reimport or add more sprites for the same category

* Put new/updated PNG into GUI\SpriteParts\ui\_{YOUR\_CATEGORY\_NAME}
* Run TaleWorlds.TwoDimension.SpriteSheetGenerator.exe
* In resource.show\_resource\_browser press RMB on the existing category and select Reimport:

![](https://docs.bannerlordmodding.lt/pics/b3kmMwv.png)

## Overwrite native sprites

Let's say we want to change the crossbow skill icon to a musket.

The files we need are in the `\ui_group1\SPGeneral\Skills` (find them with [BannerEdge](https://github.com/hunharibo/BannerEdge))

![](https://docs.bannerlordmodding.lt/pics/2504290832.png)

Create your custom SpriteCategory, add \SPGeneral\Skills folder in it with only those modified files and you will overwrite only those files/sprites.

If you create SpriteCategory with the name `ui_group1` - you will overwrite whole category and many sprites will be empty (if you will not replace them)

## Sprite Widget Options

`HorizontalFlip="true"` - flips sprite horizontally

`VerticalFlip="true"` - flips sprite vertically

## Count sprites in the category

```
SpriteCategory sploadingCategory;
if (!UIResourceManager.SpriteData.SpriteCategories.TryGetValue("ui_loading_custom", out sploadingCategory))
{
    // log error ($"Can't find SpriteCategory 'ui_loading_custom'");
    return;
}
int totalGenericImageCount = sploadingCategory.SpriteParts.Count;
```

## NineRegionSprites

![](https://docs.bannerlordmodding.lt/pics/Nine_Slice_1_Guides.png)

Quite an interesting way to have strething sprites. Top/bottom/left/right margins can be fixed and what's between - stretched.

Good explanation [here](https://manual.gamemaker.io/monthly/en/The_Asset_Editors/Sprite_Properties/Nine_Slices.htm).

Defined in the sprite file as:

```
<NineRegionSprite>
    <Name>troop_tree_side_9</Name>
    <SpritePartName>Encyclopedia\troop_tree_side</SpritePartName>
    <LeftWidth>8</LeftWidth>
    <RightWidth>8</RightWidth>
    <TopHeight>0</TopHeight>
    <BottomHeight>0</BottomHeight>
</NineRegionSprite>
```

## Possible problems

### Sprite not visible in the game

Make sure there is

```
<AlwaysLoad />
```

in the MODULE\_NAMESpriteData.xml under your category.

If it's not, make sure you configured it into Config.xml and category name starts with ui\_

Config.xml should be in the SpriteParts folder.

### Sprite resized in a weird way

Possible reason: you added a new sprite, executed TaleWorlds.TwoDimension.SpriteSheetGenerator.exe but failed to Reimport with the Resource Browser.

### Missing texture

![](https://docs.bannerlordmodding.lt/pics/eiAJKer.png)

Make sure you [imported](https://moddocs.bannerlord.com/asset-management/generating_and_loading_ui_sprite_sheets/#importing-created-sprite-sheets) your sprite into the Resource Browser and tpac file was generated in the Assets or AssetPackages (after Mod publishing) folder.
