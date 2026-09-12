using System;
using System.Text;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.TwoDimension;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 角色详情页 ViewModel（配 <c>GUI/Prefabs/HeroDetail.xml</c>）。
	/// 复刻太阁5「选择角色 → 角色详情」那一步：**先看人，再决定**。
	///
	/// 三块数据面板：
	///   ① 基本情报 —— 身份 / 所属 / 据点，**全部来自选人目录（静态）**
	///   ② 能力情报 —— 五维，读画像表（<see cref="HeroProfileRegistry"/>）
	///   ③ 技能情报 —— 16 项，同源
	/// 画像表查无此人（占位期英雄 / 未进全量数据）→ ② ③ 显示「暂无史料」占位，① 照常。
	///
	/// 🔴 **本页不依赖活世界**（2026-09-11 改造）：只用 heroId + 时代年份，
	///   所以可以在**建世界之前**显示（见 plans/选人流程复刻太阁5-设计.md §十）。
	///   原「名声 / 装备武器 / 防具」三行**已删**——那是游戏内实时状态，选人阶段本就不该有。
	/// 🔴 [决定] 才真正开局——本页只把开局往后挪一步（防误触）。
	/// </summary>
	public class HeroDetailVM : ViewModel
	{
		/// <summary>能力条的一项（五维）。</summary>
		public class AbilityItemVM : ViewModel
		{
			/// <summary>能力条底槽宽度（px，与 prefab 里的槽宽一致；填充 = Value/100 × 此值）。</summary>
			public const int SlotWidth = 150;

			internal AbilityItemVM(string labelRaw, int value)
			{
				Name = new TextObject(labelRaw).ToString();
				Value = value;
				ValueText = value.ToString();
			}

			[DataSourceProperty] public string Name { get; }
			[DataSourceProperty] public string ValueText { get; }
			[DataSourceProperty] public int Value { get; }

			/// <summary>填充宽度（px）。值 0 = 空槽（引擎对 0 宽 widget 不出错，照常给）。
			/// 🔴 类型必须是 float——Gauntlet 的 SuggestedWidth 是 float 依赖属性，int 绑定可能被静默丢弃。</summary>
			[DataSourceProperty] public float FillWidth => Math.Max(0, Math.Min(100, Value)) * SlotWidth / 100f;
		}

		/// <summary>技能项（16 项之一）：档位点阵 + 数值。</summary>
		public class SkillItemVM : ViewModel
		{
			/// <summary>档位点数（太阁5 是星级观感，这里用 6 点表示 0–100）。</summary>
			private const int DotCount = 6;

			internal SkillItemVM(string labelRaw, int value)
			{
				Name = new TextObject(labelRaw).ToString();
				Value = value;
				ValueText = value.ToString();
			}

			[DataSourceProperty] public string Name { get; }
			[DataSourceProperty] public string ValueText { get; }
			[DataSourceProperty] public int Value { get; }

			/// <summary>档位点阵（实心 ● / 空心 ○，共 <see cref="DotCount"/> 个）。</summary>
			[DataSourceProperty]
			public string Dots
			{
				get
				{
					int filled = (int)Math.Round(Math.Max(0, Math.Min(100, Value)) * DotCount / 100.0);
					var sb = new StringBuilder(DotCount);
					for (int i = 0; i < DotCount; i++)
					{
						sb.Append(i < filled ? '●' : '○');	// ● / ○（BMP 内字符，安全）
					}
					return sb.ToString();
				}
			}
		}

		/// <summary>基本情报的一行（标签 + 值）。</summary>
		public class InfoRowVM : ViewModel
		{
			internal InfoRowVM(string label, string value)
			{
				Label = label;
				Value = value;
			}

			[DataSourceProperty] public string Label { get; }
			[DataSourceProperty] public string Value { get; }
		}

		private readonly Action _onBack;
		private readonly Action _onConfirm;

		public HeroDetailVM(string heroId, int year, Action onBack, Action onConfirm)
		{
			_onBack = onBack;
			_onConfirm = onConfirm;
			HeroId = heroId ?? string.Empty;

			Abilities = new MBBindingList<AbilityItemVM>();
			SkillsLeft = new MBBindingList<SkillItemVM>();
			SkillsRight = new MBBindingList<SkillItemVM>();
			BasicRows = new MBBindingList<InfoRowVM>();

			HeroCatalogRegistry.Lord lord = HeroSelectData.FindLord(year, HeroId);
			if (lord != null)
			{
				NameText = HeroSelectData.Resolve(lord.NameRaw);
				AddRow(IdentityLabel, HeroSelectData.Resolve(lord.IdentityRaw));
				AddRow(AllegianceLabel, HeroSelectData.GetHouseName(year, lord.HouseId));
				AddRow(SeatLabel, HeroSelectData.Resolve(lord.SeatRaw));
			}
			else
			{
				// 目录里没有（不该发生——目录与英雄集合同源）→ 至少把 id 显示出来便于排查
				NameText = HeroId;
			}

			// ── 生卒 / 年龄（时代年份 − 生年；史实口径）
			// 🔴 不能用 `CampaignTime.Now.GetYear`——那是"开局以来经过的年数"（新档 = 0）
			HeroProfileRegistry.Profile profile = HeroProfileRegistry.GetProfile(HeroId);
			HasProfile = profile != null;
			if (profile != null && profile.HasLifespan)
			{
				int age = year > 0 ? year - profile.Birth : 0;
				AgeText = new TextObject("{=LWN_hero_detail_age}{AGE} years old")
					.SetTextVariable("AGE", age.ToString()).ToString();
				LifespanText = $"{profile.Birth} – {profile.Die}";
			}

			// 立绘：优先用**目录按时代挑好的那张卡**（如木下在 1560 用「藤吉郎」那张，
			// 而不是立绘表首张的「羽柴」）；目录没给 = 回落立绘表首张；都没有 = 界面画占位框
			// 🔴 显示点：名字 → `Sprite` 对象（顺带按需加载纹理）。**名字串绑给 `Sprite=` 静默无效**
			//    ——引擎只对 prefab 里的字面量属性做名字解析，VM 绑定不做转换（范本 PlaybackDialogVM）。
			string bustupName = !string.IsNullOrEmpty(lord?.BustupSprite)
				? lord.BustupSprite
				: HeroProfileRegistry.GetBustupSpriteName(HeroId);
			BustupSprite = HeroProfileRegistry.LoadPortraitSprite(bustupName);
			HasBustup = BustupSprite != null;
			// 排查用（一次一行、有界）：立绘是"名字查不到"还是"纹理没进显存"，看这行就能分
			DebugLogger.Log($"[HeroDetail] 立绘：{(string.IsNullOrEmpty(bustupName) ? "(无名字)" : bustupName)}"
				+ $" → {(BustupSprite != null ? "已就绪" : "null")}");

			// 型别 / 目标描述：仅「推荐」人配了（普通人物留空 → 界面隐藏那一行）
			HeroProfileRegistry.Recommendation rec = FindRecommendation(HeroId);
			StoryType = rec != null ? new TextObject(rec.StoryTypeRaw).ToString() : string.Empty;
			StoryGoal = rec != null ? new TextObject(rec.StoryGoalRaw).ToString() : string.Empty;
			HasStory = !string.IsNullOrEmpty(StoryType) || !string.IsNullOrEmpty(StoryGoal);

			if (profile != null)
			{
				// 标签由**内容包**给（铁律 3：LWN 不认识「统率/足轻」这类具体词汇）
				// 读到的就是 {=KEY}fallback 原样串，交给 TextObject 走引擎本地化。
				string[] dims = HeroProfileRegistry.DimensionLabels;
				Abilities.Add(new AbilityItemVM(dims[0], profile.Command));
				Abilities.Add(new AbilityItemVM(dims[1], profile.Force));
				Abilities.Add(new AbilityItemVM(dims[2], profile.Govern));
				Abilities.Add(new AbilityItemVM(dims[3], profile.Wisdom));
				Abilities.Add(new AbilityItemVM(dims[4], profile.Charm));

				string[] skillLabels = HeroProfileRegistry.SkillLabels;
				// 16 项拆左右两列（各 8 项）——太阁5 原版就是两列，prefab 用两个 ListPanel 并排
				for (int i = 0; i < HeroProfileRegistry.SkillCount; i++)
				{
					var item = new SkillItemVM(skillLabels[i], profile.Skills[i]);
					if (i < HeroProfileRegistry.SkillCount / 2)
					{
						SkillsLeft.Add(item);
					}
					else
					{
						SkillsRight.Add(item);
					}
				}
			}
		}

		/// <summary>本页展示的英雄 StringId（[决定] 时用它落地）。</summary>
		public string HeroId { get; }

		// ── 文本 ──
		[DataSourceProperty] public string NameText { get; private set; } = string.Empty;
		[DataSourceProperty] public string AgeText { get; private set; } = string.Empty;
		[DataSourceProperty] public string LifespanText { get; private set; } = string.Empty;
		[DataSourceProperty] public string StoryType { get; private set; } = string.Empty;
		[DataSourceProperty] public string StoryGoal { get; private set; } = string.Empty;

		/// <summary>半身立绘（`Sprite` 对象；名字串绑给 `Sprite=` 无效，见构造里注释）。null = 无立绘。</summary>
		[DataSourceProperty] public Sprite BustupSprite { get; private set; }

		/// <summary>基本情报各行的「标签 + 值」（空值的行不入列）。</summary>
		[DataSourceProperty] public MBBindingList<InfoRowVM> BasicRows { get; }

		/// <summary>
		/// 列传正文。🔴 **太阁5 的列传不在 TaikouHero.csv 里**（129 列无此字段），
		/// 在 tg5msg 文本包里（已破解 XOR A5）——抽出来之前一律显示「暂无史料」占位，**不编造**。
		/// </summary>
		[DataSourceProperty] public string BiographyText => NoRecordText;

		/// <summary>是否查到了画像（false = 太阁专属面板显示「暂无史料」）。</summary>
		[DataSourceProperty] public bool HasProfile { get; private set; }

		[DataSourceProperty] public bool HasBustup { get; private set; }

		/// <summary>没立绘时画空白底板（不画 ImageWidget——空 sprite 的图片控件行为不可靠）。</summary>
		[DataSourceProperty] public bool HasNoBustup => !HasBustup;

		/// <summary>是否显示「型别 / 目标描述」（推荐人专属）。</summary>
		[DataSourceProperty] public bool HasStory { get; private set; }

		/// <summary>是否显示生卒行。</summary>
		[DataSourceProperty] public bool HasLifespan => !string.IsNullOrEmpty(LifespanText);

		[DataSourceProperty] public MBBindingList<AbilityItemVM> Abilities { get; }

		/// <summary>技能左列（前 8 项）。</summary>
		[DataSourceProperty] public MBBindingList<SkillItemVM> SkillsLeft { get; }

		/// <summary>技能右列（后 8 项）。</summary>
		[DataSourceProperty] public MBBindingList<SkillItemVM> SkillsRight { get; }

		// ── 固定文案（本地化键，铁律 13）──
		[DataSourceProperty] public string BasicHeader => new TextObject("{=LWN_hero_detail_basic}Profile").ToString();
		[DataSourceProperty] public string AbilityHeader => new TextObject("{=LWN_hero_detail_ability}Attributes").ToString();
		[DataSourceProperty] public string SkillHeader => new TextObject("{=LWN_hero_detail_skills}Skills").ToString();
		[DataSourceProperty] public string IdentityLabel => new TextObject("{=LWN_hero_detail_identity}Station").ToString();
		[DataSourceProperty] public string AllegianceLabel => new TextObject("{=LWN_hero_detail_allegiance}Allegiance").ToString();
		[DataSourceProperty] public string SeatLabel => new TextObject("{=LWN_hero_detail_seat}Seat").ToString();
		[DataSourceProperty] public string BioHeader => new TextObject("{=LWN_hero_detail_biography}Biography").ToString();
		[DataSourceProperty] public string NoRecordText => new TextObject("{=LWN_hero_detail_no_record}No record of this one survives.").ToString();
		[DataSourceProperty] public string BackText => new TextObject("{=LWN_hero_detail_back}Back").ToString();
		[DataSourceProperty] public string ConfirmText => new TextObject("{=LWN_hero_detail_confirm}Decide").ToString();

		/// <summary>[返回] → 回选人列表（不落地）。</summary>
		public void ExecuteBack()
		{
			_onBack?.Invoke();
		}

		/// <summary>[决定] → 用这个人开局（记下 id，建世界后落地）。</summary>
		public void ExecuteConfirm()
		{
			_onConfirm?.Invoke();
		}

		public override void RefreshValues()
		{
			base.RefreshValues();
			OnPropertyChanged(nameof(BasicHeader));
			OnPropertyChanged(nameof(AbilityHeader));
			OnPropertyChanged(nameof(SkillHeader));
			OnPropertyChanged(nameof(NoRecordText));
			OnPropertyChanged(nameof(BackText));
			OnPropertyChanged(nameof(ConfirmText));
		}

		// ───────────────────────── 取值助手 ─────────────────────────

		/// <summary>加一行基本情报；**值为空就不加**（列不出来 = 数据确实没有，不摆空行）。</summary>
		private void AddRow(string label, string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				BasicRows.Add(new InfoRowVM(label, value));
			}
		}

		private static HeroProfileRegistry.Recommendation FindRecommendation(string heroId)
		{
			foreach (HeroProfileRegistry.Recommendation r in HeroProfileRegistry.Recommendations)
			{
				if (r.HeroId == heroId)
				{
					return r;
				}
			}
			return null;
		}
	}
}
