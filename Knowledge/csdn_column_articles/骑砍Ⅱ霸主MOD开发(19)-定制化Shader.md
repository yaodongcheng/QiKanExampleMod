# 骑砍Ⅱ霸主MOD开发(19)-定制化Shader

> 来源: https://blog.csdn.net/qq_35829452/article/details/141208132
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Tableau图标

     <1.实际应用

          Mission中Agent头部label标识3D旗帜图标。

    <2.着色器实现:tableau_with_alpha_mask.rs

         根据材质输入参数确定3DMesh映射为2D纹理位置,高度,透明度.

    <3.使用

二.Decal贴花

    <1.实际应用

         大地图中围城光标,血迹等动态贴花实现.

    <2.着色器实现:decal.rs

         获取场景中与Decal贴近的GameEntity,将Decal中2D纹理映射至目标上.

    <3.使用Decal贴花

         1.导入2D贴图,创建decal对应材质decal_mat

         2.添加Decal至GameEntity,设置世界坐标

Decal decal = Decal.CreateDecal(null);
decal.SetMaterial(decal_mat);
decal.SetFactor1Linear();
GameEntity.AddComponent(decal);
GameEntity.setGlobalFrame();

三.Contour描边

    <1.实际应用

         人物轮廓显示

    <2.着色器实现:contour.rsh

         在像素着色器中不进行2D纹理映射,输入3D模型网格轮廓.

    <3.使用Contour描边着色器

uint color = new Color(1f, 0.84f, 0.35f, 1f).ToUnsignedInteger();
GameEntity.SetContourColor(color, true);

四.海洋模拟

    <1.实际应用

         大地图&游戏场景中水面,河流,海洋随天气系统起伏,

    <2.着色器实现:water_simulation.rs

         使用计算着色器实现,无需调用顶点着色器&像素着色器单元,

         输入参数:振幅,时间,风向,重力

    <3.使用海洋模拟着色器

         调整scene.xscene中wind,water_wind_dependency参数

五.布料模拟

    <1.实际应用

         旗帜,披风等随风飘扬效果

    <2.着色器实现:shared_vertex_functions.rsh

#输入顶点的顶点颜色ALPHA值将决定布料模拟是否进行
void rgl_cloth_transform()
{
	const bool use_cloth = In.color.a > 0;
}

    <3.使用

六.弓弦动画

    <1.实际应用

         大地图&游戏场景中水面,河流,海洋随天气系统起伏,

    <2.着色器实现:bow_deformer.rsh

#弓的顶点动画根据顶点颜色的G通道确定
void deform_bow()
{
    pos.x += (1.0f - abs_point_on_bow) * progress * d_pull_max * vertex_color.g;
}

    <3.使用

七.粒子模拟

                
