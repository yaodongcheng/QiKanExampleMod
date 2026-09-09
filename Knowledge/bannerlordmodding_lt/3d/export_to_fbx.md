# Export to FBX (from Blender)

<!-- 源: https://docs.bannerlordmodding.lt/3d/export_to_fbx/ | 抓取日期: 2026-09-09 -->

## Select all elements

If the mesh is rigged (armor/helmet/boots/gloves/shoulders) - then you need to select with human\_skeleton\_notused, like this:

![](https://docs.bannerlordmodding.lt/pics/2409281004.png)

For the shields select two bo\_.. elements and main mesh with lods:

![](https://docs.bannerlordmodding.lt/pics/2409281005.png)

## File - Export - FBX

![](https://docs.bannerlordmodding.lt/pics/2409281009.png)

## Select export settings

Check Limit to Selected Objects:

![](https://docs.bannerlordmodding.lt/pics/2409281011.png)

Uncheck Add Leaf Bones:

![](https://docs.bannerlordmodding.lt/pics/2409281012.png)

Leave other settings as they are.

## Export

![](https://docs.bannerlordmodding.lt/pics/2409281016.png)

## Create preset

Create preset for the future to avoid clicking those settings every time:

![](https://docs.bannerlordmodding.lt/pics/2409281017.png)

## Problems

### Add Leaf Bones enabled

You will get this in the [Model Viewer](/3d/model_viewer/):

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/add_leaf_bones.webm)

![](https://docs.bannerlordmodding.lt/pics/2409281039.png)

### Not visible in the Inventory

![](https://docs.bannerlordmodding.lt/pics/2411221718.png)

Reason #1: Wrong mesh in the item description:

![](https://docs.bannerlordmodding.lt/pics/2411221720.png)

Reason #2: Model is too big in the Blender:

![](https://docs.bannerlordmodding.lt/pics/2411221718b.png)

After reimport with proper size:

![](https://docs.bannerlordmodding.lt/pics/2411221718c.png)
