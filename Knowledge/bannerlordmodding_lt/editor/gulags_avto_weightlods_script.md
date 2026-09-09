# GulagEnabler' s Avto-WeightLODs.py script

<!-- 源: https://docs.bannerlordmodding.lt/editor/gulags_avto_weightlods_script/ | 抓取日期: 2026-09-09 -->

Made by [GulagEnabler🍉](https://discord.com/channels/411286129317249035/697071240015380500/1216758524055519242)

## Functions

* Simplifies [Weight Transfer](/editor/weight_painting/#weight-transfer)
* Deletes empty Vertex Groups
* Auto generates LODs
* Adds Hand-Morphs

## Download

* [Version without Hand-Morphs](https://drive.google.com/file/d/1AVcaXiRo6SWVr3eP9EON7cwePCXRfeAK/view?usp=drive_link)
* [Version with Hand-Morps](https://drive.google.com/file/d/1H9DslcyKdt_Ba6sPaKDm0CdhzUMS0n6_/view?usp=drive_link)

## Install

Install into Blender:

![](https://docs.bannerlordmodding.lt/pics/2403202131.jpg)

![](https://docs.bannerlordmodding.lt/pics/2403202132.jpg)

At the bottom should show:

![](https://docs.bannerlordmodding.lt/pics/2403202134.jpg)

Enable the addon:

![](https://docs.bannerlordmodding.lt/pics/2403202136.jpg)

Press N and click on Tool to see the addon window:

![](https://docs.bannerlordmodding.lt/pics/2403202137.jpg)

## Weight Transfer

Select source mesh (with LMB) - from where to transfer weights

Select destination (target) mesh (with ctrl+LMB) - where to transfer the weights

Press "Weight Paint":

![](https://docs.bannerlordmodding.lt/pics/2403202140.jpg)

Select (LMB) only the target mesh.

This DataTransfer window should appear for the target mesh:

![](https://docs.bannerlordmodding.lt/pics/2403202141.jpg)

If it does not appear, delete all Vertex Groups from the target mesh and repeat the previous step (click on Weight Paint):

![](https://docs.bannerlordmodding.lt/pics/2403202143.jpg)

Press on Generate Data Layers:

![](https://docs.bannerlordmodding.lt/pics/2403202144.jpg)

Apply the modifier:

![](https://docs.bannerlordmodding.lt/pics/2403202145.jpg)

(For target mesh) check if Vertex Groups are created:

![](https://docs.bannerlordmodding.lt/pics/2403202146.jpg)

and are they weight painted:

![](https://docs.bannerlordmodding.lt/pics/2403202146b.jpg)

Delete non-active groups, that do not impact the mesh (all blue):

![](https://docs.bannerlordmodding.lt/pics/2403202146c.jpg)

### Demo

![](https://docs.bannerlordmodding.lt/pics/2403202147.gif)

![](https://docs.bannerlordmodding.lt/pics/2403202152.gif)

## Delete Empty Vertex Groups

Press on "Delete Empty Vertex Groups" to remove empty groups.

![](https://docs.bannerlordmodding.lt/pics/delete_empty_vertex_groups.gif)

## LODs

Select your mesh. Make sure it has correct name (which you will not be able to change in the BL Editor)

Select Object Mode

![](https://docs.bannerlordmodding.lt/pics/2405220909.png)

Newly generated LOD starts from 0.8 Decimate Ratio, then 0.4, 0.2, 0.1, 0.05 ...

Set how many LODs to generate and press the "Generate LODs" button:

![](https://docs.bannerlordmodding.lt/pics/2403202149.jpg)

### Demo

![](https://docs.bannerlordmodding.lt/pics/2403202153.gif)

## Hand Morphs

![](https://docs.bannerlordmodding.lt/pics/hand_morphs_script.gif)

Prerequisites: Need rigged hand model :handMRef\_Rigged . Need your new hand, Your mesh must be weight painted already.

1. Import the Rigged hand and Your weighted mesh, duplicate your weight painted mesh, , remove the armature and any vertex groups manually,select imported handMRef\_Rigged and your duplicated mesh, and Press the weight paint button, select duplicated mesh, generate data layers and apply modifier.
2. You must cycle through the animation frames to see if the weight paint looks okay, if not then fix it , once it looks good to you then can hide the imported handMRef\_Rigged
3. With duplicated model selected Click button: Generate Morph Targets , it generates 25 morphs
4. Select all 25 morphs, and your original weight painted mesh (26 items selected in total)
5. Click button: Assign Morph Targets, Done.

Youtube guide:
