# 骑砍Ⅱ霸主MOD开发(4)-游戏场景Scene

> 来源: https://blog.csdn.net/qq_35829452/article/details/137966654
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Mission

    进入野外战斗,大地图,竞技场等3D游戏环境即为Mission,Mission由多个Components组成.

    Mission = MissionLogic + MissionView + Scene

    MissionLogic:在Mission中动态业务逻辑例如战场AI,战场音乐等.

    MissionView:在Mission中的2D界面绘制,例如血条,计分板等.

    Scene:在Mission中的地形,场景物,天气,导航网格,路径等静态数据集合.

    SceneLevel:不同Mission中场景物不同,投石车参观Mission中会消失,在围攻Mission中会存在

二.Scene

    场景 = 地形系统 + 植被系统 + 海洋系统 + 天气系统 + 游戏实例 + 导航网格

    <1.地形系统

terrain.bin:材质ID分布图(MIDX)+高度分布图(HGHT)+材质权重图(WGHT)+物理材质分布图(PHYS)

terrain_shaders_header_data.bin:网格贴图Shader数据(terrain_header_data.rsh)

scene.xscene<terrain/>:节点数量(node_dimension) + 节点大小(node_size) + 网格LOD参数

    ->scene.xscene<layer/>:材质贴图参数,材质物理材质参数

    ->scene.xscene<node/>:材质贴图权重分布

    <2.植被系统

flora.bin:植被(树木,草)GameEntity(
    预制件(Prefab) + 坐标(position) + 旋转(rotation) + 边界(Bounding) + 颜色(color))

ModuleData/flora_kinds.xml:植被预制件配置文件

scene.xscene<flora/>:植被边界(Bounding)

    <3.海洋系统

scene.xscene<water_properties/>:水材质 + 水位线 + 风力等级

   <4.天气系统

atomosphere.xml:季节粒子效果&天空盒&太阳经纬度&环境参数

scene.xscene<atmosphere_properties/>:天气参数,渲染级别

   <5.游戏实例

scene.xscene<game_entity/>:出生点,地图边界,地图outer_mesh等实例

   <6.导航网格

navmesh.bin:导航网格对应点,边,面数据

三.地形系统

    <1.地形长&宽

width = nodeCount.X * nodeSize
height = nodeCount.Y * nodeSize

    <2.地形网格顶点数

int[][] nodeDimensionArr = new int[nodeCountX][nodeCountY];
#X轴方向顶点数
 int nodeDimensionX = 1;
 for(int i=0; i<nodeCountX; i++)
 {
    nodeDimensionX += nodeDimensionArr[i][0]
 }
#Y轴方向顶点数
 int vertexCntY = 1;
 for(int i=0; i<nodeDimensionY; i++)
 {
    nodeDimensionY += nodeDimensionArr[0][i]
 }
#整个Terrain的网格数
vertex_count = nodeDimensionY * nodeDimensionX

  <3.获取地形参数API

#获取基本网格配置参数
Scene.GetTerrainData()

#获取地形网格高度图
Scene.GetTerrainHeightData()

#获取地形大小
Scene.GetBoundingBox()

四.植被系统

#ModuleData/flora_kinds.xml配置预制件名称
<flora_variation
	body_name="bo_tree_acacia_fall_2"
	name="tree_acacia_fall_2"
	density_multiplier="1.000"
	bb_radius="11.23039055">
</flora_variation>

#scene.xscene配置植被最大最小边界
<flora_bounding_rect min="212.851, 194.081" max="932.115, 951.931"/>

五.海洋系统

    1.海洋材质&海洋平面起伏(正弦波FFC)

#scene.xscene配置水体材质,水体风力等级
<water_properties version="1">
   <property name="water_level" value="-100.000"/>
   <property name="water_strength" value="5.000"/>
   <property name="water_wind_dependency" value="1.000"/>
   <property name="water_material" value="water_default"/>
   <property name="water_shallow_color" value="1.000, 1.000, 1.000"/>
   <property name="water_deep_color" value="1.000, 1.000, 1.000"/>
   <property name="water_exists" value="false"/>
   <property name="place_water_probe" value="true"/>
</water_properties>


    2.波浪&浪花(SDF Clip距离计算)

#波纹的大小和密集度
Scene.SetWaterStrength()

#添加海浪&浪花
Scene.AddWaterWakeWithSphere()
Scene.AddWaterWakeWithCapusle()
Scene.TickWake()

六.天气系统

    1.天气系统加载

       <1.静态加载

            1.创建Prefab

#方案一,在MOD根目录创建Atmospheres文件夹,添加test_atmosphere.xml
<atmosphere>
    <values>
	    <value name="name" value="test_atmosphere"/>
	</values>
</atmosphere>

#方案二,在场景根目录创建atmosphere.xml
<atmosphere>
    <values>
	    <value name="name" value="scene_atmosphere"/>
	</values>
</atmosphere>

            2.scene.xscene中配置Prefab名称

<environment_properties>
   <atmosphere_properties>
	  <property name="atmosphere_name" value="test_atmosphere"/>
   </atmosphere_properties>
</environment_properties>

       <2.动态加载

#OpenMission时构造MissionInitializerRecord
MissionInitializerRecord.AtmosphereOnCampaign

#进入游戏场景后设置Prefab名称
Scene.SetAtmosphereWithName

     2.天气系统组成

        <1.天空盒(贴图,高度参数,atmosphere.xml配置)

        <2.太阳(光照,位置参数,atmosphere.xml配置)

        <3.云(atmosphere.xml配置)

        <4.雾(atmosphere.xml配置)

        <5.雨雪(atmosphere.xml配置)

        <6..风(scene.xscene配置)

七.游戏实例

    <1.场景边界

         1.添加定制化实例border_soft,border_hard可获得场景边界

#添加软边界
<game_entity prefab="border_soft">
</game_entity>

#添加硬边界
<game_entity prefab="border_hard">
</game_entity>

         2.获取场景边界

#获取soft_border
Mission.Current.Scene.GetSoftBoundaryVertex(int index)

#获取hard_border
Mission.Current.Scene.GetHardBoundaryVertex(int index)

    <2.出生点

         为GameEntity添加Tag,例如sp_player等,可控制AI,玩家初始化坐标

八.导航网格

     AI系统中AI寻路所需的辅助网格,可快速获取两点间最短路径

#获取NavMesh点/线/面
    Scene.GetIdOfNavMeshFace()
    Scene.GetNavMeshFaceIndex()
    Scene.GetNavMeshCenterPosition()
    Scene.GetPathBetweenAIFaces()
    Scene.GetLastPointOnNavigationMeshFromWorldPositionToDestination()
#动态导入删除NavMesh
    Scene.ImportNavigationMeshPrefab()
    Scene.RemoveEntity()

九.创建Mission

#沙盒游戏野外战斗Mission
SandBoxMissions.OpenBattleMission()

#藏身处Mission
SandBoxMissions.OpenHideoutBattleMission()

                
