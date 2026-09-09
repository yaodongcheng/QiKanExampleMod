# Items

<!-- 源: https://docs.bannerlordmodding.lt/modding/items/ | 抓取日期: 2026-09-09 -->

* [ItemObject TaleWorlds.Core.ItemObject Class Reference](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_core_1_1_item_object.html)

## Tutorials

* [Creating Custom Items - Part 1](https://www.youtube.com/watch?v=U-qAa4cmf28&list=PLxhni8XI_dRDjRRDsCzEBZg4eUInzkzQT&index=2)
* [Creating Custom Items - Part 2](https://www.youtube.com/watch?v=IKyJD9dzTEI&list=PLxhni8XI_dRDjRRDsCzEBZg4eUInzkzQT&index=3)

## ItemObject

```
ItemObject grain = MBObjectManager.Instance.GetObject<ItemObject>("grain");
```

Trade Items:

beer  
butter  
cheese  
date\_fruit  
fish  
grape  
grain  
meat  
olives

charcoal  
ironIngot1 (Crude Iron)  
ironIngot2 (Wrought Iron)  
ironIngot3 (Iron)  
ironIngot4 (Steel)  
ironIngot5 (Fine Steel)  
ironIngot6 (Thamaskene Steel)

clay  
cotton (Raw Silk)  
flax  
fur  
hardwood  
hides  
jewelry  
leather  
linen  
oil  
pottery  
salt  
silver  
spice  
tools  
velvet  
wine  
wool

cat  
dog  
chicken  
goose  
hog  
sheep  
cow

stolen\_goods

planks (1.3)  
walrus\_tusk (DLC)  
whale\_oil (DLC)

## ItemRoster

* [API](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_campaign_system_1_1_roster_1_1_item_roster.html)

Class to describe all items the Party/Town/Hero has.

### Properties

```
int     Count [get]
float   TotalWeight [get]
int     TotalFood [get]
int     FoodVariety [get]
int     TotalValue [get]
int     TradeGoodsTotalValue [get]
int     NumberOfPackAnimals [get]
int     NumberOfLivestockAnimals [get]
int     NumberOfMounts [get]
```

### Examples

Count all food items in the ItemRoster

```
int itemCount = 0;
for (int i = 0; i < itemRoster.Count; i++)
{
    ItemObject item = itemRoster.GetItemAtIndex(i);
    if (item.IsFood)
    {
        itemCount += itemRoster.GetItemNumber(item);
    }
}
```

How many grain player party has:

```
PartyBase.MainParty.ItemRoster.GetItemNumber(MBObjectManager.Instance.GetObject<ItemObject>("grain"));
```

How many grain settlement has:

```
Settlement.ItemRoster.GetItemNumber(MBObjectManager.Instance.GetObject<ItemObject>("grain"));
```

Check how much of item X the party has example method

```
public static int GetPartyItemCount(MobileParty party, string itemID)
{
    if (party == null || party.ItemRoster == null) return 0;
    if (itemID.Length == 0) return 0;

    ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(itemID);
    if (item == null) return 0;

    return party.ItemRoster.GetItemNumber(item);
}
```

Check if party has X items example method

```
public static bool PartyHasItemCount(MobileParty party, string itemID, int count)
{
    if (party == null || party.ItemRoster == null) return false;
    if (itemID.Length == 0) return false;
    if (count < 0) return false;

    ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(itemID);
    if (item == null) return false;

    if (party.ItemRoster.GetItemNumber(item) >= count) return true;

    return false;
}
```

Add Item to ItemRoster

```
ItemObject butterItemObject = MBObjectManager.Instance.GetObject<ItemObject>("butter");
if (butterItemObject != null) {
    ItemRosterElement itemRoster = new ItemRosterElement(butterItemObject, 2, null);
    MobileParty.MainParty.ItemRoster.Add(itemRoster);
}
```

Add X items to the party method example

```
public static void AddItemsToParty(MobileParty party, string itemID, int count)
{
    if (party == null || party.ItemRoster == null) return;
    if (itemID.Length == 0) return;
    if (count < 0) return;

    ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(itemID);
    if (item == null) return;

    party.ItemRoster.Add(new ItemRosterElement(item, count, null));
}
```

Remove

```
ItemRosterElement itemRosterElement = new ItemRosterElement(itemObject, 10, null);
settlement.Stash.Remove(itemRosterElement);     // from settlement's stash
party.ItemRoster.Remove(itemRosterElement);     // from party
```

Remove X items from the party example method

```
public static void RemoveItemsFromParty(MobileParty party, string itemID, int count)
{
    if (party == null || party.ItemRoster == null) return;
    if (itemID.Length == 0) return;
    if (count < 0) return;

    ItemObject item = MBObjectManager.Instance.GetObject<ItemObject>(itemID);
    if (item == null) return;

    party.ItemRoster.Remove(new ItemRosterElement(item, count, null));
}
```

## Sell

```
SellItemsAction.Apply(mobileParty.Party, settlement.Party, itemRosterElement, number, settlement);
```

## Price

Returns weird price, not the same as in the Trade screen.

town.GetItemPrice or village.GetItemPrice:

```
int GetItemPrice(ItemObject item, MobileParty tradingParty = null, bool isSelling = false)
int GetItemPrice(EquipmentElement itemRosterElement, MobileParty tradingParty = null, bool isSelling = false)
```

This returns the same price as in the Trade screen.

town.MarketData.GetPrice or village.MarketData.GetPrice

```
int GetPrice(ItemObject item, MobileParty tradingParty, bool isSelling, PartyBase merchantParty)

int itemPrice = village.MarketData.GetPrice(item, MobileParty.MainParty, false, village.Settlement.Party);
```

item.value - returns default item's value (from XML?)

## Tier

```
Public enum ItemTiers
{
    Tier1,
    Tier2,
    Tier3,
    Tier4,
    Tier5,
    Tier6,
    NumTiers
}
```

Tier 0 (in GUI) == -1 when comparing with ItemTier.

For a head armor Tier is set automatically based on the <ItemComponent><Armor head\_armor value

By price for other types of armors also:

![](https://docs.bannerlordmodding.lt/pics/2402160845.png)

Ulfkarl: Can be set manually in the items.xml with tier\_override= (1..6)

## InventoryManager

Screen(s) to trade/exchange items

```
InventoryManager.OpenScreenAsReceiveItems(itemRoster, new TextObject("Top Left Name"), new InventoryManager.DoneLogicExtrasDelegate(OnInventoryScreenDone));
```

More functions

```
public static void OpenScreenAsInventoryOfSubParty(MobileParty rightParty, MobileParty leftParty, DoneLogicExtrasDelegate doneLogicExtrasDelegate) // not used in-game, crash on exit
public static void OpenScreenAsInventoryForCraftedItemDecomposition(MobileParty party, CharacterObject character, DoneLogicExtrasDelegate doneLogicExtrasDelegate)
public static void OpenScreenAsInventory(DoneLogicExtrasDelegate doneLogicExtrasDelegate = null
public static void OpenScreenAsReceiveItems(ItemRoster items, TextObject leftRosterName, DoneLogicExtrasDelegate doneLogicDelegate = null)
public static void OpenScreenAsTrade(ItemRoster leftRoster, SettlementComponent settlementComponent, InventoryCategoryType merchantItemType = InventoryCategoryType.None, DoneLogicExtrasDelegate doneLogicExtrasDelegate = null)
public static void OpenScreenAsInventoryOf(MobileParty party, CharacterObject character)
public static void OpenScreenAsInventoryOf(PartyBase rightParty, PartyBase leftParty)
public static void OpenScreenAsLoot(Dictionary<PartyBase, ItemRoster> itemRostersToLoot)
public static void OpenScreenAsStash(ItemRoster stash)
public static void OpenTradeWithCaravanOrAlleyParty(MobileParty caravan, InventoryCategoryType merchantItemType = InventoryCategoryType.None)
```

Crash if used in mission

Type: System.AccessViolationException
Message: Attempted to read or write protected memory. This is often an indication that other memory is corrupt.
Stacktrace:
at void ManagedCallbacks.ScriptingInterfaceOfIMBMission.GetBoundaryPoints(UIntPtr missionPointer, string name, int boundaryPointOffset, Vec2[] boundaryPoints, int boundaryPointsSize, ref int retrievedPointCount)
at List TaleWorlds.MountAndBlade.Mission+MBBoundaryCollection.GetBoundaryPoints(string name)
at bool TaleWorlds.MountAndBlade.Mission+MBBoundaryCollection.ContainsKey(string name

## Get Items in-game

Trade goods: Items.AllTradeGoods  
Other: Items.All.Where(item => (whatever condition you want))

```
Items.All.Where(item => item.Type == ItemObject.ItemTypeEnum.Goods && !item.StringId.Contains("book")))

// get ordered by Name
var orderedItems = Items.All
  .Where(item => item.Type == ItemObject.ItemTypeEnum.Goods && !item.StringId.Contains("book"))
  .OrderBy(item => item.Name.ToString(), StringComparer.Ordinal);

foreach (ItemObject item in orderedItems) { // do something with the items }
```

## XML

### Folder

```
\Modules\SandBoxCore\ModuleData\items

Basic trade goods described in this file:
\Modules\SandBoxCore\ModuleData\items\horses_and_others.xml
```

For mods:

```
\MODFolder\ModuleData
```

PROBLEM: Items are not loaded from XML

Check your engine logs for item read errors/warning. As I said before a new behavior that I experienced with 1.2++ versions of the game that if you have a faulty item, all the item definitions coming after it will not load either, not just the one that is bad. So maybe there has always been one that was bad, you just didnt notice it among the hundreds of items. ([hunharibo](https://discord.com/channels/411286129317249035/677511186295685150/1187684885457010690))

### Properties

* 'mesh' - 3D mesh
* 'using\_tableau' - true if an item (usually body armor) has banner/sigil on it
* 'is\_merchandise' - is used to determine whether an item is a merchandise or not. Used in the game code to determine if Item can be lost, on deciding rewards and something else
* 'appearance' - used in calculating the item value (CalculateValue): return (int)(num2 \* num \* (1f + 0.2f \* (item.Appearance - 1f)) + 100f \* MathF.Max(0f, item.Appearance - 1f));
* 'value' - price
* 'weight'
* 'culture' (optional) - Defined culture of the current item. If an item is set for a culture, the item will only be sold on the markets of the same culture. Can't have multiple values of the 'culture' attribute, so that the product is available for sale in towns of only two cultures. You can set it to neutral culture, but then it will show up everywhere.
* 'difficulty' - (need confirmation) is used to indicate the level of difficulty in crafting an item
* 'item\_category' - (need confirmation) influences price range based on the market. Example - if item\_category="jewelry" and item value="1000", in-game item can cost from ~1000 at start and drop to 150 after few weeks. Because jewelry prices drop in the region. If this item\_category is changed to let's say 'book', then in-game same item costs ~1000 again.
* 'subtype' - is used to provide additional information about the type of item being defined. The subtype property is often used in conjunction with the item\_category property to further refine the classification of an item
* 'covers\_body' - true/false, property for an armor to tell if body should be hidden (true) or not (false)
* 'no\_slim' - to hand armors to prevent using the slim version of body armors (from v1.4b)

### ItemObject

Required skill/value:

```
ItemObject.RelevantSkill, ItemObject.Difficulty
```

### Type

ItemTypeEnum

* Invalid
* Horse
* OneHandedWeapon
* TwoHandedWeapon
* Polearm
* Arrows
* Bolts
* Shield
* Bow
* Crossbow
* Thrown
* Goods
* HeadArmor
* BodyArmor
* LegArmor
* HandArmor
* Pistol
* Musket
* Bullets
* Animal
* Book
* ChestArmor
* Cape
* HorseHarness
* Banner

Type="Goods" sets ItemObject.IsTradeGood == true and ItemObject.ItemType == "Goods"

Caravan Ambush mission

Gives 3 random "Goods" type items as a reward. So if your item type is "Goods", they will be given at some point.

Q. How to make certain items not appear in the lootpool and also in the marketplace?   
A. Set is\_merchandise="false" and assign to the culture that does not have markets, e.g. Looters/Bandits (Culture.looter)

It appears that items that are not for sale (is\_merchandise="false") may appear as prizes in tournaments.
But it probably depends on the cost of the item - the higher it is, the greater the likelihood.

### Broken Item

[PlebeianKilo [ER]](https://discord.com/channels/411286129317249035/697071240015380500/1215514757331943424):

Since 1.2.x, if a single item is broken, everything past that won't get loaded

So lets say you have a list of 100 items. If item 32 is broken, everything after doesn't appear in game.

If you want to more easily find out which it is check your logs in programdata (its a hidden folder so you gotta make hidden folders visible) and search for null or invalid.

![](https://docs.bannerlordmodding.lt/pics/2403080814.png)

### Transparent Body

![](https://docs.bannerlordmodding.lt/pics/202507070944.png)

Use `covers_body="false"` in items.xml for that item

## How to find ItemID

Let's say we want to find ItemID for this item:

![](https://docs.bannerlordmodding.lt/pics/hkirJAc.png)

Easy way: use this [mod](https://www.nexusmods.com/mountandblade2bannerlord/mods/4030)

Hard way: Use [ripgrep](/resources/tools/) in the game's folder:

```
rg -C 5 "Thinhide Coif"
```

And you will see (among a lot of other stuff):

```
Modules\SandBoxCore\ModuleData\items\head_armors.xml
869-  </Item>
870-  <!-- #region Battania Armors -->
871-  <!-- #region Battania Head Armors -->
872-  <!-- #region Battania Light Head Armors -->
873-  <Item id="thinhide_coif"
874:        name="{=Q50hfNjW}Thinhide Coif"
875-        subtype="head_armor"
876-        mesh="battania_helmet_a"
877-        culture="Culture.battania"
878-        weight="0.5"
879-        difficulty="0"
```

This is what you need:

```
<Item id="thinhide_coif"
```

Then you can use it in your mod. Example:

```
<Equipments>
    <EquipmentRoster civilian="true">
        <equipment slot="Head"
            id="Item.thinhide_coif" />
```

### ...and mesh

Mesh (3D model's name) usually is different from the ItemID, and you can find it in the item's definition in the XML:

```
876-        mesh="battania_helmet_a"
```

You will need it for export from [TpacTool](/resources/tpactool/) and working with 3D program such as Blender.

## Remove Native Items

Example [here](/modding/xml/#example-deleteremove-native-items)

## Helmet Hair Coverage

![](https://docs.bannerlordmodding.lt/pics/Iq2hAG0.png)

## Beard Coverage

![](https://docs.bannerlordmodding.lt/pics/2507120905.png)
