using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 🔴 临时诊断（2026-09-23，**定案即删**）：抓「谁把 GameTexts 清了 / 谁销毁了 Campaign」。
	///
	/// 背景（实机崩溃）：1.3.15 建号流程点「完成」→ 引擎要建「叙事阶段」界面
	///   → `CampaignUIHelper` 静态构造 → `GameTexts.FindText` → `_gameTextManager` 为 null → NRE。
	/// 反编译实证两条：
	///   · 设置点 = `Game.Initialize()` 里的 `GameTexts.Initialize(GameTextManager)`（整个进程一辈子就这一次）；
	///   · 清空点 = `Campaign.OnDestroy()` 里的 `GameTexts.ClearInstance()`。
	///   ⇒ 结论：**本局里有一个 Game 被结束了**。但没人知道是谁触发的（我们的补丁？第三方 mod？引擎自身？）
	///     → 本补丁只打日志 + 打调用栈，**不改任何行为**，用来一次定位。
	///
	/// 判读（运行日志搜 `[Lifecycle]`）：
	///   · `OnDestroy` / `ClearInstance` 两段调用栈里，**第一个「非引擎非 Harmony」的帧**就是元凶候选；
	///   · 若 `Initialize` 那行压根没出现 → 是另一条线（`Game.Initialize` 没跑完）。
	///   · 正常一局（不开新档）应当只在启动时看到一条 `Initialize`，没有任何 `OnDestroy`/`ClearInstance`。
	///
	/// 删法：删掉 csproj 里那一行 `Compile Include="Debug\CampaignLifecycleDiag.cs"`，再删本文件。
	/// </summary>
	internal static class CampaignLifecycleDiag
	{
		private static void Log(string what)
		{
			try
			{
				DebugLogger.Log($"[Lifecycle] {what}\n---- 调用栈 ----\n{Environment.StackTrace}---- 栈完 ----");
			}
			catch
			{
				// 诊断绝不能影响游戏运行
			}
		}

		/// <summary>游戏结束（Campaign : GameType，引擎销毁 GameType 时走这里）——GameTexts 就在这被清空。</summary>
		[HarmonyPatch(typeof(Campaign), "OnDestroy")]
		internal static class CampaignOnDestroyPatch
		{
			[HarmonyPrefix]
			private static void Prefix()
			{
				Log("Campaign.OnDestroy 被调用（GameTexts 即刻被清空 → 此后任何 GameTexts.FindText 都会 NRE）");
			}
		}

		/// <summary>清空点本体（万一还有别的调用方）。</summary>
		[HarmonyPatch(typeof(GameTexts), "ClearInstance")]
		internal static class GameTextsClearPatch
		{
			[HarmonyPrefix]
			private static void Prefix()
			{
				Log("GameTexts.ClearInstance 被调用");
			}
		}

		/// <summary>设置点本体（确认它到底跑没跑、拿到的是不是 null）。</summary>
		[HarmonyPatch(typeof(GameTexts), "Initialize")]
		internal static class GameTextsInitPatch
		{
			[HarmonyPostfix]
			private static void Postfix(GameTextManager gameTextManager)
			{
				Log($"GameTexts.Initialize 被调用（传入的 GameTextManager 为 null = {gameTextManager == null}）");
			}
		}

		/// <summary>1.3.15 原版建号启动点：`CleanAndPushState(CreateState&lt;CharacterCreationState&gt;(), 0)` —— **清空整个状态栈**，
		/// 而且**反编译实锤它没有任何守卫**（方法全文就两行）。被调 &gt;1 次 = 建号被反复启动 / 状态栈被反复清空。</summary>
		[HarmonyPatch(typeof(SandBox.SandBoxGameManager), "LaunchSandboxCharacterCreation")]
		internal static class LaunchCharCreationProbe
		{
			private static int _count;

			[HarmonyPrefix]
			private static void Prefix()
			{
				_count++;
				Log($"LaunchSandboxCharacterCreation 第 {_count} 次（>1 = 建号被启动了多次，状态栈被 Clean 多次）");
			}
		}

		/// <summary>引擎的 `OnLoadFinished` **没有"完成"守卫**（本项目 2026-09-10 实机验证过：只要 loading 状态还活着就每帧都调）。
		/// 计数飙升 = 本项目自己踩过的那个坑在 1.3.15 上重演。</summary>
		[HarmonyPatch(typeof(SandBox.SandBoxGameManager), "OnLoadFinished")]
		internal static class SandboxOnLoadFinishedProbe
		{
			private static int _count;

			[HarmonyPrefix]
			private static void Prefix()
			{
				_count++;
				if (_count <= 5 || _count % 30 == 0)
				{
					Log($"SandBoxGameManager.OnLoadFinished 第 {_count} 次（每帧被调 = 本值飙升，当前 ActiveState={GameStateManager.Current?.ActiveState?.GetType().Name ?? "null"}）");
				}
			}
		}

		/// <summary>
		/// 🔴 票决探针（2026-09-23）：**只在 `_gameTextManager` 变成 null 的那一刻记一次**，并转储所有已加载的
		/// `TaleWorlds.Core*` 程序集与它们的物理路径。
		///
		/// 为什么需要它：前五个探针给出的是**互相矛盾**的结论——`Initialize` 调过且传入非 null、
		/// `ClearInstance` 从没调过、`Campaign.OnDestroy` 从没调过，可 `FindText` 还是 NRE。
		/// 而 `GameTexts.FindText` 全文只有一个裸解引用（`_gameTextManager`），`GameTextManager` 那侧全是 null-safe。
		/// ⇒ 唯一能同时成立的解释 = **进程里存在两份 `GameTexts` 静态状态**（反射写、或第二份 Core 副本）。
		/// 本探针把这个判断摆到台面上：null 出现的那一刻 + 程序集清单 = 谁在说话。
		/// 只在 null 时记录（正常时只多一次 bool 判断），诊断完即删。
		/// </summary>
		[HarmonyPatch(typeof(GameTexts), "FindText")]
		internal static class GameTextsFindNullProbe
		{
			private static bool _logged;
			private static System.Reflection.FieldInfo _field;

			[HarmonyPrefix]
			private static void Prefix()
			{
				if (_logged)
				{
					return;
				}
				try
				{
					if (_field == null)
					{
						_field = AccessTools.Field(typeof(GameTexts), "_gameTextManager");
					}
					if (_field != null && _field.GetValue(null) != null)
					{
						return; // 正常状态：不记（FindText 调用极频繁，不能刷日志）
					}

					_logged = true;
					var cores = new System.Text.StringBuilder();
					int total = 0;
					foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
					{
						total++;
						try
						{
							string n = a.GetName().Name;
							if (n != null && n.StartsWith("TaleWorlds.Core"))
							{
								cores.Append("\n    · ").Append(a.GetName().FullName).Append("  @ ").Append(a.Location);
							}
						}
						catch { /* 动态程序集取不到 Location，忽略 */ }
					}
					if (cores.Length == 0)
					{
						cores.Append("\n    （一个都没枚举到？）");
					}
					Log($"_gameTextManager 读到 NULL（反射读私有静态字段，field 解析={( _field != null ? "成功" : "失败")}）"
						+ $"\n  typeof(GameTexts).Assembly.Location = {typeof(GameTexts).Assembly.Location}"
						+ $"\n  进程内已加载程序集总数 = {total}"
						+ $"\n  其中 TaleWorlds.Core* ：{cores}");
				}
				catch
				{
					// 诊断绝不能影响游戏
				}
			}
		}
	}
}
