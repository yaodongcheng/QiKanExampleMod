# 骑砍Ⅱ霸主MOD开发(20)-Resource

> 来源: https://blog.csdn.net/qq_35829452/article/details/141209792
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Resource

     构成GameEntity的基本元素MetaMesh,Material,PhysicsShape等均被定义为Resource.

     Resource的类型:

          <1.预制Resource:存储至TPAC文件中.

          <2.动态Resource:应用程序动态创建(地图边界红色标识,旗帜标识)

二.预制Resource

     <1.Tpac文件

AssetPackage(Tpac)
    ->MetaMesh = GUID + AssetName + MetaData + AssetSegmentData
    ->Material = GUID + AssetName + MetaData + AssetSegmentData
    ->Texture = GUID + AssetName + MetaData + AssetSegmentData


     <2.根据名称读取Resource

Mesh.GetFromResource()
Material.GetFromResource()
Texture.GetFromResource()

三.Resource-MetaMesh

    <1.预制MetaMesh

MetaMesh.GetCopy()
MetaMesh.CreateMetaMesh()
MetaMesh.GetMeshAtIndex()


    <2.动态MetaMesh

#创建Mesh
Mesh mesh = Mesh.CreateMeshWithMaterial(Material.GetDefaultMaterial());
UIntPtr uIntPtr = mesh.LockEditDataWrite();
mesh.UnlockEditDataWrite(uIntPtr);

#修改顶点,UV
UIntPtr uIntPtr = mesh.LockEditDataWrite();
ManagedMeshEditOperations meshOperator = ManagedMeshEditOperations.Create(mesh);
mesh.UnlockEditDataWrite(uIntPtr);

四.Resource-Material

    <1.预制Material

Material.CreateCopy()
Material.SetShader()
Material.SetTexture()

五.Resource-Shader

六.Resource-Texture

     <1.预制Texture:

Texture.GetFromResource()

     <2.动态Texture

#图像数据映射(CPU映射)
 <1.方法一:
    Texture texture = ((EngineTexture)TaleWorlds.TwoDimension.PlatformTexture).Texture
 <2.方法二:
    Texture.CreateTextureFromPath()
 <3.方法三:
    Texture.CreateFromByteArray()

#Tableau映射(GPU映射)
 <1.缓存Tableau(人物,旗帜等Tableau)
    ThumbnailCacheManager.CreateTexture()
 <2.动态Tableau(过场动画)
    1.创建Scene-tableauScene
      Scene.Read()
    2.实例化AgentVisual,GameEntity
      Scene.AddItemEntity()
    3.将3D映射为2DTexture
      Texture.CreateTableauTexture()


七.Resource-PhysicsShape

    1.预制PhysicsShape:

#在Prefab预制件中声明
<game_entity name="bo_test">
   <physics shape="bo_test"/>
	<tags>
	 <tag name="bo_rts_building"/>
	</tags>
</game_entity>

    2.动态PhysicsShape:

#创建PhysicsShape
PhysicsShape shape = PhysicsShape.GetFromResource("bo_axe_short", false).CreateCopy();
shape.Clear();
shape.InitDescription();
shape.AddCapsule(new CapsuleData(1f, new Vec3(0f, 0f, -1f), new Vec3(0f, 0f, 1f)));

#设置PhysicsShape RigidBody参数 PhysicsMaterial参数
GameEntity gameEntity = GameEntity.CreateEmpty()
gameEntity.BodyFlag = BodyFlags.None;
gameEntity.BodyFlag |= BodyFlags.Moveable;
gameEntity.SetBodyShape(shape);
gameEntity.setMass();
gameEntity.AddPhysics();

                
