# Blender-Python脚本(物体篇)

> 来源: https://blog.csdn.net/qq_35829452/article/details/134760453
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.创建圆柱体

bpy.ops.mesh.primitive_cylinder_add(enter_editmode=False, align='WORLD', location=(0, 0, 0), scale=(1, 1, 1))
bpy.context.object.name = 'capsule'


二.创建球体

bpy.ops.mesh.primitive_uv_sphere_add(enter_editmode=False, align='WORLD', location=(0, 0, 0), scale=(1, 1, 1))
bpy.context.object.name = 'sphere'


三.创建空物体

bpy.ops.object.empty_add(type='ARROWS', align='WORLD', location=(0, 0, 0), scale=(1, 1, 1))
bpy.context.object.name = 'empty_object'


四.根据点/线/面创建物体

mesh_name = ''
mesh_material_name = ''
vertices = []
faces = []
blender_mesh = bpy.data.meshes.new(mesh_name)
blender_mesh.from_pydata(vertices, [], faces)
blender_object = bpy.data.objects.new(mesh_name, blender_mesh)
blender_object.data.materials.append(bpy.data.materials.new(name=mesh_material_name))
bpy.context.collection.objects.link(blender_object)

五.复制物体

objects = bpy.context.scene.objects
blender_obj = objects[mesh_name].copy()
blender_obj.data = objects[mesh_name].data.copy()
blender_obj.data.materials[0] = objects[mesh_name].data.materials[0].copy()
blender_obj.rotation_euler = objects[mesh_name].rotation_euler.copy()
blender_obj.name = mesh_name
blender_obj_rotation = (0, 0, 0)
bpy.context.collection.objects.link(blender_obj)

六.设置物体父子级关系

blender_cube = bpy.context.scene.objects['cube']
bpy.context.object.parent = blender_cube

七.添加自定义属性

objects = bpy.context.scene.objects
blender_obj = objects[mesh_name].copy()
blender_obj['name'] = name
bpy.context.collection.objects.link(blender_obj)

                
