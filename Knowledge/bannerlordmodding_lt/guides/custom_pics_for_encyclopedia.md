# Custom pictures for Encyclopedia

<!-- 源: https://docs.bannerlordmodding.lt/guides/custom_pics_for_encyclopedia/ | 抓取日期: 2026-09-09 -->

![](https://docs.bannerlordmodding.lt/pics/encyclopedia_pics.gif)

Encyclopedia pictures are in the ui\_encyclopedia\_1.png. Use TpacTool/BannerEdge to extract them.

Or just download from this [link](https://drive.google.com/file/d/1VRVqGKRkz4OoT-hkDti_UCTAkLP57fLt/view?usp=drive_link).

Extract the archive to your MOD\_FOLDER/GUI/ folder

Change the appropriate files in MOD\_FOLDER/GUI/SpriteParts/ui\_encyclopedia/

[Import the sprites](/gauntletui/sprites/#import) in the Editor.

This method overwrites several sprites and this happens (lines and frames are gone):

![](https://docs.bannerlordmodding.lt/pics/2403072105.png)

To fix it in YOURMOD/GUI/YOURMORSpriteData.xml at the end of <Sprites> add:

```
<NineRegionSprite>
    <Name>troop_tree_side_9</Name>
    <SpritePartName>Encyclopedia\troop_tree_side</SpritePartName>
    <LeftWidth>8</LeftWidth>
    <RightWidth>8</RightWidth>
    <TopHeight>0</TopHeight>
    <BottomHeight>0</BottomHeight>
</NineRegionSprite>
<NineRegionSprite>
    <Name>subpage_slick_frame_9</Name>
    <SpritePartName>Encyclopedia\subpage_slick_frame</SpritePartName>
    <LeftWidth>14</LeftWidth>
    <RightWidth>15</RightWidth>
    <TopHeight>15</TopHeight>
    <BottomHeight>15</BottomHeight>
</NineRegionSprite>
```

This change is overwriten next time you will generate new sprites, so it's annoying, but I don't know better way currently to resolve this problem.
