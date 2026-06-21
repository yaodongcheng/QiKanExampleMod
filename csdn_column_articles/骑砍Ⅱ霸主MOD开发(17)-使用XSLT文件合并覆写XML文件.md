# 骑砍Ⅱ霸主MOD开发(17)-使用XSLT文件合并覆写XML文件

> 来源: https://blog.csdn.net/qq_35829452/article/details/140731873
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.MBObject

    沙盒游戏/自定义游戏/多人联机游戏中所需要的业务数据,王国,角色,文化等统一被定义为MBObject,所有的MBObject通过继承MBObjectBase获得GUID,ID等。

二.MBObject配置&加载&读取

    配置:SubModule.xml中<xmls>标签声明ModuleData中xml加载路径

    加载:MBGameManager.LoadModuleData()(InculdeGameType用于过滤游戏模式)

    获取:MBObjectManager.Instance.GetObject<ItemObject>("guarded_padded_vambrace");

三.MBProject配置文件

    引擎所需的重要数据配置文件即MBProject配置文件,例如骨骼动画,语音系统,粒子系统等依赖的配置文件

    配置路径:ModuleData\project.mbproj配置的

四.多个MOD不同XML加载&合并

    本体MOD:Native/ModuleData/a.xml         <test id = '12'>q</test>

    本体MOD:SandBox/ModuleData/b.xml    <test id = '12'>w</test>

    开发MOD:NativeTest/ModuleData/a.xml  <test id = '12'>k</test>  <test id = '78'>k</test>

    实际加载后id=12对应的MBObject数值仍为q,id=78对应MBObject数值为k。

五.使用XSLT文件剔除本体中指定元素

    开发MOD:NativeTest/ModuleData/a.xml  <test id = '12'>k</test>  <test id = '78'>k</test>

    若要实现开发MOD中id =12中数值为k,那么需要创建a.xslt文件,剔除本体中对应元素:

<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
    <xsl:output omit-xml-declaration="yes"/>
    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>
    <xsl:template match="test[@id='12']"/>
</xsl:stylesheet>



                
