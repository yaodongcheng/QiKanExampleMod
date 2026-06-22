# 骑砍Ⅱ霸主MOD开发(30)-光照系统

> 来源: https://blog.csdn.net/qq_35829452/article/details/161130331
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.光源类型

    1.点光源

#在game_entity中添加light_component实现点光源添加
<light_component flags="96" color="1.000, 0.596, 0.243" shadow_radius="4.000"/>

    2.平行光源

#设置atomphere.xml中太阳参数实现平行光源参数调整
<value name="sun_intesity" value="7500.522"/>
<value name="sky_brightness" value="400.121"/>
<value name="cloud_brightness" value="0.700"/>
<value name="sun_size" value="0.150"/>
<value name="sunshafts_strength" value="1.200"/>

#创建全局反射scriptcompoennt实现平行光源参数调整
<script name="ReflectionCapturer">
	<variables>
		<variable name="IsGlobal" value="true"/>
		<variable name="IsParallaxCorrected" value="false"/>
		<variable name="AmbientMultiplier" value="1.000"/>
		<variable name="AttenuationCoef" value="20.000"/>
		<variable name="BoxOffset" value="0.000, 0.000, 0.000, 0.000"/>
	</variables>
</script>

二.光源反射

    1.漫反射(材质漫反射贴图)

    2.镜面反射(材质镜面反射贴图)

                
