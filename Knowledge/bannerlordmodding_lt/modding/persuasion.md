# Persuasion

<!-- 源: https://docs.bannerlordmodding.lt/modding/persuasion/ | 抓取日期: 2026-09-09 -->

## Implementation Examples

### LordDefectionCampaignBehavior

Structure of LordDefectionCampaignBehavior

[Fullsize Link](https://docs.bannerlordmodding.lt/pics/j5Sm0Zs.png)

![](https://docs.bannerlordmodding.lt/pics/j5Sm0Zs.png)

### RomanceCampaignBehavior

### [Trade Agent mod](https://www.nexusmods.com/mountandblade2bannerlord/mods/5618)

[Link](https://github.com/Litauen/LT_TradeAgent/blob/master/LTTABarter.cs)

Structure

![](https://docs.bannerlordmodding.lt/pics/OjHOkDe.png)

## PersuasionDifficulty

Used in DefaultPersuasionModel.GetDefaultSuccessChance

```
VeryEasy
Easy
EasyMedium
Medium
MediumHard
Hard
VeryHard
UltraHard
Impossible
```

## PersuasionArgumentStrength

```
ExtremelyHard = -3
VeryHard
Hard
Normal
Easy
VeryEasy
ExtremelyEasy
```

Make sure you are not going to -4, like ExtremelyHard - 1, that way it overflows to Normal

Possible function to clamp values:

```
PersuasionArgumentStrength GetPersuasionArgumentStrength(int value)
{
    PersuasionArgumentStrength persuasionArgumentStrength = PersuasionArgumentStrength.Normal;

    if (value < -2) return PersuasionArgumentStrength.ExtremelyHard;
    if (value == -2) return PersuasionArgumentStrength.VeryHard;
    if (value == -1) return PersuasionArgumentStrength.Hard;
    if (value == 1) return PersuasionArgumentStrength.Easy;
    if (value == 2) return PersuasionArgumentStrength.VeryEasy;
    if (value > 2) return PersuasionArgumentStrength.ExtremelyEasy;

    return persuasionArgumentStrength;
}
```

Impact on persuasion argument:

With PersuasionDifficulty.Medium:

![](https://docs.bannerlordmodding.lt/pics/cVU0hOH.png)

## PersuasionOptionArgs

```
PersuasionOptionArgs(
    SkillObject skill,
    TraitObject trait,
    TraitEffect traitEffect,
    PersuasionArgumentStrength argumentStrength,
    bool givesCriticalSuccess,
    TextObject line,
    Tuple<TraitObject, int>[] traitCorrelation = null,
    bool canBlockOtherOption = false,
    bool canMoveToTheNextReservation = false,
    bool isInitiallyBlocked = false)
```
