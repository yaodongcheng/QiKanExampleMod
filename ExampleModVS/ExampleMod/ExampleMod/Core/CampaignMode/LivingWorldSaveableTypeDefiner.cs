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
			AddClassDefinition(typeof(TaikouCampaign), 1, (IObjectResolver)null);
			AddClassDefinition(typeof(LivingWorldCampaign), 2, (IObjectResolver)null);
		}
	}
}
