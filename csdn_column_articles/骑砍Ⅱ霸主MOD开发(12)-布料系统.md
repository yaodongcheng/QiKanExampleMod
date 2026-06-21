# 骑砍Ⅱ霸主MOD开发(12)-布料系统

> 来源: https://blog.csdn.net/qq_35829452/article/details/139640331
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    1.布料

   绑骨模型根据布料参数实现坐标偏移的效果即为布料模拟,布料参数存贮在MetaMesh-TPAC资源中.在加载该模型时由GPU读取.

2.顶点绑定骨骼

   布料MetaMesh绑定骨骼确定布料运动性质,GPU通过调整骨骼坐标实现布料随风而动.

3.顶点颜色

   布料MetaMesh顶点颜色Alpha通道确定了布料偏移的最小和最远距离,

3.布料材质

   布料物理参数,通过ModuleData\cloth_materials.xml配置,

4.布料碰撞体

   布料碰撞对象,通过ModuleData\cloth_bodies.xml配置,

5.布料LOD模型

   布料实际模拟时需要低面数模型,通过对原模型进行LOD处理得到clo_mesh布料模拟模型

                
