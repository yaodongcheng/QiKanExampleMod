# Map

<!-- 源: https://docs.bannerlordmodding.lt/modding/map/ | 抓取日期: 2026-09-09 -->

## Settlement Nameplates

```
SettlementNameplateVM
SettlementNameplateItem.xml
```

[Example](https://github.com/Litauen/LT_Education/tree/master/UI/NameplateIcon) how to modify it. Adds additional icon on condition.

## Track Settlements

```
// do not mark already tracked settlements
if (!Campaign.Current.VisualTrackerManager.CheckTracked(settlement))
{
    Campaign.Current.VisualTrackerManager.RegisterObject(settlement);
}

RemoveTrackedObject(ITrackableBase obj, bool forceRemove = false)
```

Can be marked several times to be tracked. In that case needs several clicks to remove tracking.
