# Blender-Python库安装

> 来源: https://blog.csdn.net/qq_35829452/article/details/133995304
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Blender外部python库所在位置

D:\work\Blender\4.1\python\lib\site-packages

二.Blender安装numpy库

import subprocess
import sys
subprocess.check_call([sys.executable, "-m", "pip", "install", "numpy"])

                
