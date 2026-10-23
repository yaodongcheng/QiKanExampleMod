using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 钩索的**冒泡反馈**（2026-10-23 立，用户口径）：
	///   ① **当事人**（被钩中 / 被拉紧 / 被拖 / 被扛 / 被放上马 / 被从马背拿下 / 被松开）说一句；
	///   ② **看见这件事的人**也说一句（距离 + 视野 + 遮挡由 <see cref="NpcSightSystem"/> 判）；
	///   ③ **玩家自己永不冒泡**（移除的是面板里那几句玩家台词）；
	///   ④ **不当犯罪处理** —— 这里只出声音，不记账、不报警、不广播 WitnessCrime。
	///
	/// 台词选择 = **按说话者特征挑**：职业（`ReactiveAgent.ClassifyOccupation`）、
	///   是否玩家队伍成员（`FriendlinessHelper.IsPlayerPartyMember`）、性格标签（`ReactivePersonality` 的
	///   Temper/Duty/Social/Greed/Gullibility 分档）。**条件越具体越优先**，通用行永远保底。
	///   数据 = `ModuleData/DesignData/GrappleBarks.csv`（八列：ID/Event/Role/Relation/Occupation/Trait/Weight/TextKey）。
	///   加台词 = 加一行 + 一条语言键（英文层 + `CNs/` 中文层），**不改代码**。
	///
	/// 🔴 发声走既有轮子 <see cref="SpeechChannel"/>（唯一出口、队列/优先级/LLM 润色都在它里面）：
	///   · **当事人 = 被直接操作的那位 → LLM 优先，离线台词兜底**（`SayPolished`，无 LLM 时零延迟播模板）；
	///     🔴 润色 prompt 要求「≤15 个汉字 / 英文 ≤10 词、脱口而出」——**这是对 LLM 的要求，不是事后否决**：
	///     拿到什么就播什么，超长也不丢（用户 2026-10-23 口径）；模板侧由体检卡 ≤15 字。
	///   · **旁观者 → 只播模板**（省额度；用户口径 = LLM 优先给直接交互者）；
	///   · 两者都 `forwardToNearby: false` —— 只冒泡、不往 IM 附近频道灌环境话。
	///
	/// 防刷屏：当事人同一类事件 8 s、全局同一类事件 6 s、同一个旁观者 25 s；
	///   同一当事人两次走 LLM 之间至少 6 s（连着的第二个动作直接用模板）。
	/// 这些值都能用 `custom.grapple bark …` 热调（重启复位）。
	/// </summary>
	public static class GrappleBarks
	{
		/// <summary>冒泡时机（CSV 的 `Event` 列取值，大小写不敏感）。</summary>
		public enum BarkEvent { Hit, Tug, Drag, Carry, Horse, HorseOff, Release }

		// ───────────────────────────── 旋钮（命令热调；重启复位）─────────────────────────────

		/// <summary>总开关（`custom.grapple bark on|off`）。</summary>
		public static bool Enabled = true;

		/// <summary>旁观者半径（米）—— 与交互距离同档，别让半条街都来围观。</summary>
		public static float WitnessRadius = 12f;

		/// <summary>旁观者视野锥（度）：背对着的人不算"看见"。</summary>
		public static float WitnessFovDegrees = 120f;

		/// <summary>同一类事件两次"旁观者"反馈的最小间隔（秒，全局）——战场多人被钩时防刷屏。</summary>
		public static float WitnessEventGapSeconds = 6f;

		/// <summary>同一个旁观者两次开口的最小间隔（秒）。</summary>
		public static float WitnessPerAgentGapSeconds = 25f;

		/// <summary>同一个当事人**同一类事件**两次开口的最小间隔（秒）——状态机反复进同一拍时防刷屏。
		/// 🔴 按「人 × 事件」计：命中 → 拉紧 → 拖行 是三个不同动作，几秒内连着发生都**该各说一句**。</summary>
		public static float VictimEventGapSeconds = 8f;

		/// <summary>同一个当事人两次「走 LLM 润色」的最小间隔（秒）。连着的第二个动作直接用模板 ——
		/// 既省额度，也避免润色在途时排队导致台词晚到。</summary>
		public static float PolishMinGapSeconds = 6f;

		// ───────────────────────────── 数据 ─────────────────────────────

		private sealed class Line
		{
			public string Key;
			public int Weight;
			public string Relation = "Any";                     // Any / Party / Stranger
			public HashSet<string> Occupations;                 // 空 = 不限（guard/villager/drunkard/…）
			public HashSet<string> Traits;                      // 空 = 不限（hot/timid/dutiful/…）
			public int Score;                                   // 条件列里非 Any 的个数 —— 越具体越优先
		}

		/// <summary>说话者画像（职业 / 是否玩家队伍成员 / 主导性格标签）——选台词用。</summary>
		private sealed class SpeakerProfile
		{
			public string Occupation = "default";
			public bool Party;
			public readonly HashSet<string> Traits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		private static readonly Dictionary<BarkEvent, List<Line>> _victimLines = new Dictionary<BarkEvent, List<Line>>();
		private static readonly Dictionary<BarkEvent, List<Line>> _witnessLines = new Dictionary<BarkEvent, List<Line>>();
		private static bool _loaded;
		private static bool _missingLogged;
		private static int _victimCount;
		private static int _witnessCount;

		// ───────────────────────────── 防刷屏状态（换场景清空）─────────────────────────────

		private static readonly Dictionary<long, float> _victimLast = new Dictionary<long, float>();
		private static readonly Dictionary<int, float> _victimAnyLast = new Dictionary<int, float>();   // 上次"开口"（任何事件），LLM 润色节流用
		private static readonly Dictionary<int, float> _witnessLast = new Dictionary<int, float>();
		private static readonly Dictionary<BarkEvent, float> _witnessEventLast = new Dictionary<BarkEvent, float>();
		private static readonly Dictionary<int, string> _lastLine = new Dictionary<int, string>();
		private static Mission _mission;
		private static readonly Random _rng = new Random();

		// ───────────────────────────── 对外：七个时机 ─────────────────────────────

		/// <summary>钩索命中一个人（缠开始的那一刻；骑马目标是"被拽下马"那一刻）。</summary>
		public static void OnHit(Agent victim, Agent attacker) => Fire(BarkEvent.Hit, victim, attacker);

		/// <summary>玩家按【拉紧】（<see cref="GrappleBind.StartYank"/> 进拽倒那一段）。</summary>
		public static void OnTug(Agent victim, Agent attacker) => Fire(BarkEvent.Tug, victim, attacker);

		/// <summary>被拖在地上（绳主骑马/跑太快，趴着被拖）。</summary>
		public static void OnDragged(Agent victim, Agent attacker) => Fire(BarkEvent.Drag, victim, attacker);

		/// <summary>被扛起来。</summary>
		public static void OnCarried(Agent carrier, Agent carried) => Fire(BarkEvent.Carry, carried, carrier);

		/// <summary>被放上马背（<paramref name="horse"/> = 那匹马；当前台词不用它，留给以后"马怎么样"的变体）。</summary>
		public static void OnPutOnHorse(Agent carrier, Agent carried, Agent horse) => Fire(BarkEvent.Horse, carried, carrier);

		/// <summary>从马背被拿下。</summary>
		public static void OnTakenOffHorse(Agent carrier, Agent carried) => Fire(BarkEvent.HorseOff, carried, carrier);

		/// <summary>被松开（玩家主动放人）。</summary>
		public static void OnReleased(Agent victim, Agent releaser) => Fire(BarkEvent.Release, victim, releaser);

		// ───────────────────────────── 核心 ─────────────────────────────

		private static void Fire(BarkEvent evt, Agent victim, Agent actor)
		{
			try
			{
				if (!Enabled) return;
				Mission mission = Mission.Current;
				if (mission == null || victim == null) return;
				EnsureLoaded();
				ResetIfNewMission(mission);
				float now = mission.CurrentTime;

				SayVictim(evt, victim, actor, now);
				SayWitnesses(evt, victim, actor, now);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Bark] 冒泡触发异常（{evt}）：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void SayVictim(BarkEvent evt, Agent victim, Agent actor, float now)
		{
			// 🔴 玩家不冒泡（用户 2026-10-23 口径）—— 玩家当当事人（反向绑）也不说。
			if (victim == Agent.Main) return;
			if (!AgentControlHelper.SafeIsActive(victim)) return;

			long key = ((long)victim.Index << 8) | (byte)evt;
			if (_victimLast.TryGetValue(key, out float last) && now - last < VictimEventGapSeconds) return;

			Line line = Pick(_victimLines, evt, victim);
			if (line == null) return;
			string text = LWNTextHelper.ResolveText(line.Key);
			if (string.IsNullOrWhiteSpace(text)) return;
			_victimLast[key] = now;

			// 🔴 直接交互者 → **LLM 优先，模板兜底**（用户 2026-10-23 口径）：
			//    SayPolished 内部先查 LLM 是否配置/开关；没配置直接播模板（零延迟，铁律 1）。
			//    连着的第二个动作（< PolishMinGapSeconds）直接走模板：省额度、也避免排队晚到。
			bool recentPolish = _victimAnyLast.TryGetValue(victim.Index, out float t) && now - t < PolishMinGapSeconds;
			_victimAnyLast[victim.Index] = now;

			SpeechContext ctx = MakeContext(victim, actor, evt);
			if (recentPolish)
			{
				SpeechChannel.Say(victim, text, SpeechPriority.Chat, ctx, 0f, forwardToNearby: false);
			}
			else
			{
				SpeechChannel.SayPolished(victim, text, SpeechPriority.Chat, ctx,
					budgetS: 1.3f, forwardToNearby: false, fallbackWhenBusy: true);
			}
		}

		private static void SayWitnesses(BarkEvent evt, Agent victim, Agent actor, float now)
		{
			if (victim == null) return;
			if (!_witnessLines.ContainsKey(evt)) return;   // 这一类没写旁观台词 = 不反应
			if (_witnessEventLast.TryGetValue(evt, out float last) && now - last < WitnessEventGapSeconds) return;
			// 🔴 先记账再扫描：扫一轮要逐人做遮挡射线，没人合格时也别让下一次事件立刻重扫。
			_witnessEventLast[evt] = now;

			// 看见"这件事发生的地方"的人：距离 + 高度 + 视野锥 + 遮挡（NpcSightSystem 现成轮子）
			List<Agent> observers = NpcSightSystem.GetObserversOf(victim, WitnessRadius, WitnessFovDegrees);
			if (observers == null || observers.Count == 0) return;

			// 蓄水池抽样：一遍过选一个，且天然随机（同一时刻一般只让一个人开口）
			Agent pick = null;
			int candidates = 0;
			for (int i = 0; i < observers.Count; i++)
			{
				Agent a = observers[i];
				if (a == null || a == Agent.Main) continue;                 // 玩家不冒泡
				if (ReferenceEquals(a, victim) || ReferenceEquals(a, actor)) continue;
				if (!AgentControlHelper.SafeIsActive(a)) continue;
				if (AgentBrain.IsKnockedOut(a)) continue;                    // 昏迷的人不说话
				if (GrappleBind.IsTargetClaimed(a)) continue;                // 被绑着的人不围观
				if (_witnessLast.TryGetValue(a.Index, out float t) && now - t < WitnessPerAgentGapSeconds) continue;
				candidates++;
				if (_rng.Next(candidates) == 0) pick = a;
			}
			if (pick == null) return;

			Line line = Pick(_witnessLines, evt, pick);
			if (line == null) return;
			Say(pick, line, evt, victim);
			_witnessLast[pick.Index] = now;
		}

		/// <summary>语境：刺激 = `grapple_<event>`（LLM prompt 的「当前处境」段依据，见 SpeechChannel.BuildSituationLine）。
		/// 🔴 `ShortBark = true`：润色走「极短」硬规则（≤15 个汉字），超长结果作废、用模板兜底。</summary>
		private static SpeechContext MakeContext(Agent speaker, Agent source, BarkEvent evt)
		{
			SpeechContext ctx = SpeechContext.FromBrain(
				AgentAIController.GetBrainForAgent(speaker), source,
				"grapple_" + evt.ToString().ToLowerInvariant());
			ctx.ShortBark = true;
			return ctx;
		}

		/// <summary>旁观者开口（**只走模板**：用户口径 = LLM 优先给"被直接操作的那位"）。</summary>
		private static void Say(Agent speaker, Line line, BarkEvent evt, Agent source)
		{
			if (speaker == null || line == null) return;
			string text = LWNTextHelper.ResolveText(line.Key);
			if (string.IsNullOrWhiteSpace(text)) return;

			// 🔴 forwardToNearby: false —— 纯冒泡，不进 IM 附近频道（用户口径："单纯的 bubblesay"）
			SpeechChannel.Say(speaker, text, SpeechPriority.Chat, MakeContext(speaker, source, evt),
				0f, forwardToNearby: false);
		}

		/// <summary>
		/// 按「说话者特征」选一句：先只保留匹配的行（关系 / 职业 / 性格），
		/// 再取**条件最具体的一档**（非 Any 列越多越优先），同档内按权重随机；同一个人连续两次不重复。
		/// 没有完全匹配的行时逐级回落到通用行（CSV 保证每类事件都有通用行）。
		/// </summary>
		private static Line Pick(Dictionary<BarkEvent, List<Line>> table, BarkEvent evt, Agent speaker)
		{
			if (speaker == null || table == null) return null;
			if (!table.TryGetValue(evt, out List<Line> pool) || pool == null || pool.Count == 0) return null;

			SpeakerProfile profile = ProfileOf(speaker);

			int bestScore = -1;
			for (int i = 0; i < pool.Count; i++)
			{
				Line l = pool[i];
				if (Matches(l, profile) && l.Score > bestScore) bestScore = l.Score;
			}
			if (bestScore < 0) return null;

			int total = 0;
			for (int i = 0; i < pool.Count; i++)
			{
				Line l = pool[i];
				if (l.Score == bestScore && Matches(l, profile)) total += Math.Max(1, l.Weight);
			}
			if (total <= 0) return null;

			_lastLine.TryGetValue(speaker.Index, out string avoid);

			for (int attempt = 0; attempt < 3; attempt++)
			{
				int roll = _rng.Next(total);
				for (int i = 0; i < pool.Count; i++)
				{
					Line l = pool[i];
					if (l.Score != bestScore || !Matches(l, profile)) continue;
					roll -= Math.Max(1, l.Weight);
					if (roll >= 0) continue;
					if (l.Key == avoid) break;                 // 和上一句一样 ⇒ 这次重摇
					_lastLine[speaker.Index] = l.Key;
					return l;
				}
			}

			// 兜底：同档里第一条匹配的（避免"只因为和上一句相同就整句不说"）
			for (int i = 0; i < pool.Count; i++)
			{
				Line l = pool[i];
				if (l.Score == bestScore && Matches(l, profile))
				{
					_lastLine[speaker.Index] = l.Key;
					return l;
				}
			}
			return null;
		}

		/// <summary>说话者特征（全部来自既有轮子：职业分类 / 队伍成员判定 / 人格参数）。</summary>
		private static SpeakerProfile ProfileOf(Agent a)
		{
			var p = new SpeakerProfile();
			try
			{
				string occ = ReactiveAgent.ClassifyOccupation(a);
				if (!string.IsNullOrEmpty(occ)) p.Occupation = occ;
			}
			catch { }
			try { p.Party = FriendlinessHelper.IsPlayerPartyMember(a); } catch { }
			try
			{
				ReactivePersonality pers = ReactiveAgent.Get(a)?.Personality;
				if (pers != null)
				{
					// 阈值只用来"分档"，不是精确判定：同一职业默认人格不同 ⇒ 天然分成几档
					if (pers.Temper >= 0.6f) p.Traits.Add("hot");
					if (pers.Temper <= 0.35f) p.Traits.Add("timid");
					if (pers.Duty >= 0.7f) p.Traits.Add("dutiful");
					if (pers.Social >= 0.65f) p.Traits.Add("social");
					if (pers.Greed >= 0.6f) p.Traits.Add("greedy");
					if (pers.Gullibility >= 0.7f) p.Traits.Add("naive");
				}
			}
			catch { }
			return p;
		}

		/// <summary>这一行台词是否适用于该说话者（Any / 空 = 不限制；多值取"任一命中"）。</summary>
		private static bool Matches(Line line, SpeakerProfile p)
		{
			if (line == null || p == null) return false;
			if (line.Relation.Equals("Party", StringComparison.OrdinalIgnoreCase) && !p.Party) return false;
			if (line.Relation.Equals("Stranger", StringComparison.OrdinalIgnoreCase) && p.Party) return false;
			if (line.Occupations != null && line.Occupations.Count > 0 && !line.Occupations.Contains(p.Occupation)) return false;
			if (line.Traits != null && line.Traits.Count > 0 && !line.Traits.Overlaps(p.Traits)) return false;
			return true;
		}

		// ───────────────────────────── 数据装载 ─────────────────────────────

		/// <summary>多值列（`|` 分隔）：type 写成 list 时直接拿 GetList；写成 string 也能解析。</summary>
		private static HashSet<string> ParseSet(DynamicRecord rec, string column)
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				List<string> raw = rec.GetList(column);
				if (raw != null && raw.Count > 0)
				{
					foreach (string s in raw) AddToken(set, s);
				}
				else
				{
					string text = rec.GetString(column, "");
					foreach (string s in (text ?? "").Split('|')) AddToken(set, s);
				}
			}
			catch { }
			return set;
		}

		private static void AddToken(HashSet<string> set, string raw)
		{
			string s = (raw ?? "").Trim();
			if (s.Length == 0) return;
			if (s.Equals("Any", StringComparison.OrdinalIgnoreCase)) return;
			set.Add(s);
		}

		private static void EnsureLoaded()
		{
			if (_loaded) return;
			try
			{
				DataTable table = GameDatabase.GrappleBarks;
				if (table == null)
				{
					if (!_missingLogged)
					{
						_missingLogged = true;
						DebugLogger.Log("[Bark] 台词表未加载（GameDatabase.GrappleBarks = null）—— 冒泡不生效"
							+ "（检查 ModuleData/DesignData/GrappleBarks.csv）");
					}
					return;   // 不置 _loaded：表晚一点就绪时还能补上
				}

				foreach (DynamicRecord rec in table.GetAll())
				{
					if (rec == null) continue;
					string evtName = rec.GetString("Event");
					string role = rec.GetString("Role");
					string key = rec.GetString("TextKey");
					int weight = rec.GetInt("Weight", 1);
					if (!TryParseEvent(evtName, out BarkEvent evt) || string.IsNullOrWhiteSpace(key))
					{
						DebugLogger.Log($"[Bark] 略过一行（Event='{evtName}' Role='{role}' TextKey='{key}'）");
						continue;
					}

					// 说话者特征条件（可选列）：Relation 单值；Occupation / Trait 用 `|` 分隔多值
					string relation = (rec.GetString("Relation", "Any") ?? "Any").Trim();
					if (!relation.Equals("Any", StringComparison.OrdinalIgnoreCase)
						&& !relation.Equals("Party", StringComparison.OrdinalIgnoreCase)
						&& !relation.Equals("Stranger", StringComparison.OrdinalIgnoreCase))
					{
						DebugLogger.Log($"[Bark] 行 '{rec.GetString("ID")}' 的 Relation='{relation}' 不认识 ⇒ 按 Any 处理");
						relation = "Any";
					}
					HashSet<string> occ = ParseSet(rec, "Occupation");
					HashSet<string> traits = ParseSet(rec, "Trait");
					int score = 0;
					if (!relation.Equals("Any", StringComparison.OrdinalIgnoreCase)) score++;
					if (occ.Count > 0) score++;
					if (traits.Count > 0) score++;
					bool witness;
					string roleTrim = (role ?? "").Trim();
					if (roleTrim.Equals("Bystander", StringComparison.OrdinalIgnoreCase)) witness = true;
					else if (roleTrim.Equals("Victim", StringComparison.OrdinalIgnoreCase)) witness = false;
					else
					{
						DebugLogger.Log($"[Bark] 略过一行（Role='{role}' 只认 Victim / Bystander；TextKey='{key}'）");
						continue;
					}
					Dictionary<BarkEvent, List<Line>> dst = witness ? _witnessLines : _victimLines;
					if (!dst.TryGetValue(evt, out List<Line> list))
					{
						list = new List<Line>();
						dst[evt] = list;
					}
					list.Add(new Line
					{
						Key = key.Trim(),
						Weight = Math.Max(1, weight),
						Relation = relation,
						Occupations = occ,
						Traits = traits,
						Score = score,
					});
					if (witness) _witnessCount++; else _victimCount++;
				}

				_loaded = true;
				DebugLogger.Log($"[Bark] 台词表已载入：当事人 {_victimCount} 条 / 旁观者 {_witnessCount} 条"
					+ $"（{_victimLines.Count} + {_witnessLines.Count} 类事件）");
			}
			catch (Exception ex)
			{
				_loaded = true;
				DebugLogger.Log($"[Bark] 台词表加载失败（冒泡关闭）：{ex.GetType().Name}: {ex.Message}");
			}
		}

		/// <summary>CSV 的 `Event` 列 → 枚举（命令解析也用它）。</summary>
		public static bool TryParseEvent(string name, out BarkEvent evt)
		{
			evt = BarkEvent.Hit;
			if (string.IsNullOrWhiteSpace(name)) return false;
			switch (name.Trim().ToLowerInvariant())
			{
				case "hit": evt = BarkEvent.Hit; return true;
				case "tug": evt = BarkEvent.Tug; return true;
				case "drag": evt = BarkEvent.Drag; return true;
				case "carry": evt = BarkEvent.Carry; return true;
				case "horse": evt = BarkEvent.Horse; return true;
				case "horseoff": evt = BarkEvent.HorseOff; return true;
				case "release": evt = BarkEvent.Release; return true;
				default: return false;
			}
		}

		private static void ResetIfNewMission(Mission mission)
		{
			if (ReferenceEquals(_mission, mission)) return;
			_mission = mission;
			_victimLast.Clear();
			_victimAnyLast.Clear();
			_witnessLast.Clear();
			_witnessEventLast.Clear();
			_lastLine.Clear();
		}

		// ───────────────────────────── 状态（命令用）─────────────────────────────

		/// <summary>清空防刷屏计时（`custom.grapple bark test` 用：测试永远能立刻听到）。</summary>
		public static void ResetCooldowns()
		{
			_victimLast.Clear();
			_victimAnyLast.Clear();
			_witnessLast.Clear();
			_witnessEventLast.Clear();
			_lastLine.Clear();
		}

		/// <summary>一行状态（纯英文：控制台回执，2026-09-07 用户裁定）。</summary>
		public static string Describe()
		{
			EnsureLoaded();
			string voice;
			try
			{
				voice = (Settings.Instance.IsLLMConfigured && Settings.Instance.PolishSpeechEnabled)
					? "LLM first (template fallback)"
					: "template only (LLM off)";
			}
			catch { voice = "unknown"; }
			return $"barks {(Enabled ? "ON" : "OFF")} | victim lines {_victimCount} / witness lines {_witnessCount}"
				+ $" | victim voice: {voice}"
				+ $" | witness radius {WitnessRadius:F0} m, fov {WitnessFovDegrees:F0} deg"
				+ $" | gaps: event {WitnessEventGapSeconds:F0}s, witness {WitnessPerAgentGapSeconds:F0}s, victim {VictimEventGapSeconds:F0}s, polish {PolishMinGapSeconds:F0}s";
		}
	}
}
