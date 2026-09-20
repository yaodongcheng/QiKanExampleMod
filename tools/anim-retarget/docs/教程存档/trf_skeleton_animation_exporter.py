import bpy

class TrfSkeletonAnimation:
    def __init__(self):
        self.name = ''
        self.bone_count = 0
        self.bone_animations = []

class TrfBoneAnimation:
    def __init__(self):
        self.index = 0
        self.name = ''
        self.position_frame_count = 0
        self.position_frames = []
        self.rotation_frame_count = 0
        self.rotation_frames = []

class TrfPositionFrame:
    def __init__(self):
        self.frame_time = 0
        self.x = 0
        self.y = 0
        self.z = 0

class TrfRotationFrame:
    def __init__(self):
        self.frame_time = 0
        self.w = 0
        self.x = 0
        self.y = 0
        self.z = 0

def export_trf_file(trf_skeleton_animation, file_path):
    trf_contents = []
    write_trf_skeleton_animation(trf_skeleton_animation, trf_contents)
    with open(file_path, 'w', encoding='utf-8') as file:
        file.writelines(trf_contents)

def write_trf_skeleton_animation(trf_skeleton_animation, trf_contents):
    #write skeleton animation header
    trf_contents.append('rfver 4\n')
    trf_contents.append('skeleton_anim ' + str(1) + '\n')
    space_prefix = ''
    row_arr = [trf_skeleton_animation.name, '1']
    trf_contents.append(space_prefix + ' '.join(row_arr) + '\n')
    #write rotation frames
    space_prefix = ' '
    trf_contents.append(space_prefix + str(trf_skeleton_animation.bone_count) + '\n')
    for trf_bone_animation in trf_skeleton_animation.bone_animations:
        write_trf_rotation_frames(trf_bone_animation, trf_contents)

    #write root position frames
    write_trf_position_frames(trf_skeleton_animation.bone_animations[0], trf_contents)
    trf_contents.append('end')

def write_trf_rotation_frames(trf_bone_animation, trf_contents):
    space_prefix = ' '
    trf_contents.append(space_prefix + str(trf_bone_animation.rotation_frame_count) + '\n')
    for trf_rotation_frame in trf_bone_animation.rotation_frames:
        row_arr = []
        row_arr.append(str(trf_rotation_frame.frame_time))
        row_arr.append(format(trf_rotation_frame.x, '.6f'))
        row_arr.append(format(trf_rotation_frame.y, '.6f'))
        row_arr.append(format(trf_rotation_frame.z, '.6f'))
        row_arr.append(format(trf_rotation_frame.w, '.6f'))
        trf_contents.append(space_prefix + ' '.join(row_arr) + '\n')

def write_trf_position_frames(trf_bone_animation, trf_contents):
    space_prefix = ' '
    trf_contents.append(space_prefix + str(trf_bone_animation.position_frame_count) + '\n')
    for trf_position_frame in trf_bone_animation.position_frames:
        row_arr = []
        row_arr.append(str(trf_position_frame.frame_time))
        row_arr.append(format(trf_position_frame.x, '.6f'))
        row_arr.append(format(trf_position_frame.y, '.6f'))
        row_arr.append(format(trf_position_frame.z, '.6f'))
        trf_contents.append(space_prefix + ' '.join(row_arr) + '\n')

def create_trf_skeleton_animation(skeleton_name, start_frame, end_frame):
    blender_skeleton_object = bpy.context.scene.objects[skeleton_name]
    blender_skeleton_animation = blender_skeleton_object.animation_data.action
    bpy.ops.object.mode_set(mode='POSE')
    blender_skeleton_pose = blender_skeleton_object.pose
    #create skeleton
    trf_skeleton_animation = TrfSkeletonAnimation()
    trf_skeleton_animation.name = blender_skeleton_animation.name
    #create bone animations
    create_bone_animation(trf_skeleton_animation, blender_skeleton_pose)
    #create rotation frames
    for frame_time in range(start_frame, end_frame):
        bpy.context.scene.frame_set(frame_time)
        create_rotation_frames(trf_skeleton_animation, blender_skeleton_pose, frame_time)
        create_position_frames(trf_skeleton_animation, blender_skeleton_pose, frame_time)
    return trf_skeleton_animation

def create_bone_animation(trf_skeleton_animation, blender_skeleton_pose):
    trf_skeleton_animation.bone_count = len(blender_skeleton_pose.bones)
    for i in range(0, trf_skeleton_animation.bone_count):
        pose_bone = blender_skeleton_pose.bones[i]
        trf_bone_animation = TrfBoneAnimation()
        trf_bone_animation.index = i
        trf_bone_animation.name = pose_bone.name
        trf_skeleton_animation.bone_animations.append(TrfBoneAnimation())

def create_rotation_frames(trf_skeleton_animation, blender_skeleton_pose, frame_time):
    for i in range(0, trf_skeleton_animation.bone_count):
        pose_bone = blender_skeleton_pose.bones[i]
        bone_animation = trf_skeleton_animation.bone_animations[i]
        trf_rotation_frame = TrfRotationFrame()
        trf_rotation_frame.frame_time = frame_time
        trf_rotation_frame.x = pose_bone.rotation_quaternion.x
        trf_rotation_frame.y = pose_bone.rotation_quaternion.y
        trf_rotation_frame.z = pose_bone.rotation_quaternion.z
        trf_rotation_frame.w = pose_bone.rotation_quaternion.w
        bone_animation.rotation_frames.append(trf_rotation_frame)
        bone_animation.rotation_frame_count = bone_animation.rotation_frame_count + 1

def create_position_frames(trf_skeleton_animation, blender_skeleton_pose, frame_time):
    pose_bone = blender_skeleton_pose.bones[0]
    bone_animation = trf_skeleton_animation.bone_animations[0]
    trf_position_frame = TrfPositionFrame()
    trf_position_frame.frame_time = frame_time
    trf_position_frame.x = pose_bone.location.x
    trf_position_frame.y = pose_bone.location.y
    trf_position_frame.z = pose_bone.location.z
    bone_animation.position_frames.append(trf_position_frame)
    bone_animation.position_frame_count = bone_animation.position_frame_count + 1

trf_skeleton_animation = create_trf_skeleton_animation('dog_skeleton', 1, 12)
print("skeleton animation name:" + trf_skeleton_animation.name)
print("skeleton animation bone_count:" + str(trf_skeleton_animation.bone_count))

export_trf_file(trf_skeleton_animation, 'D:/dog_cu_jump_animation_1.trf')
