# 骑砍Ⅱ霸主MOD开发(16)-FaceKey&BannerKey

> 来源: https://blog.csdn.net/qq_35829452/article/details/140675882
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.FaceKey人物模型生成流程

FaceKey编辑(沙盒模式按下V键进入编辑页面,Ctrl+C,Ctrl+V获取FaceKey)

                   ⬇

FaceKey参数获取(NpcCharacter配置FaceKey,序列化对象BodyProperties)

                   ⬇

FaceKey参数解析(序列化对象BodyProperties反序列化为FaceGenerationParams对象)

                   ⬇

读取ModuleData\skins.xml指定skin节点(Race,Gender,Age组成主键唯一确定skin节点)
                   
                   ⬇

人物模型生成(skin节点 + FaceGenerationParams确定头部/身体/四肢模型生成)

二.FaceKey参数序列化&反序列化

FaceKey序列化对象:
BodyProperties:
    DynamicBodyProperties:weight,age,build等参数
    StaticBodyProperties:由KeyPart组成的数字字符串

FaceKey反序列化对象:
FaceGenerationParams
    CurrentFaceTattoo:战痕
    KeyWeights[320]:脸部特征deformKey数值,最大数量320

#解析FaceKey(序列化对象 -> 反序列化对象)
MBBodyProperties.GetParamsFromKey()

#生成FaceKey(反序列化对象 -> 序列化对象)
MBBodyProperties.ProduceNumericKeyWithParams()

三.FaceKey模型组成

    <1.头部

#一个MetaMesh四个子Mesh组成,每个Mesh都包含Tag实现区分
1.头部 = 脸部 + 嘴 + 睫毛 + 眼睛
2.head_feamle_a = head_feamle_a.0 + head_feamle_a.1 + head_feamle_a.2 + head_feamle_a.3
3.头部模型
  <skin face_meta_mesh="head_male_a">
  </skin>
4.绑骨实现:
  head_feamle_a绑定人类骨骼Head,Neck
5.动画实现:
  <skin>
    <deform_keys/> #顶点动画关键帧
  </skin>
6.皮肤颜色实现:
  贴图RGB通道实现

    <2.眉毛

<skin>
   <eyebrow_meshes/>
</skin>

    <3.头发

<skin>
   <hair_meshes/>
</skin>

    <4.身体

<skin body_meta_mesh = "body_male_a">
</skin>

    <5.躯干

<skin legs_mesh="feet_male_a" hands_mesh="hands_male_a">
</skin>

四.BannerKey生成旗帜模型流程

     旗帜制作网站:​https://bannerlord.party/banner/

BannerKey参数获取(B键呼出旗帜编辑获取,通过网站获取)

                   ⬇

BannerKey参数解析(模型,贴图,颜色参数获取)

                   ⬇

旗帜贴图,材质,模型生成(ModuleData\banner_icons.xml)

五.BannerKey参数解析

#Banner中读取BannerKey参数
Banner
{
    MBList<BannerData> _bannerDataList; 
}
BannerData
{
    MeshId
    Color1
    Color2
}

#根据BannerKey生成Banner
Banner banner = new Banner(bannerKey)

                
