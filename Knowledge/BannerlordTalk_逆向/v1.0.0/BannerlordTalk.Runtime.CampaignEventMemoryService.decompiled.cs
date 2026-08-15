using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordTalk.Runtime;

internal static class CampaignEventMemoryService
{
	internal const int MaximumRecords = 30;

	internal const int RetentionDays = 30;

	internal const int InjectionCooldownTurns = 3;

	internal const int InjectionCooldownDays = 1;

	internal const int MaximumExposures = 2;

	internal static CampaignEventAppendResult AppendSettlementOwnership(IList<CampaignEventRecord> records, CampaignSettlementOwnershipEvent value, CampaignEventRelationContext context, CampaignEventScopeMode scopeMode)
	{
		if (records == null || value == null || string.IsNullOrWhiteSpace(value.SettlementId) || value.SettlementKind == CampaignSettlementKind.Other)
		{
			return Rejected("settlement_invalid");
		}
		CampaignEventRelationLevel campaignEventRelationLevel = Classify(context, new string[3] { value.OldOwnerHeroId, value.NewOwnerHeroId, value.CapturerHeroId }, new string[2] { value.OldClanId, value.NewClanId }, new string[2] { value.OldKingdomId, value.NewKingdomId }, new string[1] { value.SettlementId });
		if (!ScopeAllows(scopeMode, campaignEventRelationLevel, isNotable: true))
		{
			return Rejected("settlement_out_of_scope");
		}
		Prune(records, value.CampaignDay);
		string id = "settlement:" + CleanId(value.SettlementId) + ":" + value.ChangeKind.ToString() + ":" + CleanId(value.OldClanId) + ":" + CleanId(value.NewClanId) + ":" + value.CampaignDay;
		CampaignEventRecord campaignEventRecord = records.FirstOrDefault((CampaignEventRecord item) => SameId(item, id));
		if (campaignEventRecord != null)
		{
			campaignEventRecord.OccurrenceCount = Math.Max(1, campaignEventRecord.OccurrenceCount) + 1;
			campaignEventRecord.UpdatedDay = Math.Max(campaignEventRecord.UpdatedDay, value.CampaignDay);
			campaignEventRecord.RelationLevel = Max(campaignEventRecord.RelationLevel, campaignEventRelationLevel);
			campaignEventRecord.Importance = Math.Max(campaignEventRecord.Importance, SettlementImportance(value, campaignEventRelationLevel));
			Normalize(records, value.CampaignDay);
			return Merged(campaignEventRecord, "settlement_same_day_duplicate");
		}
		CampaignEventRecord campaignEventRecord2 = (from item in records
			where item != null && item.Kind == CampaignEventKind.SettlementOwnership && Same(item.SettlementId, CleanId(value.SettlementId)) && value.CampaignDay >= item.UpdatedDay && value.CampaignDay - item.UpdatedDay <= 3
			orderby item.UpdatedDay descending
			select item).FirstOrDefault();
		if (campaignEventRecord2 != null)
		{
			campaignEventRecord2.EventId = id;
			campaignEventRecord2.OccurrenceCount = Math.Max(1, campaignEventRecord2.OccurrenceCount) + 1;
			campaignEventRecord2.UpdatedDay = value.CampaignDay;
			campaignEventRecord2.RelationLevel = Max(campaignEventRecord2.RelationLevel, campaignEventRelationLevel);
			campaignEventRecord2.Importance = Math.Max(campaignEventRecord2.Importance, SettlementImportance(value, campaignEventRelationLevel));
			campaignEventRecord2.Text = DisplayName(value.SettlementName, value.SettlementId) + "在数日内" + ChineseCount(campaignEventRecord2.OccurrenceCount) + "次易主；最近一次：" + FormatSettlementOwnership(value);
			MergeIds(campaignEventRecord2.RelatedHeroIds, value.OldOwnerHeroId, value.NewOwnerHeroId, value.CapturerHeroId);
			MergeIds(campaignEventRecord2.RelatedClanIds, value.OldClanId, value.NewClanId);
			MergeIds(campaignEventRecord2.RelatedKingdomIds, value.OldKingdomId, value.NewKingdomId);
			Normalize(records, value.CampaignDay);
			return Merged(campaignEventRecord2, "settlement_rapid_changes_aggregated");
		}
		CampaignEventRecord campaignEventRecord3 = new CampaignEventRecord();
		campaignEventRecord3.EventId = id;
		campaignEventRecord3.Kind = CampaignEventKind.SettlementOwnership;
		campaignEventRecord3.RelationLevel = campaignEventRelationLevel;
		campaignEventRecord3.Text = FormatSettlementOwnership(value);
		campaignEventRecord3.Importance = SettlementImportance(value, campaignEventRelationLevel);
		campaignEventRecord3.CreatedDay = value.CampaignDay;
		campaignEventRecord3.UpdatedDay = value.CampaignDay;
		campaignEventRecord3.SettlementId = CleanId(value.SettlementId);
		campaignEventRecord3.SettlementName = Clean(value.SettlementName, 120);
		campaignEventRecord3.IsNotable = true;
		campaignEventRecord3.RelatedHeroIds = DistinctIds(value.OldOwnerHeroId, value.NewOwnerHeroId, value.CapturerHeroId);
		campaignEventRecord3.RelatedClanIds = DistinctIds(value.OldClanId, value.NewClanId);
		campaignEventRecord3.RelatedKingdomIds = DistinctIds(value.OldKingdomId, value.NewKingdomId);
		CampaignEventRecord campaignEventRecord4 = campaignEventRecord3;
		records.Add(campaignEventRecord4);
		Normalize(records, value.CampaignDay);
		return Added(campaignEventRecord4);
	}

	internal static CampaignEventAppendResult AppendHeroCaptured(IList<CampaignEventRecord> records, CampaignHeroCapturedEvent value, CampaignEventRelationContext context, CampaignEventScopeMode scopeMode)
	{
		if (records == null || value == null || string.IsNullOrWhiteSpace(value.HeroId))
		{
			return Rejected("capture_invalid");
		}
		CampaignEventRelationLevel campaignEventRelationLevel = Classify(context, new string[1] { value.HeroId }, new string[2] { value.ClanId, value.CaptorClanId }, new string[2] { value.KingdomId, value.CaptorKingdomId }, Array.Empty<string>());
		if (!ScopeAllows(scopeMode, campaignEventRelationLevel, value.IsNotable))
		{
			return Rejected("capture_out_of_scope");
		}
		Prune(records, value.CampaignDay);
		CampaignEventRecord campaignEventRecord = (from item in records
			where item != null && item.Kind == CampaignEventKind.HeroCaptivity && Same(item.SubjectHeroId, value.HeroId)
			orderby item.UpdatedDay descending
			select item).FirstOrDefault((CampaignEventRecord item) => item.CaptivityOpen);
		if (campaignEventRecord != null)
		{
			campaignEventRecord.CaptivityOpen = true;
			campaignEventRecord.ReleasedDay = -1;
			campaignEventRecord.ReleaseDetail = "";
			campaignEventRecord.UpdatedDay = value.CampaignDay;
			campaignEventRecord.OccurrenceCount = Math.Max(1, campaignEventRecord.OccurrenceCount) + 1;
			campaignEventRecord.CaptorName = First(value.CaptorName, campaignEventRecord.CaptorName);
			campaignEventRecord.RelationLevel = Max(campaignEventRecord.RelationLevel, campaignEventRelationLevel);
			campaignEventRecord.Importance = Math.Max(campaignEventRecord.Importance, HeroEventImportance(campaignEventRelationLevel, value.IsNotable, 0.72f));
			campaignEventRecord.Text = FormatCaptivity(campaignEventRecord);
			MergeIds(campaignEventRecord.RelatedClanIds, value.ClanId, value.CaptorClanId);
			MergeIds(campaignEventRecord.RelatedKingdomIds, value.KingdomId, value.CaptorKingdomId);
			Normalize(records, value.CampaignDay);
			return Merged(campaignEventRecord, "capture_cycle_merged");
		}
		CampaignEventRecord campaignEventRecord2 = records.FirstOrDefault((CampaignEventRecord item) => item != null && item.Kind == CampaignEventKind.HeroCaptivity && Same(item.SubjectHeroId, value.HeroId) && item.CapturedDay == value.CampaignDay);
		if (campaignEventRecord2 != null)
		{
			campaignEventRecord2.OccurrenceCount = Math.Max(1, campaignEventRecord2.OccurrenceCount) + 1;
			return Merged(campaignEventRecord2, "capture_same_day_duplicate");
		}
		CampaignEventRecord campaignEventRecord3 = new CampaignEventRecord();
		campaignEventRecord3.EventId = "captivity:" + CleanId(value.HeroId) + ":" + value.CampaignDay;
		campaignEventRecord3.Kind = CampaignEventKind.HeroCaptivity;
		campaignEventRecord3.RelationLevel = campaignEventRelationLevel;
		campaignEventRecord3.Importance = HeroEventImportance(campaignEventRelationLevel, value.IsNotable, 0.72f);
		campaignEventRecord3.CreatedDay = value.CampaignDay;
		campaignEventRecord3.UpdatedDay = value.CampaignDay;
		campaignEventRecord3.SubjectHeroId = CleanId(value.HeroId);
		campaignEventRecord3.SubjectHeroName = Clean(value.HeroName, 120);
		campaignEventRecord3.CaptivityOpen = true;
		campaignEventRecord3.CapturedDay = value.CampaignDay;
		campaignEventRecord3.CaptorName = Clean(value.CaptorName, 120);
		campaignEventRecord3.IsNotable = value.IsNotable;
		campaignEventRecord3.RelatedHeroIds = DistinctIds(value.HeroId);
		campaignEventRecord3.RelatedClanIds = DistinctIds(value.ClanId, value.CaptorClanId);
		campaignEventRecord3.RelatedKingdomIds = DistinctIds(value.KingdomId, value.CaptorKingdomId);
		CampaignEventRecord campaignEventRecord4 = campaignEventRecord3;
		campaignEventRecord4.Text = FormatCaptivity(campaignEventRecord4);
		records.Add(campaignEventRecord4);
		Normalize(records, value.CampaignDay);
		return Added(campaignEventRecord4);
	}

	internal static CampaignEventAppendResult AppendHeroReleased(IList<CampaignEventRecord> records, CampaignHeroReleasedEvent value, CampaignEventRelationContext context, CampaignEventScopeMode scopeMode)
	{
		if (records == null || value == null || string.IsNullOrWhiteSpace(value.HeroId))
		{
			return Rejected("release_invalid");
		}
		CampaignEventRelationLevel campaignEventRelationLevel = Classify(context, new string[1] { value.HeroId }, new string[1] { value.ClanId }, new string[1] { value.KingdomId }, Array.Empty<string>());
		if (!ScopeAllows(scopeMode, campaignEventRelationLevel, value.IsNotable))
		{
			return Rejected("release_out_of_scope");
		}
		Prune(records, value.CampaignDay);
		CampaignEventRecord campaignEventRecord = (from item in records
			where item != null && item.Kind == CampaignEventKind.HeroCaptivity && Same(item.SubjectHeroId, value.HeroId)
			orderby item.UpdatedDay descending
			select item).FirstOrDefault((CampaignEventRecord item) => item.CaptivityOpen);
		if (campaignEventRecord != null)
		{
			campaignEventRecord.CaptivityOpen = false;
			campaignEventRecord.ReleasedDay = value.CampaignDay;
			campaignEventRecord.ReleaseDetail = Clean(value.ReleaseDetail, 100);
			campaignEventRecord.UpdatedDay = value.CampaignDay;
			campaignEventRecord.RelationLevel = Max(campaignEventRecord.RelationLevel, campaignEventRelationLevel);
			campaignEventRecord.Importance = Math.Max(campaignEventRecord.Importance, HeroEventImportance(campaignEventRelationLevel, value.IsNotable, 0.68f));
			campaignEventRecord.Text = FormatCaptivity(campaignEventRecord);
			MergeIds(campaignEventRecord.RelatedClanIds, value.ClanId);
			MergeIds(campaignEventRecord.RelatedKingdomIds, value.KingdomId);
			Normalize(records, value.CampaignDay);
			return Merged(campaignEventRecord, "release_joined_capture_cycle");
		}
		CampaignEventRecord campaignEventRecord2 = records.FirstOrDefault((CampaignEventRecord item) => item != null && item.Kind == CampaignEventKind.HeroCaptivity && Same(item.SubjectHeroId, value.HeroId) && item.ReleasedDay == value.CampaignDay);
		if (campaignEventRecord2 != null)
		{
			campaignEventRecord2.OccurrenceCount = Math.Max(1, campaignEventRecord2.OccurrenceCount) + 1;
			return Merged(campaignEventRecord2, "release_same_day_duplicate");
		}
		CampaignEventRecord campaignEventRecord3 = new CampaignEventRecord();
		campaignEventRecord3.EventId = "captivity:" + CleanId(value.HeroId) + ":release:" + value.CampaignDay;
		campaignEventRecord3.Kind = CampaignEventKind.HeroCaptivity;
		campaignEventRecord3.RelationLevel = campaignEventRelationLevel;
		campaignEventRecord3.Text = DisplayName(value.HeroName, value.HeroId) + "于第" + value.CampaignDay + "日获释" + DetailSuffix(value.ReleaseDetail) + "。";
		campaignEventRecord3.Importance = HeroEventImportance(campaignEventRelationLevel, value.IsNotable, 0.6f);
		campaignEventRecord3.CreatedDay = value.CampaignDay;
		campaignEventRecord3.UpdatedDay = value.CampaignDay;
		campaignEventRecord3.SubjectHeroId = CleanId(value.HeroId);
		campaignEventRecord3.SubjectHeroName = Clean(value.HeroName, 120);
		campaignEventRecord3.CapturedDay = -1;
		campaignEventRecord3.ReleasedDay = value.CampaignDay;
		campaignEventRecord3.ReleaseDetail = Clean(value.ReleaseDetail, 100);
		campaignEventRecord3.IsNotable = value.IsNotable;
		campaignEventRecord3.RelatedHeroIds = DistinctIds(value.HeroId);
		campaignEventRecord3.RelatedClanIds = DistinctIds(value.ClanId);
		campaignEventRecord3.RelatedKingdomIds = DistinctIds(value.KingdomId);
		CampaignEventRecord campaignEventRecord4 = campaignEventRecord3;
		records.Add(campaignEventRecord4);
		Normalize(records, value.CampaignDay);
		return Added(campaignEventRecord4);
	}

	internal static CampaignEventAppendResult AppendHeroDeath(IList<CampaignEventRecord> records, CampaignHeroDeathEvent value, CampaignEventRelationContext context, CampaignEventScopeMode scopeMode)
	{
		if (records == null || value == null || string.IsNullOrWhiteSpace(value.HeroId))
		{
			return Rejected("death_invalid");
		}
		CampaignEventRelationLevel campaignEventRelationLevel = Classify(context, new string[2] { value.HeroId, value.KillerHeroId }, new string[1] { value.ClanId }, new string[1] { value.KingdomId }, Array.Empty<string>());
		if (!ScopeAllows(scopeMode, campaignEventRelationLevel, value.IsNotable))
		{
			return Rejected("death_out_of_scope");
		}
		Prune(records, value.CampaignDay);
		string id = "death:" + CleanId(value.HeroId) + ":" + value.CampaignDay;
		CampaignEventRecord campaignEventRecord = records.FirstOrDefault((CampaignEventRecord item) => SameId(item, id));
		if (campaignEventRecord != null)
		{
			campaignEventRecord.OccurrenceCount = Math.Max(1, campaignEventRecord.OccurrenceCount) + 1;
			return Merged(campaignEventRecord, "death_same_day_duplicate");
		}
		string text = DisplayName(value.HeroName, value.HeroId) + "于第" + value.CampaignDay + "日身亡";
		if (!string.IsNullOrWhiteSpace(value.KillerName))
		{
			text = text + "，凶手为" + Clean(value.KillerName, 120);
		}
		text = text + DetailSuffix(value.CauseDetail) + "。";
		CampaignEventRecord campaignEventRecord2 = new CampaignEventRecord();
		campaignEventRecord2.EventId = id;
		campaignEventRecord2.Kind = CampaignEventKind.HeroDeath;
		campaignEventRecord2.RelationLevel = campaignEventRelationLevel;
		campaignEventRecord2.Text = text;
		campaignEventRecord2.Importance = HeroEventImportance(campaignEventRelationLevel, value.IsNotable, 0.84f);
		campaignEventRecord2.CreatedDay = value.CampaignDay;
		campaignEventRecord2.UpdatedDay = value.CampaignDay;
		campaignEventRecord2.SubjectHeroId = CleanId(value.HeroId);
		campaignEventRecord2.SubjectHeroName = Clean(value.HeroName, 120);
		campaignEventRecord2.IsNotable = value.IsNotable;
		campaignEventRecord2.RelatedHeroIds = DistinctIds(value.HeroId, value.KillerHeroId);
		campaignEventRecord2.RelatedClanIds = DistinctIds(value.ClanId);
		campaignEventRecord2.RelatedKingdomIds = DistinctIds(value.KingdomId);
		CampaignEventRecord campaignEventRecord3 = campaignEventRecord2;
		records.Add(campaignEventRecord3);
		Normalize(records, value.CampaignDay);
		return Added(campaignEventRecord3);
	}

	internal static CampaignEventRecallResult SelectForInjection(IList<CampaignEventRecord> records, CampaignEventRecallRequest request)
	{
		CampaignEventRecallResult campaignEventRecallResult = new CampaignEventRecallResult();
		if (records == null || request == null || request.ScopeMode == CampaignEventScopeMode.Off)
		{
			return campaignEventRecallResult;
		}
		Prune(records, request.CurrentDay);
		int count = Math.Max(1, Math.Min(2, request.MaximumResults));
		foreach (CampaignEventRecord item in from item in (from item in records
				where EligibleForRecall(item, request)
				select new
				{
					Record = item,
					Score = RecallScore(item, request.Context, request.CurrentDay, request.ScopeMode)
				} into item
				where item.Score > 0.0
				orderby item.Score descending, item.Record.UpdatedDay descending
				select item).ThenBy(item => item.Record.EventId, StringComparer.Ordinal).Take(count)
			select item.Record)
		{
			campaignEventRecallResult.Records.Add(item);
		}
		return campaignEventRecallResult;
	}

	internal static void MarkInjected(IEnumerable<CampaignEventRecord> records, int currentDay, long currentTurn)
	{
		foreach (CampaignEventRecord item in records ?? Array.Empty<CampaignEventRecord>())
		{
			if (item != null)
			{
				item.LastInjectedDay = currentDay;
				item.LastInjectedTurn = currentTurn;
				item.ExposureCount = Math.Max(0, item.ExposureCount) + 1;
			}
		}
	}

	internal static void Normalize(IList<CampaignEventRecord> records, int currentDay)
	{
		if (records == null)
		{
			return;
		}
		foreach (CampaignEventRecord item3 in records.Where((CampaignEventRecord item) => item != null))
		{
			item3.EventId = CleanId(item3.EventId);
			item3.Text = Clean(item3.Text, 1200);
			item3.SubjectHeroId = CleanId(item3.SubjectHeroId);
			item3.SubjectHeroName = Clean(item3.SubjectHeroName, 120);
			item3.SettlementId = CleanId(item3.SettlementId);
			item3.SettlementName = Clean(item3.SettlementName, 120);
			item3.CaptorName = Clean(item3.CaptorName, 120);
			item3.ReleaseDetail = Clean(item3.ReleaseDetail, 100);
			item3.RelatedHeroIds = DistinctIds((item3.RelatedHeroIds ?? new List<string>()).ToArray());
			item3.RelatedClanIds = DistinctIds((item3.RelatedClanIds ?? new List<string>()).ToArray());
			item3.RelatedKingdomIds = DistinctIds((item3.RelatedKingdomIds ?? new List<string>()).ToArray());
			item3.Importance = Math.Max(0f, Math.Min(1f, item3.Importance));
			item3.OccurrenceCount = Math.Max(1, item3.OccurrenceCount);
			item3.ExposureCount = Math.Max(0, item3.ExposureCount);
			item3.UpdatedDay = Math.Max(item3.CreatedDay, item3.UpdatedDay);
		}
		Prune(records, currentDay);
		while (records.Count > 30)
		{
			CampaignEventRecord item2 = (from item in records
				orderby item?.UpdatedDay ?? int.MinValue, item?.RelationLevel ?? CampaignEventRelationLevel.World, item?.Importance ?? 0f
				select item).First();
			records.Remove(item2);
		}
	}

	internal static CampaignSettlementChangeKind ParseSettlementChangeKind(string value)
	{
		return (value ?? "").Trim() switch
		{
			"BySiege" => CampaignSettlementChangeKind.Siege, 
			"ByBarter" => CampaignSettlementChangeKind.Barter, 
			"ByLeaveFaction" => CampaignSettlementChangeKind.LeaveFaction, 
			"ByKingDecision" => CampaignSettlementChangeKind.KingDecision, 
			"ByGift" => CampaignSettlementChangeKind.Gift, 
			"ByRebellion" => CampaignSettlementChangeKind.Rebellion, 
			"ByClanDestruction" => CampaignSettlementChangeKind.ClanDestruction, 
			_ => CampaignSettlementChangeKind.Default, 
		};
	}

	private static bool EligibleForRecall(CampaignEventRecord record, CampaignEventRecallRequest request)
	{
		if (record == null || string.IsNullOrWhiteSpace(record.Text) || !ScopeAllows(request.ScopeMode, record.RelationLevel, record.IsNotable) || record.ExposureCount >= 2)
		{
			return false;
		}
		if (record.LastInjectedDay >= 0 && request.CurrentDay - record.LastInjectedDay < 1)
		{
			return false;
		}
		if (record.LastInjectedTurn >= 0 && request.CurrentTurn - record.LastInjectedTurn < 3)
		{
			return false;
		}
		return true;
	}

	private static double RecallScore(CampaignEventRecord record, CampaignEventRelationContext context, int currentDay, CampaignEventScopeMode scopeMode)
	{
		context = context ?? new CampaignEventRelationContext();
		bool flag = Intersects(record.RelatedHeroIds, context.HeroIds);
		bool flag2 = Intersects(record.RelatedClanIds, context.ClanIds);
		bool flag3 = Intersects(record.RelatedKingdomIds, context.KingdomIds);
		bool flag4 = Contains(context.SettlementIds, record.SettlementId);
		bool flag5 = TopicMatches(record, context.TopicText);
		bool flag6 = record.RelationLevel == CampaignEventRelationLevel.Direct;
		bool flag7 = record.RelationLevel == CampaignEventRelationLevel.Faction;
		bool flag8 = scopeMode == CampaignEventScopeMode.WorldNews && record.IsNotable;
		if (!flag && !flag2 && !flag3 && !flag4 && !flag5 && !flag6 && !flag8)
		{
			return 0.0;
		}
		double num = (double)Math.Max(0, currentDay - record.UpdatedDay) * 0.02;
		return (double)record.Importance + (flag ? 1.0 : 0.0) + (flag4 ? 0.9 : 0.0) + (flag2 ? 0.65 : 0.0) + (flag3 ? 0.45 : 0.0) + (flag5 ? 0.55 : 0.0) + (flag6 ? 0.35 : (flag7 ? 0.15 : 0.0)) + (flag8 ? 0.05 : 0.0) - num;
	}

	private static CampaignEventRelationLevel Classify(CampaignEventRelationContext context, IEnumerable<string> heroIds, IEnumerable<string> clanIds, IEnumerable<string> kingdomIds, IEnumerable<string> settlementIds)
	{
		context = context ?? new CampaignEventRelationContext();
		if (Intersects(heroIds, context.HeroIds) || Intersects(settlementIds, context.SettlementIds))
		{
			return CampaignEventRelationLevel.Direct;
		}
		if (Intersects(clanIds, context.ClanIds) || Intersects(kingdomIds, context.KingdomIds))
		{
			return CampaignEventRelationLevel.Faction;
		}
		return CampaignEventRelationLevel.World;
	}

	private static bool ScopeAllows(CampaignEventScopeMode mode, CampaignEventRelationLevel relation, bool isNotable)
	{
		if (mode == CampaignEventScopeMode.Off)
		{
			return false;
		}
		if (relation >= CampaignEventRelationLevel.Faction)
		{
			return true;
		}
		return mode == CampaignEventScopeMode.WorldNews && isNotable;
	}

	private static string FormatSettlementOwnership(CampaignSettlementOwnershipEvent value)
	{
		string text = DisplayName(value.SettlementName, value.SettlementId) + ((value.SettlementKind == CampaignSettlementKind.Town) ? "（城镇）" : "（城堡）");
		string text2 = First(value.OldClanName, value.OldOwnerName, value.OldKingdomName, "原主人不详");
		string text3 = First(value.NewClanName, value.NewOwnerName, value.NewKingdomName, "新主人不详");
		string text4 = First(value.CapturerName, value.NewClanName, value.NewOwnerName, value.NewKingdomName, "进攻方");
		return value.ChangeKind switch
		{
			CampaignSettlementChangeKind.Siege => text + "被" + text4 + "攻占，原属" + text2 + "，现归" + text3 + "。", 
			CampaignSettlementChangeKind.Barter => text + "经交易由" + text2 + "转归" + text3 + "。", 
			CampaignSettlementChangeKind.LeaveFaction => text + "因所属家族退出原势力，由" + text2 + "转归" + text3 + "。", 
			CampaignSettlementChangeKind.KingDecision => text + "经领主裁定，由" + text2 + "改归" + text3 + "。", 
			CampaignSettlementChangeKind.Gift => text + "作为赠与，由" + text2 + "转归" + text3 + "。", 
			CampaignSettlementChangeKind.Rebellion => text + "因叛乱易主，由" + text2 + "转归" + text3 + "。", 
			CampaignSettlementChangeKind.ClanDestruction => text + "因原家族覆灭，由" + text2 + "转归" + text3 + "。", 
			_ => text + "易主，由" + text2 + "转归" + text3 + "。", 
		};
	}

	private static string FormatCaptivity(CampaignEventRecord record)
	{
		string text = DisplayName(record.SubjectHeroName, record.SubjectHeroId);
		string text2 = (string.IsNullOrWhiteSpace(record.CaptorName) ? "身份不明的一方" : record.CaptorName);
		string text3 = text + "于第" + Math.Max(0, record.CapturedDay) + "日被" + text2 + "俘虏";
		if (!record.CaptivityOpen && record.ReleasedDay >= 0)
		{
			text3 = text3 + "，并于第" + record.ReleasedDay + "日获释" + DetailSuffix(record.ReleaseDetail);
		}
		return text3 + "。";
	}

	private static float SettlementImportance(CampaignSettlementOwnershipEvent value, CampaignEventRelationLevel relation)
	{
		float num = ((value.ChangeKind == CampaignSettlementChangeKind.Siege) ? 0.82f : 0.66f);
		if (value.SettlementKind == CampaignSettlementKind.Town)
		{
			num += 0.04f;
		}
		switch (relation)
		{
		case CampaignEventRelationLevel.Direct:
			num += 0.12f;
			break;
		case CampaignEventRelationLevel.Faction:
			num += 0.06f;
			break;
		}
		return Math.Min(1f, num);
	}

	private static float HeroEventImportance(CampaignEventRelationLevel relation, bool notable, float baseValue)
	{
		float num = baseValue + (notable ? 0.06f : 0f);
		switch (relation)
		{
		case CampaignEventRelationLevel.Direct:
			num += 0.18f;
			break;
		case CampaignEventRelationLevel.Faction:
			num += 0.08f;
			break;
		}
		return Math.Min(1f, num);
	}

	private static void Prune(IList<CampaignEventRecord> records, int currentDay)
	{
		if (records == null)
		{
			return;
		}
		int num = currentDay - 30 + 1;
		for (int num2 = records.Count - 1; num2 >= 0; num2--)
		{
			CampaignEventRecord campaignEventRecord = records[num2];
			if (campaignEventRecord == null || campaignEventRecord.UpdatedDay < num)
			{
				records.RemoveAt(num2);
			}
		}
	}

	private static bool TopicMatches(CampaignEventRecord record, string topic)
	{
		string text = Clean(topic, 1000);
		if (text.Length < 2 || record == null)
		{
			return false;
		}
		if (!ContainsText(text, record.SubjectHeroName) && !ContainsText(text, record.SettlementName))
		{
			return ContainsText(text, record.CaptorName);
		}
		return true;
	}

	private static bool ContainsText(string source, string candidate)
	{
		string text = Clean(candidate, 120);
		if (text.Length >= 2)
		{
			return source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}

	private static bool Intersects(IEnumerable<string> left, IEnumerable<string> right)
	{
		HashSet<string> values = new HashSet<string>((right ?? Array.Empty<string>()).Where((string value) => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
		return (left ?? Array.Empty<string>()).Any((string value) => !string.IsNullOrWhiteSpace(value) && values.Contains(value));
	}

	private static bool Contains(IEnumerable<string> values, string expected)
	{
		if (!string.IsNullOrWhiteSpace(expected))
		{
			return (values ?? Array.Empty<string>()).Contains(expected, StringComparer.Ordinal);
		}
		return false;
	}

	private static List<string> DistinctIds(params string[] values)
	{
		return (from value in (values ?? Array.Empty<string>()).Select(CleanId)
			where value.Length > 0
			select value).Distinct(StringComparer.Ordinal).ToList();
	}

	private static void MergeIds(IList<string> target, params string[] values)
	{
		if (target == null)
		{
			return;
		}
		foreach (string item in DistinctIds(values))
		{
			if (!target.Contains(item, StringComparer.Ordinal))
			{
				target.Add(item);
			}
		}
	}

	private static CampaignEventRelationLevel Max(CampaignEventRelationLevel left, CampaignEventRelationLevel right)
	{
		if (left < right)
		{
			return right;
		}
		return left;
	}

	private static string DetailSuffix(string detail)
	{
		string text = Clean(detail, 100);
		if (text.Length != 0)
		{
			return "（" + text + "）";
		}
		return "";
	}

	private static string ChineseCount(int count)
	{
		return count switch
		{
			2 => "两", 
			3 => "三", 
			4 => "四", 
			_ => Math.Max(2, count).ToString(), 
		};
	}

	private static string DisplayName(string name, string fallback)
	{
		return First(name, fallback, "未知对象");
	}

	private static string First(params string[] values)
	{
		return (values ?? Array.Empty<string>()).Select((string value) => Clean(value, 120)).FirstOrDefault((string value) => value.Length > 0) ?? "";
	}

	private static string CleanId(string value)
	{
		return Clean(value, 160);
	}

	private static string Clean(string value, int maximum)
	{
		string text = (value ?? "").Replace('\0', ' ').Replace('\r', ' ').Replace('\n', ' ')
			.Trim();
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum).TrimEnd();
	}

	private static bool Same(string left, string right)
	{
		return string.Equals(left, right, StringComparison.Ordinal);
	}

	private static bool SameId(CampaignEventRecord record, string id)
	{
		if (record != null)
		{
			return Same(record.EventId, id);
		}
		return false;
	}

	private static CampaignEventAppendResult Added(CampaignEventRecord record)
	{
		return new CampaignEventAppendResult
		{
			Record = record,
			Added = true,
			DiagnosticCode = "event_added"
		};
	}

	private static CampaignEventAppendResult Merged(CampaignEventRecord record, string code)
	{
		return new CampaignEventAppendResult
		{
			Record = record,
			Merged = true,
			DiagnosticCode = code
		};
	}

	private static CampaignEventAppendResult Rejected(string code)
	{
		return new CampaignEventAppendResult
		{
			DiagnosticCode = code
		};
	}
}
