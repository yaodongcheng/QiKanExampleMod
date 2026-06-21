# Blender-Python脚本(骨骼动画篇)

> 来源: https://blog.csdn.net/qq_35829452/article/details/132353935
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.设置骨骼自定义模型

bpy.ops.object.mode_set(mode='POSE')
bpy.context.object.pose.bones["bone_0"].custom_shape = bpy.data.objects["球体"]

二.插入关键帧

bpy.ops.object.mode_set(mode='POSE')
pose_bones = bpy.context.object.pose.bones
pose_bone = pose_bones['bone_01']
pose_bone.rotation_quaternion = [1, 0, 0, 0]
pose_bone.location = [1, 2, 3]
pose_bone.keyframe_insert(data_path="rotation_quaternion", frame = 0)
pose_bone.keyframe_insert(data_path="location", frame = 0)

三.读取关键帧

start_frame = 0
end_frame = 100

#关键帧
for frame_time in range(start_frame, end_frame):
    bpy.context.scene.frame_set(frame_time)
    pose_bones = bpy.context.object.pose.bones
    pose_bone = pose_bones['bone_01']
    location = pose_bone.location
    rotation_quaternion = pose_bone.rotation_quaternion

四.动画摄影表设置帧时刻



bpy.context.scene.frame_current = 2
bpy.context.scene.frame_start = 0
bpy.context.scene.frame_end = 100

五.编辑骨骼动画

skeleton_name = 'human_skeleton'
blender_skeleton_object = bpy.context.scene.objects[skeleton_name]
blender_skeleton_animation = blender_skeleton_object.animation_data.action
blender_skeleton_animation.name = 'test_animation'

                
