# python常用功能代码实现

> 来源: https://blog.csdn.net/qq_35829452/article/details/136827529
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.图像分辨率转化

from PIL import Image
import os
 
resolution = (1024, 1024)
with Image.open("spyglass.png") as img:
    img_resized = img.resize(resolution)
    img_resized.save("spyglass.png")

二.欧拉角转化旋转矩阵(右手坐标系, ZXY顺序)

# coding=UTF-8
from scipy.spatial.transform import Rotation as R
 
# 定义欧拉角 XYZ顺序
euler_angles = [90, 3, 3]

if (90 - abs(euler_angles[0])) < 0.5:
    euler_angles[0] = 89.5
euler_angles_zxy = [0, 0, 0]
euler_angles_zxy[0] = euler_angles[2]
euler_angles_zxy[1] = euler_angles[0]
euler_angles_zxy[2] = euler_angles[1]
euler_angles_zxy = [-x for x in euler_angles_zxy]
 
# 创建Rotation对象
r = R.from_euler('zxy', euler_angles_zxy, degrees=True)
 
# 获取旋转矩阵
rotation_matrix = r.as_dcm()
print('[')
for row in rotation_matrix:
    msg = ", ".join(map(str, row))
    msg = '[' + msg + '],'
    print(msg)
print(']')

三.旋转矩阵转化欧拉角(右手坐标系, ZXY顺序)

# coding=UTF-8
import numpy as np
from scipy.spatial.transform import Rotation as R
 
# 假设我们有一个旋转矩阵
#80 -7 45
rotation_matrix = np.array([
[0.9945219996629926, 0.10452647320585942, -0.00045671157999537665],
[-0.00045671157999537665, 0.008714576084760173, 0.9999619230641713],
[0.10452647320585942, -0.9944839227271639, 0.008714576084760173]
])
# 使用SciPy的Rotation对象转换欧拉角
rotation = R.from_dcm(rotation_matrix)
euler_angles_zxy = rotation.as_euler('zxy', degrees=True)  # sco存储使用'zxy'顺序，如果需要度为单位，设置degrees=True

#左右手参考坐标系正负值替换
euler_angles_zxy = [-x for x in euler_angles_zxy]
euler_angles = [0, 0, 0]
euler_angles[0] = euler_angles_zxy[1]
euler_angles[1] = euler_angles_zxy[2]
euler_angles[2] = euler_angles_zxy[0]
print(euler_angles)

四.PFM文件读取

from PIL import Image
import imageio
import numpy as np
import struct
 
#pfm array[height][width]
def read_pfm(file_name):  
    with open(file_name, "rb") as file:
        header = file.readline().decode('utf-8').rstrip()
        if header != 'Pf':
            raise Exception('Not a pfm file.')
        width, height = map(int, file.readline().decode('utf-8').rstrip().split())
        scale = float(file.readline().decode('utf-8').rstrip())
        data = np.fromfile(file, np.float32)
        shape = (height, width,  1)
        data = np.reshape(data, shape)
        return data

五.PFM文件生成

from PIL import Image
import imageio
import numpy as np
import struct

#pfm array[height][width]        
def write_pfm(file, height_map):
    width = len(height_map[0])
    height = len(height_map)
    print(str(width) + ',' +  str(height))
    file = open(file, 'wb')
    file.write('Pf\n'.encode('ascii'))
    file.write('%d %d\n'.encode('ascii') % (width, height))
    file.write('%.3f\n'.encode('ascii') % (-1))
 
    for row in height_map:
        for pixel in row:
            file.write(struct.pack('f', pixel))
    file.close()
    
height_map = read_pfm("example_read.pfm")
write_pfm('example_write.pfm', height_map)




                
