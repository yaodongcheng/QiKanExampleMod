# Model Viewer

<!-- 源: https://docs.bannerlordmodding.lt/3d/model_viewer/ | 抓取日期: 2026-09-09 -->

* [OFFICIAL: Model Viewer](https://moddocs.bannerlord.com/editor/resource-editors/model_viewer/)

## Open

Editor > Window > Show Model Viewer:

![](https://docs.bannerlordmodding.lt/pics/2410042155.png)

From the left panel, you can change Atmosphere, hide/show ground, or add as many entities as you want. The entities can either be Human or simple Mesh. Pressing Add Entity will open a modal window for you to select the entity type:

## Add human

![](https://docs.bannerlordmodding.lt/pics/2410042157.png)

To avoid super slow zoom I enlarge my human x10. That allows to look around him much much faster:

![](https://docs.bannerlordmodding.lt/pics/2410042158.png)

Do not do this when working with cloth physics.   
For some reason when scale is not 1;1;1 - cloth physics are ruined.

Holding SHIFT while zooming helps also

## Add armors

Add armors to the `Body parts` section, not `Items`:

![](https://docs.bannerlordmodding.lt/pics/2410042201.png)

## Animation

For quick tests I am using:

* `lord_walk` - `lord_walk_forward` (good for normal movement testing)
* `run` - and the last from the list (easy to set) (good for extreme movement testing)
* `inventory` - `inventory_idle` (good for idle testing, for small details)

![](https://docs.bannerlordmodding.lt/pics/2410042204.png)

## Test horse caparison

Select Entity Model > Mesh:

![](https://docs.bannerlordmodding.lt/pics/2410061334.png)

Select caparison as `Mesh` and horse animations as shown:

![](https://docs.bannerlordmodding.lt/pics/2410061334b.png)

Add horse as a second entity:

![](https://docs.bannerlordmodding.lt/pics/2410061334c.png)

Set same animation as for caparison:

![](https://docs.bannerlordmodding.lt/pics/2410061334d.png)

Click on `Animation Control` to start/stop animations until caparison and the horse are in sync (maybe there is a better way to sync animations?)

## Good armors for testing

Several armors that are bulky and are very good for testing with shoulder armors/gloves/shoes:

![](https://docs.bannerlordmodding.lt/pics/2410061339.png)

```
tunic_ironplate_armor
vlandia_scale_armor
mail_hauberk
armor_hemp_tunic
```

## Problems

### Add Leaf Bones enabled

Floating helmet:

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/add_leaf_bones.webm)

Cause: Add Leaf Bones enabled in the Blender export settings: [FBX Export](/3d/export_to_fbx/)

![](https://docs.bannerlordmodding.lt/pics/2409281039.png)

### Missing/bad [Armature](/3d/armature_skeleton/)

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/fbx_armature_problem.webm)

Cause: missing [armature (skeleton)](/3d/armature_skeleton/) or wrong armature assigned:

![](https://docs.bannerlordmodding.lt/pics/2409281050.png)

Another case:

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/caparison_problem.webm)

The cause - export to FBX did not include the horse\_skeleton\_notused:

![](https://docs.bannerlordmodding.lt/pics/2601190811.png)

### Disabled skinning

Not moving helmet:

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/material_skinning_problem.webm)

Cause: Skinning disabled in material settings:

![](https://docs.bannerlordmodding.lt/pics/2409281102.png)

### Bad [Weight Painting](/3d/weight_painting/)

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/2410102105.webm)

Redo the [Weight Painting](/3d/weight_painting/). Fastest way - do [Weight Transfer](/3d/weight_painting/#weight-transfer).

### Model too big/small

![](https://docs.bannerlordmodding.lt/pics/2601280813.png)

You [imported](/3d/editor_fbx_import/) the model in the wrong scale. Should be m (meters):

![](https://docs.bannerlordmodding.lt/pics/2601280817.png)

## Model Viewer Problems

### Save/Load Scene

`File` - `Save Scene`/`Load Saved Scene` messes up the viewer, weird colors/shadows, no idea how to restore without the Editor restart:

![](https://docs.bannerlordmodding.lt/pics/2410042212.png)

### Closed window

If Model Viewer window is closed and then opened again from the menu - it does not work.

Helps only Editor restart:

![](https://docs.bannerlordmodding.lt/pics/2410042207.png)
