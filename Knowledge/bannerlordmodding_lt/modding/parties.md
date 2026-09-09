# Parties

<!-- 源: https://docs.bannerlordmodding.lt/modding/parties/ | 抓取日期: 2026-09-09 -->

* [API](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_campaign_system_1_1_party_1_1_mobile_party.html)

## PartyBase

```
.ItemRoster
.MemberRoster
.PrisonRoster

.IsSettlement
.IsMobileParty

.Settlement
.MobileParty
```

## Settlement

```
PartyBase Settlement.Party
party.CurrentSettlement
```

## MobileParty

Our party:

```
MobileParty.MainParty
```

PartyBase MobileParty.Party

```
.ItemRoster
.MemberRoster
```

## Leader

```
Hero mobileParty.LeaderHero
mobileParty.RemovePartyLeader();
mobileParty.ChangePartyLeader(newHeroLeader);
```

## Garrison

```
bool MobileParty.IsGarrison
```

Garrison party does not have `.ActualClan`. To check the clan use `MobileParty.CurrentSettlement.OwnerClan`

## Count members

TroopRoster

```
MobileParty.MainParty.MemberRoster.TotalManCount

.TotalRegulars
.TotalWoundedRegulars
.TotalWoundedHeroes
.TotalHeroes
.TotalWounded
.TotalManCount
.TotalHealthyCount
.TotalHealthyCount
```

## Morale

Get:

```
MobileParty.MainParty.Morale
MobileParty.MainParty.RecentEventsMorale
```

Set:

```
MobileParty.MainParty.RecentEventsMorale += 10;
```

This changes `Recent Events` and the change fades with time:

![](https://docs.bannerlordmodding.lt/pics/2411051332.png)

To add an additional line to the morale, it is necessary to patch `DefaultPartyMoraleModel - GetEffectivePartyMorale` or create own Morale Model.

![](https://docs.bannerlordmodding.lt/pics/2411051404.png)

Patch example:

```
public class DefaultPartyMoraleModel_GetEffectivePartyMorale_Patch
{
    public static void Postfix(MobileParty mobileParty, bool includeDescription, ref ExplainedNumber __result)
    {
        __result.Add(2, new TextObject("Camp Followers"), null);
    }
}
```

NOTE: I apply this as a late Harmony patch in OnGameStart

## Food

```
int GetNumDaysForFoodToLast()
float FoodChange                // how much eats / day
int TotalFoodAtInventory
```

## Get party Companions

```
for (int i = 0; i < MobileParty.MainParty.MemberRoster.Count; i++)
{
    CharacterObject characterAtIndex = Hero.MainHero.PartyBelongedTo.MemberRoster.GetCharacterAtIndex(i);
    if (characterAtIndex.HeroObject != null && characterAtIndex.HeroObject != Hero.MainHero)
    {
        Hero hero = characterAtIndex.HeroObject;
        // do smthg with the hero (companion)
    }
}
```

## Get effective party role heroes

```
party.EffectiveEngineer
party.EffectiveQuartermaster
party.EffectiveScout
party.EffectiveSurgeon
```

## Add hero to a party

```
AddCompanionAction.Apply()
```

## Disorganized

```
bool    IsDisorganized [get]
void    SetDisorganized (bool isDisorganized)
CampaignTime        DisorganizedUntilTime [get]
DefaultPartyImpairmentModel -  bool CanGetDisorganized(PartyBase party)
```

## Destroy

```
DestroyPartyAction.Apply()
```

[Don't use](https://discord.com/channels/411286129317249035/677511186295685150/1263112214873772043) MobileParty.RemoveParty()

## Desert troops

```
MobilePartyHelper.DesertTroopsFromParty(MobileParty party, int stackNo, int numberOfDeserters, int numberOfWoundedDeserters, ref TroopRoster desertedTroopList)
```

## Main party on game start

Add 50 grain on start:

```
[HarmonyPatch(typeof(Campaign), "InitializeMainParty")]
public class CampaignPatch
{
    [HarmonyPostfix]
    private static void CampaignPostfix(Campaign __instance)
    {
        __instance.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 50);
    }
}
```

## Party Size

Sly:

If anyone one day is looking for info on how the AI adjusts party sizes:

MakeClanFinancialEvaluation sets party wage caps : >90k clan gold = 10k, ]30k, 90k] = ]600, 1200], <30k = [200, 600]
a MobileParty has its PaymentLimit set by the above
CalculateMobilePartyMemberSizeLimit calculates party size limit
a party has a LimitedPartySize if the ClanFinancialEval sets one (always for kingdom/minor faction clans)
this cap is only lower than the normal calculation if finances throttle PaymentLimit
the cap is roughly PaymentLimit/(average wage/troop in the default culture hero party template)
if a party exceeds its PaymentLimit, GetNumberOfDeserters calculates a number of deserters based on the (wages paid beyond the limit)/(4\*AvgCulturalTemplateWage); a maxmimum of 20 desertions per day is allowed
PartiesCheckDesertionDueToPartySizeExceedsPaymentRatio checks for troops in excess of party limit or wage limit
it then attempts to remove the lowest tier units first until it reaches the #ofDeserters

## Position

```
Vec2 mobileParty.GetPosition2D
CampaignVec2 mobileParty.Position
Vec3 GetPositionAsVec3()
```

## Party templates

### Example

```
<MBPartyTemplate id="looters_template">
    <stacks>
        <PartyTemplateStack min_value="4" max_value="36" troop="NPCCharacter.looter"/>
        <PartyTemplateStack min_value="1" max_value="4" troop="NPCCharacter.deserter"/>
        <PartyTemplateStack min_value="1" max_value="1" troop="NPCCharacter.camp_whore"/>
    </stacks>
</MBPartyTemplate>
```

### min\_value max\_value

hunharibo:

Those numbers are not absolute, they are defining ratios  
If you sum up all the min\_values and max\_values (respectively) of all the partytemplate stacks within a partytemplate, that is your total. The values of each individual stack gets divided by the total to get a ratio of percentage for that troop type in the party

## IsVisualDirty

In C# code:

IsVisualDirty = true is just a “needs visual refresh” flag.

When something about a party changes in a way that could affect how it looks on the campaign map (visibility, banner/colors, nameplate, icon, etc.), the code calls SetVisualAsDirty(), which sets IsVisualDirty to true. A visual system elsewhere will notice this and rebuild/update the party’s visuals once, then call OnVisualsUpdated() to set it back to false.

A few notes:

* It’s marked [CachedData], so it’s not saved to the save file—purely runtime state.
* Setting it to true doesn’t redraw immediately; it lets the engine defer the work and avoid re-building visuals every tiny change.
* You should flip it when changing anything that affects appearance (e.g., owner/banner/culture/visibility).
