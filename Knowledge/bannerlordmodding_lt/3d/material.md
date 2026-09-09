# Material

<!-- 源: https://docs.bannerlordmodding.lt/3d/material/ | 抓取日期: 2026-09-09 -->

## Open Resource Browser

Open the Modding Kit ([Editor](/editor/editor/)) and then [Resource Browser](/editor/resource_browser/) with your mod selected:

![](https://docs.bannerlordmodding.lt/pics/2410040940.png)

Go to `Modules - YOUR_MOD - Assets`:

![](https://docs.bannerlordmodding.lt/pics/2410040914.png)

## Create Folder

RMB (Right Mouse Button) on the empty space and `Create - Folder`:

![](https://docs.bannerlordmodding.lt/pics/2410040916.png)

Name it like your project. For example I call it 'lt\_hrodno\_helmet':

![](https://docs.bannerlordmodding.lt/pics/2410040917.png)

## Import Textures

Go inside that folder, RMB on the empty space, `Import new asset':

![](https://docs.bannerlordmodding.lt/pics/2410040919.png)

Select the textures for your model and press Open:

![](https://docs.bannerlordmodding.lt/pics/2410040920.png)

Click through the usual error messages:

![](https://docs.bannerlordmodding.lt/pics/2410040924.png)

Result should look like this:

![](https://docs.bannerlordmodding.lt/pics/2410040943.png)

Editor crashes, PNG is not imported:

Sometimes shows such error message before crashing:  
![](https://docs.bannerlordmodding.lt/pics/2410041152.png)  
Reason: There are non-Latin characters in the folder's name where the PNG files are located. ¯\\_(ツ)\_/¯

Texture is not uploaded

Try to rename it (maybe texture with the same name already exists)  
Try to upload the texture not from the OneDrive

What does the specular \_s texture do again?

Kemo III:  
The RED channel is metallic, so fully red is a metal  
GREEN is smoothness. (Opposite of roughness in blender)  
And BLUE is ambient occlusion

## Create Material

RMB on the empty space, `Create - Material`:

![](https://docs.bannerlordmodding.lt/pics/2410040947.png)

Name it like your project name:

![](https://docs.bannerlordmodding.lt/pics/2410040948.png)

## Assign Textures

In the right panel click on the `default_editor_texture_d` in the `DiffuseMap` and select your first texture that usually ends in \_d (diffuse):

![](https://docs.bannerlordmodding.lt/pics/2410040951.png)

`NormalMap` and `SpecularMap` textures are assigned automatically, if you have textures with the same name with \_n and \_s at the end of the names:

![](https://docs.bannerlordmodding.lt/pics/2410040952.png)

## Set Settings

In `Material Shader Flags` mark `use_specular`, `do_not_use_alpha` (if you are not using alpha/transparency), `do_not_use_vertex_color_as_occlusion`:

![](https://docs.bannerlordmodding.lt/pics/2410040955.png)

In `Others` set `Two Sided` if you need texture to be visible from both sides (I do this for helmets so the insides could be visible also), in `Vertex Layout` mark `Skinning` if your model is rigged ([Weight Painted](/3d/weight_painting/) with [Armature/Skeleton](/3d/armature_skeleton/)), then press Save:

![](https://docs.bannerlordmodding.lt/pics/2410040956.png)

If your model is rigged and you forgot to mark `Skinning` you will get this in the [Model Viewer](/3d/model_viewer/#disabled-skinning):

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/material_skinning_problem.webm)
