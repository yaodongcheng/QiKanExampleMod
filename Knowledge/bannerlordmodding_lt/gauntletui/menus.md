# Menus

<!-- 源: https://docs.bannerlordmodding.lt/gauntletui/menus/ | 抓取日期: 2026-09-09 -->

## Tutorials/Resources

* [Lesser Scholar - Ep 14: Quick Way to edit Menus](https://www.youtube.com/watch?v=WIsGqcGOeZQ)
* [Main Game Menu editing](https://www.nexusmods.com/mountandblade2bannerlord/mods/5233)

## Basic example

Adds new menu option into Village menu

```
public override void RegisterEvents()
{
    CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
}

private void OnSessionLaunched(CampaignGameStarter starter)
{
    AddGameMenus(starter);
}

private void AddGameMenus(CampaignGameStarter starter)
{
    starter.AddGameMenuOption("village", "test_menu", "My test menu",
    (MenuCallbackArgs args) => {
        args.optionLeaveType = GameMenuOption.LeaveType.HostileAction;
        args.Tooltip = new TextObject("{GOLD_ICON} Tooltip! {GOLD_ICON}", null);
        return true;
    },
    delegate (MenuCallbackArgs args)
    {
        InformationManager.DisplayMessage(new InformationMessage("My menu works!"));
    }, false, 1, false);
}
```

## Another example

Town menu:

```
private void AddHolyPlaceMenus(CampaignGameStarter starter)
{
    starter.AddGameMenuOption("town", "holy_place", "Go to the {HOLY_PLACE}",
    (MenuCallbackArgs args) => {
        args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
        // set dynamic holy place name - church, mosque, etc
        MBTextManager.SetTextVariable("HOLY_PLACE", "holy place", false);
        return true;
    },
    delegate (MenuCallbackArgs args)
    {
        GameMenu.SwitchToMenu("holy_place");
    }, false, 1, false);

    starter.AddGameMenu("holy_place", "{MENU_TEXT}",
    (MenuCallbackArgs args) => {

        // set proper background picture
        // args.MenuContext.SetBackgroundMeshName("some_bg_picture");

        // set dynamic menu text
        MBTextManager.SetTextVariable("MENU_TEXT", "You are in the holy place...", false);
    }, TaleWorlds.CampaignSystem.Overlay.GameOverlays.MenuOverlayType.SettlementWithBoth);

    starter.AddGameMenuOption("holy_place", "holy_place_enter", "Enter inside", (MenuCallbackArgs args) =>
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Mission;
        return true;
    }, (MenuCallbackArgs args) =>
    {
        InformationManager.DisplayMessage(new InformationMessage("Closed!"));
    }, true);

    starter.AddGameMenuOption("holy_place", "leave", "Leave", (MenuCallbackArgs args) =>
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Leave;
        return true;
    }, (MenuCallbackArgs args) => { GameMenu.SwitchToMenu("town"); }, true);
}
```

## Open menu

```
GameMenu.SwitchToMenu("holy_place");
```

## Menus in-game

* "town"
  + "town\_keep"
  + "town\_arena"
  + "town\_backstreet" Tavern District
* "castle"
* "village"

## AddGameMenu

```
public void AddGameMenu(
    string menuId,
    string menuText,
    OnInitDelegate initDelegate,
    GameOverlays.MenuOverlayType overlay = GameOverlays.MenuOverlayType.None,
    GameMenu.MenuFlags menuFlags = GameMenu.MenuFlags.None,
    object relatedObject = null)
```

### Background Image

```
args.MenuContext.SetBackgroundMeshName("wait_raiding_village");
```

Defined in Modules\Native\GUI\NativeSpriteData.xml

Can use custom 445x805 sprite there.

* [Settlements Menu Image](/modding/settlements/#wait_mesh)
* [Encounters](/modding/cultures/#xml) (encounter\_background\_mesh in cultures.xml)

### MenuOverlayType

`TaleWorlds.CampaignSystem.Overlay.GameOverlays.MenuOverlayType.SettlementWithBoth`

Possible types:

```
public enum MenuOverlayType
{
    None,
    SettlementWithParties,
    SettlementWithCharacters,
    SettlementWithBoth,
    Encounter
}
```

![](https://docs.bannerlordmodding.lt/pics/2411231500.jpg)

## AddGameMenuOption

![](https://docs.bannerlordmodding.lt/pics/i2CQmtK.png)

```
public void AddGameMenuOption(
    string menuId,
    string optionId,
    string optionText,
    GameMenuOption.OnConditionDelegate condition,
    GameMenuOption.OnConsequenceDelegate consequence,
    bool isLeave = false,
    int index = -1,
    bool isRepeatable = false)
```

* bool isLeave = false - if True, by pressing Tab goes back to previous menu or to the map
* int index = -1 - order in the menu (some options can be hidden, so will not always show in the exact index you define), -1 - at the very end

### Enabled/Disabled

```
args.IsEnabled = false;
```

### Tooltip

```
args.Tooltip = new TextObject("{=yNMrF2QF}You are wounded", null);
```

### Icon

```
args.optionLeaveType = GameMenuOption.LeaveType.ICON_NAME;
```

Defined in `\Modules\Native\GUI\Brushes\GameMenu.xml`

ICON\_NAMEs 1.1.0+

![](https://docs.bannerlordmodding.lt/pics/bWOtObC.png)

ICON\_NAMEs 1.0.3

![](https://docs.bannerlordmodding.lt/pics/DCeLFMO.png)

## Back to previous menu

```
GameMenu.ExitToLast();
```

## Menu with waiting

Basic example:

```
    private static CampaignTime _menuWaitStart = CampaignTime.Now;
    float _waitHours = 10f;

    campaignGameStarter.AddWaitGameMenu("wait_menu_name", "top text", delegate (MenuCallbackArgs args)
    {
        // _waitHours can be changed here dynamically, applies for each new menu

        // how long to wait
        args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(_waitHours, 0f);
        args.MenuContext.SetBackgroundMeshName("captive_at_sea_escape");        // menu background picture

        _menuWaitStart = CampaignTime.Now;
    }, delegate (MenuCallbackArgs args) // condition
    {
        return true;
    }, delegate (MenuCallbackArgs args) // consequence
    {
        GameMenu.ExitToLast();
    }, delegate (MenuCallbackArgs args, CampaignTime dt) // tick
    {
        args.MenuContext.GameMenu.SetProgressOfWaitingInMenu((float)_menuWaitStart.ElapsedHoursUntilNow / _waitHours);
        if ((float)_menuWaitStart.ElapsedHoursUntilNow > 1f) args.MenuContext.SetBackgroundMeshName("baltic_alley");    // can change bg here dynamically
    }, GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption, GameMenu.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);

    // if this AddGameMenuOption is missing - progress bar will not be visible
    campaignGameStarter.AddGameMenuOption("wait_menu_name", "stop_waiting", "Stop waiting", delegate (MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Leave;  // .Default - no icon
        return true;
    }, delegate (MenuCallbackArgs args)
    {
        GameMenu.ExitToLast();  // exit from menu
        //GameMenu.ActivateGameMenu("town");    // go to other menu
    },
    true, // (bool isLeave) if true here and there is no GameMenu.ExitToLast() above or similar -> wait menu will hang and game will need a reload
          // if no action is needed - make it false here to avoid hang
    -1, false);
```

args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(100); - stops the waiting (even if previously wait time is not reached yet)

### ProgressBar

* GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption - only progress bar
* GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption - writes total wait hours on top of progress bar

Progressbar will not be shown if there are no AddGameMenuOption after the AddWaitGameMenu

## Sound in menus

```
args.MenuContext.SetPanelSound("event:/ui/panels/settlement_village");
args.MenuContext.SetAmbientSound("event:/map/ambient/node/settlements/2d/village");
```

## Which menu is open?

```
if (Campaign.Current.CurrentMenuContext != null)
{
    string currentMenuId = Campaign.Current.CurrentMenuContext.GameMenu.StringId;
}
else
{
    // no menu is open
}
```
