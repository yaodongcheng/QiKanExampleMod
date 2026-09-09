# FBX import into Editor

<!-- 源: https://docs.bannerlordmodding.lt/3d/editor_fbx_import/ | 抓取日期: 2026-09-09 -->

## Import

On the empty space press RMB (Right Mouse Button), select `Import new asset`:

![](https://docs.bannerlordmodding.lt/pics/2410041439.png)

Select our FBX file, suffer through usual Editor messages:

![](https://docs.bannerlordmodding.lt/pics/2410041445.png)

Leave default import settings, press `Import`:

![](https://docs.bannerlordmodding.lt/pics/2410041448.png)

If all is ok, you should see the FBX file and the model itself:

![](https://docs.bannerlordmodding.lt/pics/2410041550.png)

Sometimes model is not shown:

![](https://docs.bannerlordmodding.lt/pics/2410041557.png)  
This is ok, import is successful, but not sure how to make model to show. After restarting the Editor it's ok though.

## Problems

### No model

![](https://docs.bannerlordmodding.lt/pics/2410041618.png)

When FBX file is imported, but the model is missing, that means that you imported the model with the same name - such model already exists.

Rename it in the Blender and reimport.

### Too much imported

![](https://docs.bannerlordmodding.lt/pics/2410041620.png)

When too much is imported, that means you did not check `Limit to Selected Objects` when [exporting FBX from Blender](/3d/export_to_fbx/#select-export-settings) and exported everything from it.

Delete what's imported, [export FBX](/3d/export_to_fbx/) properly and reimport.

### Wrong material

If you failed to create the material with the [same name as in the Blender](/3d/rename_material/):

![](https://docs.bannerlordmodding.lt/pics/2410041639.png)

You will get such error messages for each LOD (so 18 click for 6 LODs):

![](https://docs.bannerlordmodding.lt/pics/2410041642.png)

After all that clicking you will see your model without the material:

![](https://docs.bannerlordmodding.lt/pics/2410041644.png)

You can assign the material for each LOD manually:

![](https://docs.bannerlordmodding.lt/pics/2410041645.png)

I usually just delete the FBX, fix material in the Blender, export FBX and reimport FBX OR just rename the material in the Editor to match what came from the Blender.

### Model too big/small

![](https://docs.bannerlordmodding.lt/pics/2601280813.png)

You imported the model in the wrong scale. Should be m (meters):

![](https://docs.bannerlordmodding.lt/pics/2601280817.png)

## Hand Morph Import Settings For Gloves

![](https://docs.bannerlordmodding.lt/pics/2505301511.png)

You cannot use skinning precise option in the glove material if you do morphs
