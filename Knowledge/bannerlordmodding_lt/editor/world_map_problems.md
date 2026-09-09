# World Map Problems

<!-- 源: https://docs.bannerlordmodding.lt/editor/world_map_problems/ | 抓取日期: 2026-09-09 -->

## Red Terrain

![](https://docs.bannerlordmodding.lt/pics/2509110820.png)

Fatrod

* Changing settings on individual layers leads to broken textures
* Could be from changing Area Map texture, transparency settings, lighting, etc...
* ![⭐](https://cdnjs.cloudflare.com/ajax/libs/twemoji/14.0.2/svg/2b50.svg ":star:") Fix is to delete shaders

Snorri

* Usually you have those bugs when you try to add new layers of terrain paint when map is already a heavy file
* Painting should not be a problem, but adding new layers late in work on the map sure do
* I advice to always ad 12-14 layers at start on work on scene, paint IT on corner. They dont even need to be anything specific, could be modyfied durring the work. Tho IT will be less bugy to change and use them latter then adding new ones.
