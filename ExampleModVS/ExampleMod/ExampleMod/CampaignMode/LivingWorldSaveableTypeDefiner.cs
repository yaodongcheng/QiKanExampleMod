using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Resolvers;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 存档类型注册器（2026-09-08 雷 32：SaveFailed "Could not find type definition of type: TaikouCampaign"）。
	/// 机制（StoryMode/织丰同款实锤）：SaveableTypeDefiner 派生类由引擎启动时自动发现并实例化
	/// （StoryMode SaveableStoryModeTypeDefiner base=320000、织丰 ShokuhoSaveableTypeDefiner base=3564814——
	/// 两者均无显式 new 调用点），注册的自定义战役类才有存档类型 ID。
	/// base ID = 本注册器全局唯一标识（官方 1-100k，StoryMode 320000，织丰 3564814——本注册器取 4455667）。
	/// class ID = 本注册器内唯一。
	/// </summary>
	public class LivingWorldSaveableTypeDefiner : SaveableTypeDefiner
	{
		public LivingWorldSaveableTypeDefiner()
			: base(4455667)
		{
		}

		protected override void DefineClassTypes()
		{
			// 🔴 每个时代的具体战役类都要注册（漏一个 = 该时代的档存不出来，
			//   报 "Could not find type definition of type: TaikouCampaign15xx"，雷 32 原样复发）。
			// 加新时代 = 这里加一行（class ID 递增）。
			AddClassDefinition(typeof(TaikouCampaign1554), 5, (IObjectResolver)null);
			AddClassDefinition(typeof(TaikouCampaign1560), 1, (IObjectResolver)null);
			AddClassDefinition(typeof(TaikouCampaign1568), 6, (IObjectResolver)null);
			AddClassDefinition(typeof(TaikouCampaign1575), 7, (IObjectResolver)null);
			AddClassDefinition(typeof(TaikouCampaign1582), 3, (IObjectResolver)null);
			AddClassDefinition(typeof(TaikouCampaign1598), 8, (IObjectResolver)null);
			AddClassDefinition(typeof(TaikouCampaign), 4, (IObjectResolver)null);   // 时代基类
			AddClassDefinition(typeof(LivingWorldCampaign), 2, (IObjectResolver)null);
		}
	}
}
