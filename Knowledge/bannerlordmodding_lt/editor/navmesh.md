# Navigation Mesh

<!-- 源: https://docs.bannerlordmodding.lt/editor/navmesh/ | 抓取日期: 2026-09-09 -->

## Guides

* [Official Guide - Navmesh](https://moddocs.bannerlord.com/editor/scene-editor/nav_mesh/)
* [Official Guide - Navmesh Inspector](https://moddocs.bannerlord.com/editor/scene-editor/nav_mesh_inspector/)
* [Scene Editor Tutorial #7 - Navmesh](https://www.youtube.com/watch?v=-pxQFSfaG1c&list=PLkLMOvLG8q6bVmWqjXSduwhmzsJ9bLO0u&index=8)
* [Scene Editor Tutorial #8 - Navmesh Episode 2](https://www.youtube.com/watch?v=mSIjcJJ0hx8&list=PLkLMOvLG8q6bVmWqjXSduwhmzsJ9bLO0u&index=8)
* [Scene Editor Tutorial #17 - Quick Navmeshing](https://www.youtube.com/watch?v=yBbgr1DGht0&list=PLkLMOvLG8q6bVmWqjXSduwhmzsJ9bLO0u&index=19)
* [Navigation Mesh with Modding Tool](https://www.youtube.com/watch?v=YHVgJQAyvZw&list=PLxhni8XI_dRALYs8R9NHpMLxpMI2wSGxA&index=8)

## Controls

![](https://docs.bannerlordmodding.lt/pics/FnZfJS5.png)

## Faces

![](https://docs.bannerlordmodding.lt/pics/2512291315.png)

## Problems/Solutions

### Can't select any navmesh element

Turn ON Helpers in the Visibility tab:

![](https://docs.bannerlordmodding.lt/pics/nZJiVRD.png)

### Edit mode does not change visuals

Changing Mesh Edit Mode does not change visuals of the Navmesh

Click on navmesh icon at the top menu several times

## Civilian vs Siege

Hart:

you need to have seperate navmesh in siege and in civilian  
ideally your navmesh should be set in 6 layers  
Civilian:  
\* level 1  
\* level 2  
\* level 3  
Siege  
\* level 1  
\* level 2  
\* level 3  
select all of your navmesh faces(faces, not verticies or whatever) then at the left menu look for visibility masks  
and set the entire thing to civilian 1+2+3  
then with all of the faces selected, copy them, paste them and re-align them to the same exact spots you had them before, now change civilian visibility off and siege on

Snorri:

it's recomended, but not needed, as you can make single navmesh faces dissaper in some lvls, it's enough in many scenes for civilian use, to just disble ladders, destroyed walls, and stuff like this

## Tips

![](https://docs.bannerlordmodding.lt/pics/2510300803.png)
