# Custom Menu Background

<!-- 源: https://docs.bannerlordmodding.lt/guides/custom_menu_background/ | 抓取日期: 2026-09-09 -->

![](https://docs.bannerlordmodding.lt/pics/PeVfWv5.png)

## Custom image

Make custom image, Width x Height: 445x805

[Import as sprite](/gauntletui/sprites/)

## Set as menu background

```
args.MenuContext.SetBackgroundMeshName("YOUR_SPRITE_NAME_HERE");
```

## Other menus

* [Settlements Menu Image](/modding/settlements/#wait_mesh) (wait\_mesh in settlements.xml)
* [Encounters](/modding/cultures/#xml) (encounter\_background\_mesh in cultures.xml)

### Alley/Arena/Keep/Tavern/Port

![](https://docs.bannerlordmodding.lt/pics/2402080959.png)

These menu backgrounds are culture related.

They are saved in the /GUI/SpriteParts/ui\_fullbackgrounds

Names:

* CULTURE\_alley.png
* CULTURE\_arena.png
* CULTURE\_keep.png
* CULTURE\_tavern.png
* CULTURE\_port.png (with DLC)

When adding these for your custom culture, make sure you extract/include all the vanila PNGs as well. Otherwise you will be missing a lot of menu backgrounds.
