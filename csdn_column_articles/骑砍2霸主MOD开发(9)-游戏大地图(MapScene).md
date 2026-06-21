# 骑砍2霸主MOD开发(9)-游戏大地图(MapScene)

> 来源: https://blog.csdn.net/qq_35829452/article/details/139310593
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.大地图MapScene

     大地图游戏场景MapScene:SandBox/SceneObj/Main_map

          Campaign.LoadMapScene()

                             ⬇

                MapScene.Load()

                             ⬇

          MapScene.Read("Main_map", ref sceneInitializationData, "") 装载Main_map大地图



二.大地图地形系统

     大地图中军团(MobileParty)的移动速度,AI寻路,食物消耗等均和当前MobileParty所处地形有关,地形统一被定义为TerrainType,类型有森林,荒漠等.

     MapScene.GetFaceIndex() 解析Main_map游戏场景中导航网格navmesh.bin

                             ⬇

     MapScene.GetFaceTerrainType() 完成地形与导航网格的映射(FaceID对应不同的地形)

三.大地图天气系统

     大地图白天/黑夜,雨天/雪地等效果即为天气系统.

     scene.xscene中实现MapColorGradeManager该GameEntityComponent实现对大地图游戏场景MapScene的天气系统控制

    <game_entity mobility="1" name="color_grade_manager" old_prefab_name="">
      <transform position="476.441, 221.187, 2.895" rotation_euler="0.000, 0.000, 0.000" />
      <scripts>
        <script name="MapColorGradeManager">
          <variables>
            <variable name="ColorGradeEnabled" value="false" />
            <variable name="AtmosphereSimulationEnabled" value="false" />
            <variable name="TimeOfDay" value="10.000" />
            <variable name="SeasonTimeFactor" value="0.000" />
          </variables>
        </script>
      </scripts>
    </game_entity>

四.大地图边界Border

     大地图边Border将决定大地图摄像机移动范围和高度.

     MapScene.GetBorders()

                     ⬇

     MapScene.GetFirstEntityWithName("border_min")加载name为border_min的GameEntity

                     ⬇

     Campaign.MapMinimumPosition = mapMinimumPosition 完成边界数值初始化

    <game_entity name="border_min" old_prefab_name="">
      <transform position="62.000, 80.000, 0.000" rotation_euler="0.000, 0.000, 0.000" />
    </game_entity>
    <game_entity name="border_max" old_prefab_name="">
      <transform position="790.000, 640.000, 200.000" rotation_euler="0.000, 0.000, 0.000" />
    </game_entity>

五.大地图城池Settlement

    大地图城池游戏实例Settlement由若干子GameEntity组成,子GameEntity决定了城池中军团出生点,围城时攻城武器初始化等业务逻辑

    settlement(GameEntity)

              ->gate_position(GameEntity大地图军团出生点/AI行为防守点)

              ->map_icon_siege(GameEntity围城/攻城时攻城器械和营地的创建/销毁)

                      ->attacker_siege_*:(GameEntity攻城武器)

                      ->defender_*:(GameEntity守城武器)

                      ->map_icon_siege_camp:(GameEntity围城营地)

    <1.Settlement类型

         Settlement类型由town,castle,castle_village,village,hideout五种类型组成,通过对GameEntity中添加tag实现类型的加载.

         <tag name="town" />

         <tag name="castle" />

         <tag name="castle_village" />

         <tag name="village" />

         <tag name="hideout" />

    <2.Settlement坐标(2D坐标/3D坐标)

         ModuleData/Settlements.xml:posX&posY确定Settlement的2D坐标。

         Main_map/scene.xscene中Settlement实例的3D坐标

    <3.Settlement坐标缓存settlements_distance_cache.bin

         ModuleData/settlements_distance_cache.bin:每次修改大地图后需删除,重开档刷新缓存。

    <4.ModdingKit更新城池坐标刷新脚本SettlementPositionScript

         每次修改scene.scene中城池坐标,都需修改ModuleData/Settlements.xml以实现坐标同步.

         SettlementPositionScript.SaveSettlementPositions()

    <5.C#脚本编辑Settlement.xml和scene.xscene中城池坐标

class MainMapSettlementEditor
{
    public static string sceneRootPath = "D:\\work\\Steam\\steamapps\\common\\Mount & Blade II Bannerlord\\Modules\\PLReminiscence\\SceneObj\\Main_map";
    public static string sceneXmlPath = "scene.xscene";
    public static string rootPath = "D:\\work\\Steam\\steamapps\\common\\Mount & Blade II Bannerlord\\Modules\\PLReminiscence\\ModuleData";
    public static string xmlPath = "settlements.xml";

    public static void Main()
    {
        XmlDocument sceneXSceneDoc = XmlUtilities.ReadXml(sceneRootPath + Path.DirectorySeparatorChar + sceneXmlPath);
        XmlDocument xmlDoc = XmlUtilities.ReadXml(rootPath + Path.DirectorySeparatorChar + xmlPath);
        SetSettlement2DAnd3DPosition("castle_K2", new Vector2(413, 443), sceneXSceneDoc, xmlDoc);
        SetSettlement2DAnd3DPosition("town_B1", new Vector2(485, 531), sceneXSceneDoc, xmlDoc);

        xmlDoc.Save(rootPath + Path.DirectorySeparatorChar + "test1.xml");
        sceneXSceneDoc.Save(rootPath + Path.DirectorySeparatorChar + "test2.xml");
    }

    public static void SetSettlement2DAnd3DPosition(string settlementId, Vector2 position2D, XmlDocument sceneXSceneDoc, XmlDocument xmlDoc)
    {
        XmlElement settlementEntity = GetGameEntityByName(settlementId, sceneXSceneDoc);
        Vector3 campaignIconPosition = GetGameEntityPosition(settlementEntity.ParentNode.ParentNode);
        Vector3 settlementPosition = new Vector3(position2D.X, position2D.Y, 0) - campaignIconPosition;
        XmlNode gateGameEntity = GetSettlementGateGameEntity(settlementId, sceneXSceneDoc);
        Vector3 gatePosition = new Vector3(position2D.X, position2D.Y, 0) + GetGameEntityPosition(gateGameEntity);

        //1.set campaign icon transform
        SetGameEntityTransform(settlementEntity.ParentNode.ParentNode, new Vector3(campaignIconPosition.X, campaignIconPosition.Y, 0.000f), new Vector3(0.000f, 0.000f, 0.000f));
        //2.set settlement transform
        SetGameEntityTransform(settlementEntity, new Vector3(settlementPosition.X, settlementPosition.Y, 0), new Vector3(0.000f, 0.000f, 0.000f));
        //3.set gate transform
        SetGameEntityTransform(gateGameEntity, new Vector3(GetGameEntityPosition(gateGameEntity).X, GetGameEntityPosition(gateGameEntity).Y, 0), new Vector3(0.000f, 0.000f, 0.000f));



        XmlElement settlementElement = GetSettlmentById(settlementId, xmlDoc);
        //4.set settlment posX posY
        SetSettlementPosition(settlementElement, new Vector3(position2D.X, position2D.Y, 0));
        //5.set settlment gate_posX gate_posY
        SetSettlementGatePosition(settlementElement, new Vector3(gatePosition.X, gatePosition.Y, 0));


    }

    public static XmlElement GetSettlmentById(string settlementId, XmlDocument xmlDoc)
    {
        XmlNodeList xmlNodeList = xmlDoc.GetElementsByTagName("Settlement");
        foreach (XmlElement item in xmlNodeList)
        {
            string id = item.GetAttribute("id");
            if (id == settlementId)
            {
                return item;
            }
        }
        return null;
    }

    public static XmlElement GetGameEntityByName(string name, XmlDocument xmlDoc)
    {
        XmlNodeList gameEntityList = xmlDoc.GetElementsByTagName("game_entity");
        foreach (XmlElement item in gameEntityList)
        {
            string itenName = item.GetAttribute("name");
            if (itenName == name)
            {
                return item;
            }
        }
        return null;
    }

    public static XmlNode GetSettlementGateGameEntity(string settlmentId, XmlDocument xmlDoc)
    {
        XmlNodeList gameEntityList = xmlDoc.GetElementsByTagName("tag");
        foreach (XmlElement item in gameEntityList)
        {
            string itenName = item.GetAttribute("name");
            if (itenName == "main_map_city_gate" && item.ParentNode.ParentNode.ParentNode.ParentNode.Attributes["name"].Value == settlmentId)
            {
                return item.ParentNode.ParentNode;
            }
        }
        return null;
    }

    public static Vector3 GetGameEntityPosition(XmlNode item)
    {
        foreach (XmlElement child in item)
        {
            if (child.Name == "transform")
            {
                string[] vecStr = child.GetAttribute("position").Split(",");

                return new Vector3(float.Parse(vecStr[0]), float.Parse(vecStr[1]), float.Parse(vecStr[2]));
            }
        }
        return new Vector3(0.000f, 0.000f, 0.000f);
    }

    public static void SetGameEntityTransform(XmlNode item, Vector3 position, Vector3 rotation)
    {
        foreach (XmlElement child in item)
        {
            if (child.Name == "transform")
            {
                child.SetAttribute("position", position.X.ToString("F3") + "," + position.Y.ToString("F3") + "," + position.Z.ToString("F3"));
                child.SetAttribute("rotation_euler", rotation.X.ToString("F3") + "," + rotation.Y.ToString("F3") + "," + rotation.Z.ToString("F3"));
            }
        }
    }

    public static void SetSettlementPosition(XmlNode item, Vector3 position)
    {
        item.Attributes["posX"].Value = position.X.ToString("F3");
        item.Attributes["posY"].Value = position.Y.ToString("F3");
    }

    public static void SetSettlementGatePosition(XmlNode item, Vector3 gatePosition)
    {
        item.Attributes["gate_posX"].Value = gatePosition.X.ToString("F3");
        item.Attributes["gate_posY"].Value = gatePosition.Y.ToString("F3");
    }
}

六.大地图城门

     每一个Settlement都有一个gate_position,gate_position将决定军团的出生点,AI行为防守点

     <1.gate_position坐标(2D坐标/3D坐标)

          ModuleData/Settlements.xml:gate_posX&gate_posY确定Settlement的2D坐标。

          Main_map/scene.xscene中name为gate_position的3D坐标

七.大地图城池攻城点

     map_icon_siege







                
