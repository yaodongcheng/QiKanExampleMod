# 骑砍Ⅱ霸主MOD开发(11)-可编程渲染管线Shader编程

> 来源: https://blog.csdn.net/qq_35829452/article/details/139541975
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.固定渲染管线&可编程渲染管线

     固定渲染管线:GPU常规渲染算法,将3D模型经过四大变换计算得到2D屏幕图像

     可编程渲染管线:定制化GPU渲染算法,需要提交Shader至GPU中,GPU渲染管线经历顶点着色器,像素着色器输出2D屏幕对象

二.CoreShader&TerrainShader

     CoreShader:游戏中使用的核心shader,用于模型的材质渲染(pbr_metallic主要shader)

             源代码存放路径:Mount & Blade II Bannerlord\Shaders\Sources

             编译结果存放路径:Mount & Blade II Bannerlord\Shaders\D3D11

    TerrainShader:游戏中针对地形系统动态生成的Shader,用于地形系统图层的渲染

             源代码存放路径:Mount & Blade II Bannerlord\Shaders\Sources\pbr_terrain.rs

             编译结果存放路径:SceneObj\aserai_castle_d\ShaderCache\D3D11

三.创建自定义CoreShader

     <1.在TPAC资产中创建属于自己的Shader,将Filename字段填充为shader_test

     <2.在Mount & Blade II Bannerlord\Shaders\Sources新增shader_test.rs文件

#顶点着色器
VS_OUTPUT_FONT main_vs()
{
}

#像素着色器
PS_OUTPUT main_ps()
{
}

    <3.编译sack文件

          1.ModdingKit中File->CreatePackage(选择compile shader复选框)

          2.在Mod目录下生成Shaders文件夹,\MOD\Shaders\D3D11\sack文件(shader缓存文件)

          3.删除根目录Shaders\D3D11\sack文件下sack文件(如果没有使用新的shader,则不需打包和重新编译).启动时将会使用MOD下的sack文件进行解析和加载.

四.创建自定义TerrainShader

     ModdingKit-场景编辑器保存场景时自动生成

五.动态Shader编译

    <1.动态Shader编译

         由于GPU,GPU版本,硬件,操作系统等因素影响,相同的Shader编译结果不同,故在RGL引擎拉起时需要根据实际环境进行动态Shader编译.

    <2.Shader编译结果

         C:\ProgramData\Mount and Blade II Bannerlord\Shaders

                
