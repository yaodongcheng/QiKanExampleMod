using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 太阁·**1582 转变**时代（时代切换 spike 的对照世界：京归 G 家，无织田家）。
	/// 🔴 类名 = 该时代的 GameType 字符串（引擎 `GameTypeStringId => GetType().Name`）→
	///   必须与 Taikou/SubModule.xml 的 &lt;GameType value="TaikouCampaign1582"/&gt; **逐字一致**。
	/// 本时代的数据来源（SubModule.xml 注册）：
	///   settlements_1582.xml / spclans_1582.xml / spkingdoms_1582.xml / taikou_heroes_1582.xml
	///   ← 由 Scripts/gen_taikou_era_diff.py 从基线套派生（铁律 22：改差异改生成器）
	///   其余段与 1560 **共用同一批文件**（跨时代无差异 → 不复制）。
	/// ⚠️ 与 1560 的差异段 GameType 互斥（同名 XmlName 多段 = 引擎合并加载 → 同名对象重复定义）。
	/// </summary>
	public class TaikouCampaign1582 : TaikouCampaign
	{
		public TaikouCampaign1582(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}
	}
}
