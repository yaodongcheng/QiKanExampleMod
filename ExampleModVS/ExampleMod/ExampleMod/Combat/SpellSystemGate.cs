using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 法术体系总闸（2026-10-12 用户裁定）—— **未发布功能，默认关**。
	///
	/// 用户原话：「法术体系搞了很多道具，没经过测试不可以发布。你看看用什么办法搞个开关先关闭呢。
	/// 不只是功能，这些道具也不应该看到。」
	///
	/// 🔴 **关着的时候，7 件法术物品是「根本不存在」，不是「藏起来」** ——
	///   靠的是 `SubModule.xml` **故意不声明** `ModuleData/items/spells.xml`
	///   （那儿原来是整目录 `path="items"`，引擎会把该目录下每个 xml 都当物品加载，无条件进）。
	///   ⇒ 引擎从头到尾没看见这个文件 ⇒ 游戏里也就查不到这 7 件东西（背包 / 商店 / 控制台全无）。
	///   ⇒ 开关打开时，由本类在**每次开局**把它按需注册进去（见 <see cref="EnsureItemsLoaded"/>）。
	///
	/// 🔴 **为什么走 `LoadOneXmlFromFile`**：它是引擎公开 API，与引擎自己加载物品是**同一条反序列化路径**
	///   （解析文档 → 按根元素名 `Items` 找类型注册表 → `GetPresumedObject` → `Deserialize` → `AfterInitialized`），
	///   连 XSD 校验都能照传（`<游戏根>/XmlSchemas/Items.xsd`），不给「开关打开后校验变松」留口子。
	///   四个目标版本（1.2.12 / 1.3.15 / 1.4.x / 1.5.x）已逐一反编译核实该 API 与 `GetXsdPath` 都在。
	///
	/// ⚠️ **该 API 内部 try/catch 静默吞异常**（反编译实证）—— 所以加载完**必须自己验证**：
	///   否则「路径写错 / XML 坏了 / 时机不对」都会表现成「开关打开了但物品没出来」，日志里还一声不吭。
	///
	/// 🔴🔴 **时机（这里是本类最容易写错的地方，改动前先读完整段）**：
	///   ① `MBObjectManager` 要到「有游戏在跑」才存在（`Game.CreateGame` 里才 `Init()`，主菜单阶段是 null）；
	///   ② 更要命的是 **`BasicModels` 必须已经就位** —— `ItemObject.Deserialize` 结尾会把价格交给
	///      `Game.Current.BasicModels.ItemValueModel`（`ItemObject.DetermineValue`，`BasicModels` 是**裸解引用**），
	///      而两个物品都没写 `value=` 属性 ⇒ 一定会走那条路。
	///      反编译实证：各 GameType 的 `SetBasicModels` 就在 `GameManager.OnGameStart(...)` 的**下一行**
	///      （`Campaign.OnInitialize` / `CustomGame.OnInitialize` / `EditorGame.OnInitialize` 三处同形）
	///      ⇒ **`OnGameStart` 里加载 = 必 NRE**（被引擎静默吞掉，症状是"只注册了第 1 件、且它还是半成品"）。
	///   ③ 所以注册点选在**物品已被引擎加载、且模型已就位之后**，两条路各一个钩子（都在 `Core/MySubModule.cs`）：
	///      · 战役（新档 / 读档 / 教程）→ `AfterRegisterSubModuleObjects(bool)`
	///        —— 它由 `SandBoxManager.OnCampaignStart` 调起，位置正好在 `Campaign.OnInitialize` 里
	///        「`LoadXML("Items")` 之后、`UnregisterNonReadyObjects()` **之前**」。
	///        🔴 「之前」这一条是**读档连续性**的关键：读档时存档里的物品实例先被恢复但 `IsReady=false`，
	///        我们若晚于那次清理才注册，它们会被当垃圾注销、背包里那几件随后被换成破烂
	///        （`ItemRoster.ReplaceInvalidItemsWithTrash`）。卡在那个窗口里注册，旧实例会被重新反序列化 → 保住。
	///      · 自定义战斗 / 编辑器 / 其它 GameType → `OnGameInitializationFinished(Game)`
	///        （`CustomGame.OnInitialize` 的最后一步，那时 Items 与模型都已就位）。
	///   ④ `SpellProjectileLogic` 构造里再兜一次（最后的保险，见那边注释）—— 幂等，先到先做。
	///
	/// ⚠️ **状态必须绑对象管理器实例**：`MBObjectManager.Init()` **每次开局都 new 一个**
	///   ⇒「退主菜单再开一局」时，上一局的"已加载 / 已失败"标志必须作废，否则第二局会跳过注册
	///   （物品不在，日志却说 loaded）。所以下面存的是**实例引用**，不是裸 bool。
	///
	/// 🔴 **改开关要重启游戏**：物品注册发生在每次开局；中途热改（`custom.input_reload`）只会得到
	///   「配置关了、这一局的法术还在跑」的中间态。`custom.spell` / `custom.spell_gate` 会把
	///   **配置值 / 物品在不在 / 本场景还有没有法术宿主** 分开报，一眼可判，不让人猜。
	///
	/// 关掉时连带不挂的四件（都在 `Core/MySubModule.cs` 里判本闸）：施法管线 `SpellProjectileLogic` ·
	/// 飞行诊断 `SpellMissileTrace` · 宝石配装仓 `SpellLoadoutBehavior` · 法印开火拦截 `SpellSealFirePatch`。
	/// **不受影响**：钩索 / 飞行 / 相机 —— 它们借用了法术命名空间下的通用工具（读骨、解析网格），那些一行没动。
	/// </summary>
	public static class SpellSystemGate
	{
		/// <summary>法术物品定义文件（相对模块根）。**唯一权威 = 本处**（`SubModule.xml` 的注释指向这里）。</summary>
		public const string ItemXmlRelativePath = "ModuleData/items/spells.xml";

		/// <summary>7 件法术物品的 StringId（加载后逐件验证用；铁律 20：引用一律用 StringId，不用显示名）。</summary>
		public static readonly string[] ItemIds =
		{
			"lwn_spell_seal",     // 法印（施法武器，兼宝石手环）
			"lwn_spell_crescent", // 阴魔斩
			"lwn_spell_multi",    // 多重法弹
			"lwn_spell_sky",      // 天降法弹
			"lwn_spell_zone",     // 焚地法阵
			"lwn_spell_beam",     // 引导光束
			"lwn_spell_aura",     // 雷电领域
		};

		/// <summary>已把物品注册进**哪个**对象管理器（每次开局都是新实例 ⇒ 状态跟着实例走，见文件头）。</summary>
		private static MBObjectManager _loadedInto;

		/// <summary>在**哪个**实例上加载失败过（同一个实例内不再重试，防每场战斗重试 + 刷屏）。</summary>
		private static MBObjectManager _failedInto;

		private static bool _startupLogged;

		/// <summary>
		/// 总闸是否打开。唯一来源 = config.json 的 <c>SpellSystemEnabled</c>（**不进 MCM**）。
		/// 读的时候兜异常：<c>Settings.Instance</c> 的构造会碰本地化层，理论上可能抛 ——
		/// 铁律 1 的要求是"任何配置问题都不能让游戏崩"，这里同理，且**兜底方向必须是"关"**。
		/// </summary>
		public static bool Enabled
		{
			get
			{
				try
				{
					return Settings.Instance.SpellSystemEnabled;
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Spell] 读总闸开关失败（一律按「关闭」处理）：{ex.GetType().Name} {ex.Message}");
					return false;
				}
			}
		}

		/// <summary>
		/// 本局游戏里法术物品到底注册了没 —— **与 <see cref="Enabled"/> 分开报**（中间态一眼可判），
		/// 且绑当前对象管理器实例（跨局不会骗人）。
		/// </summary>
		public static bool ItemsLoaded
		{
			get
			{
				MBObjectManager manager = MBObjectManager.Instance;
				return manager != null && ReferenceEquals(_loadedInto, manager);
			}
		}

		/// <summary>物品定义文件的绝对路径。</summary>
		public static string ItemXmlFullPath
		{
			get { return ModuleHelper.GetModuleFullPath("LivingWorldNpcs") + ItemXmlRelativePath; }
		}

		/// <summary>当前已注册且**就绪**的法术物品件数（0~7）—— `custom.spell_gate` 的自证数据。</summary>
		public static int ReadyItemCount()
		{
			MBObjectManager manager = MBObjectManager.Instance;
			if (manager == null)
			{
				return 0;
			}
			int found = 0;
			for (int i = 0; i < ItemIds.Length; i++)
			{
				ItemObject item = manager.GetObject<ItemObject>(ItemIds[i]);
				// ⚠️ 光判 null 不够：反序列化中途抛异常的对象**已经注册**了但 `IsReady=false`
				//    （`LoadXml` 是先注册再 Deserialize 再 AfterInitialized）——
				//    这正是"只找到 1/7"那个坑的判据来源，所以必须连 IsReady 一起看。
				if (item != null && item.IsReady)
				{
					found++;
				}
			}
			return found;
		}

		/// <summary>
		/// 按需把法术物品注册进游戏（**幂等**，随便调）。总闸关着时什么都不做。
		/// </summary>
		public static void EnsureItemsLoaded()
		{
			if (!Enabled)
			{
				return;
			}
			MBObjectManager manager = MBObjectManager.Instance;
			if (manager == null)
			{
				return;   // 还没进游戏（主菜单阶段）：别的钩子会再来
			}
			if (ReferenceEquals(_loadedInto, manager))
			{
				return;   // 本局已注册（幂等）
			}
			if (ReferenceEquals(_failedInto, manager))
			{
				return;   // 本局失败过：不再重试（防刷屏），换一局才重来
			}

			// 🔴 环境就绪闸（见文件头「时机」②）：`BasicModels` 没就位时**不许**碰物品反序列化，
			//    否则 `ItemObject.DetermineValue` 裸解引用必 NRE、被引擎静默吞掉。
			//    ⚠️ 这里**不置失败标志**：没就绪 ≠ 加载失败，后面还有钩子会再来一次。
			Game game = Game.Current;
			if (game == null || game.BasicModels == null)
			{
				return;
			}

			string path = ItemXmlFullPath;
			if (!File.Exists(path))
			{
				_failedInto = manager;
				DebugLogger.Log($"[Spell] 总闸已开，但物品文件不存在：{path}"
					+ " —— SubModule.xml 故意不声明它（改由本类按需注册），文件没了 = 开关打开也没用");
				return;
			}

			try
			{
				// skipValidation = false + 引擎自己那张 XSD：与引擎平时加载物品的路径完全一致。
				manager.LoadOneXmlFromFile(path, ModuleHelper.GetXsdPath("Items"), false);
			}
			catch (Exception ex)
			{
				// 引擎内部已经吞过一层，这里只是兜底（真抛出来 = 引擎之外的意外）
				DebugLogger.Log($"[Spell] 注册法术物品抛异常：{ex.GetType().Name} {ex.Message}");
			}

			// 🔴 必须自己验证（那个 API 静默吞异常），且要连 `IsReady` 一起判（见 ReadyItemCount 的说明）
			int found = ReadyItemCount();
			if (found == ItemIds.Length)
			{
				_loadedInto = manager;
				DebugLogger.Log($"[Spell] 总闸已开（SpellSystemEnabled=true）：法术物品已注册 {found}/{ItemIds.Length} 件");
			}
			else
			{
				_failedInto = manager;
				DebugLogger.Log($"[Spell] 法术物品注册失败：只找到 {found}/{ItemIds.Length} 件就绪 ——"
					+ $" 去查 {path} 在不在、合不合法（引擎那个 API 会静默吞异常，这条是我们自己的验证）");
			}
		}

		/// <summary>
		/// 模块加载时打一行「总闸现在是开是关」（一次性）。
		/// 🔴 验收用：关着时把这轮**具体跳过了哪几件**一次写全，排查时不用挨个文件翻
		///    （判据见 plans/法术体系-通用施法框架.md 的「§零 总闸」一节）。
		/// </summary>
		public static void AnnounceAtStartup()
		{
			if (_startupLogged)
			{
				return;
			}
			_startupLogged = true;

			if (Enabled)
			{
				DebugLogger.Log("[Spell] 总闸已开（SpellSystemEnabled=true）：法术物品将在每次开局注册；"
					+ "施法管线 / 飞行诊断 / 宝石配装仓 / 法印开火拦截照常挂载");
			}
			else
			{
				DebugLogger.Log("[Spell] 总闸关闭（SpellSystemEnabled=false）→ 已跳过：法术物品注册（7 件，游戏里不存在）"
					+ " · SpellProjectileLogic · SpellMissileTrace · SpellLoadoutBehavior · SpellSealFirePatch"
					+ " · custom.spell / custom.spell_trace"
					+ "（随时可敲 custom.spell_gate 自证：它不受总闸影响）");
			}
		}

		/// <summary>
		/// 🔴 常开自证命令（2026-10-12）：**不受总闸影响**，随时能问"开关什么值 / 物品到底在不在"。
		///
		/// 为什么必须另开一条：`custom.spell` 在总闸关着时直接封掉（连状态都不给），
		/// 于是"关着的时候物品真的不在吗"就没人能自证了 —— 而**那正是本次改动的唯一目的**。
		/// 这条只看事实：开关值 · 7 件物品各就位几件 · 这个场景里还有没有法术宿主 · 定义文件在不在。
		///
		/// 用法（首参可弃，工作流约定）：`custom.spell_gate`
		/// 返回纯英文（游戏内控制台可见文本，2026-09-07 用户裁定）。
		/// </summary>
		[CommandLineFunctionality.CommandLineArgumentFunction("spell_gate", "custom")]
		public static string SpellGate(List<string> args)
		{
			try
			{
				bool enabled;
				try
				{
					enabled = Settings.Instance.SpellSystemEnabled;   // 现读配置（与 Enabled 的兜底分开，这里要报真值）
				}
				catch (Exception ex)
				{
					return "ERROR: cannot read config (" + ex.GetType().Name + ")";
				}

				MBObjectManager manager = MBObjectManager.Instance;
				int ready = ReadyItemCount();
				string file = File.Exists(ItemXmlFullPath) ? "present" : "MISSING";

				bool casterLive = false;
				try
				{
					casterLive = Mission.Current != null
						&& Mission.Current.GetMissionBehavior<SpellProjectileLogic>() != null;
				}
				catch
				{
					// 取不到就是没有（不因为一条诊断命令把游戏搞崩）
				}

				string expect = enabled
					? (ready == ItemIds.Length ? " (as expected when ON)" : " ** MISMATCH: expected 7 **")
					: (ready == 0 ? " (as expected when OFF)" : " ** MISMATCH: expected 0 **");
				string session = manager == null
					? "no-game(main menu)"
					: (ItemsLoaded ? "registered" : "not registered this game");
				string caster = casterLive
					? "live ** restart the game to unload it **"
					: "none";

				return $"OK: SpellSystemEnabled={enabled.ToString().ToLowerInvariant()}"
					+ $" | items {ready}/{ItemIds.Length} ready{expect}"
					+ $" | this-game={session}"
					+ $" | mission-caster={caster}"
					+ $" | items-file={file}";
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] spell_gate 命令异常：{ex.GetType().Name} {ex.Message}");
				return "ERROR: " + ex.GetType().Name + " (see log)";
			}
		}
	}
}
