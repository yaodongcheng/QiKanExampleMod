# Custom Culture

<!-- 源: https://docs.bannerlordmodding.lt/guides/custom_culture/ | 抓取日期: 2026-09-09 -->

WARNING: This guide is for v1.2.12 with some adjustments for 1.3 until fully converted

![](https://docs.bannerlordmodding.lt/pics/c2soKfU.png)

1.3+

This patch adds only your cultures to the selection screen:

```
[HarmonyPatch(typeof(CharacterCreationCampaignBehavior), "InitializeCharacterCreationCultures")]
public class AddOnlyOurCulturesPatch
{
    // add only our cultures to the selection screen, skip the original method
    static bool Prefix(CharacterCreationManager characterCreationManager)
    {
        foreach (CultureObject cultureObject in Game.Current.ObjectManager.GetObjectTypeList<CultureObject>())
        {
            if (cultureObject.StringId == "baltic" || cultureObject.StringId == "crusader" || cultureObject.StringId == "danish" || cultureObject.StringId == "rus" || cultureObject.StringId == "polish") // Replace with your culture's StringId
            {
                characterCreationManager.CharacterCreationContent.AddCharacterCreationCulture(cultureObject, 1, 10);
            }
        }
        return false;
    }
}
```

Sort cultures by name

```
// sorts cultures by name in the Culture selection list
[HarmonyPatch(typeof(CharacterCreationCultureStageVM), "SortCultureList")]
public static class SortCultureList_ByNameText_Prefix
{
    static bool Prefix(MBBindingList<CharacterCreationCultureVM> listToWorkOn)
    {
        listToWorkOn.Sort(new NameTextComparer());
        return false;
    }

    private sealed class NameTextComparer : IComparer<CharacterCreationCultureVM>
    {
        public int Compare(CharacterCreationCultureVM x, CharacterCreationCultureVM y)
        {
            // Prefer NameText; fallback to ShortenedNameText; then Culture.StringId
            string sx = x?.NameText ?? x?.ShortenedNameText ?? x?.Culture?.StringId ?? string.Empty;
            string sy = y?.NameText ?? y?.ShortenedNameText ?? y?.Culture?.StringId ?? string.Empty;
            return StringComparer.InvariantCultureIgnoreCase.Compare(sx, sy);
        }
    }
}
```

1.2.12

On new game start character culture can be selected if it's marked in the culture XML as is\_main\_culture="true":

```
<Culture
    id="baltic"
    name="Baltic"
    is_main_culture="true"
```

## Culture's Selection Cards

![](https://docs.bannerlordmodding.lt/pics/2512281121.png)

1.2.12

![](https://docs.bannerlordmodding.lt/pics/dAbzEt7.png)

The culture's name that is visible under the picture is defined in the module\_strings.xml :

```
<string id="str_culture_rich_name.baltic" text="Baltic" />
```

![](https://docs.bannerlordmodding.lt/pics/nnjvduV.png)

Image dimensions for the culture's picture in 1.3+: 541 x 94 (1.2.12: 366 x 668)

1.2.12

I leave some transparent space at the bottom to not cover the culture's name.

Also round the corners, so the highlight would not look weird.

![](https://docs.bannerlordmodding.lt/pics/cPK9nuC.png)

These images should be named the same as culture ID:

```
<Culture
    id="baltic"
```

Image name: baltic.png

Sprites should be [imported](/gauntletui/sprites/#import) from:

```
\MODULE_NAME\GUI\SpriteParts\ui_charactercreation\CharacterCreation\Culture\
```

That means that we are overwriting the native sprite category: `ui_charactercreation`

and vanilla culture images will be gone. If we want them back - change `ui_charactercreation` category to something like: `ui_my_charactercreation`

The game uses these sprites for your new cultures automatically - nothing to be done here anymore.

### Remove Native Cultures (1.2.12)

To get rid of native cultures from this selection:

![](https://docs.bannerlordmodding.lt/pics/2402281143.png)

It is necessary to remove them completely from the game (quite hard with all the relations/units/etc) or make: is\_main\_culture=false in their XMLs using [XSLT](/modding/xml/#xslt):

```
<xsl:template match="Culture[@id='vlandia']/@is_main_culture">
    <xsl:attribute name="is_main_culture">false</xsl:attribute>
</xsl:template>

<xsl:template match="Culture[@id='empire']/@is_main_culture">
    <xsl:attribute name="is_main_culture">false</xsl:attribute>
</xsl:template>

<xsl:template match="Culture[@id='aserai']/@is_main_culture">
    <xsl:attribute name="is_main_culture">false</xsl:attribute>
</xsl:template>

<xsl:template match="Culture[@id='sturgia']/@is_main_culture">
    <xsl:attribute name="is_main_culture">false</xsl:attribute>
</xsl:template>

<xsl:template match="Culture[@id='battania']/@is_main_culture">
    <xsl:attribute name="is_main_culture">false</xsl:attribute>
</xsl:template>

<xsl:template match="Culture[@id='khuzait']/@is_main_culture">
    <xsl:attribute name="is_main_culture">false</xsl:attribute>
</xsl:template>
```

Also it's necessary to disable one method in the code using Harmony:

```
// No sorting/expecting of native cultures
[HarmonyPatch(typeof(CharacterCreationCultureStageVM))]
[HarmonyPatch("SortCultureList")]
public class CharacterCreationCultureStageVM_SortCultureList_Patch
{
    public static bool Prefix()
    {
        return false;
    }
}
```

### Custom Music

When selecting different culture's card. Details [here](/modding/sound/#custom-music-in-culture-selection-menu)

## Culture's Description

![](https://docs.bannerlordmodding.lt/pics/SYqApNY.png)

The moving images are constructed out of 4 sprites. Each of them is stacked on top of another as visible in this example:

![](https://docs.bannerlordmodding.lt/pics/2zkCAS4.png)

4 at the bottom, does not move. Then sprite #3 - moves very slightly, #2 - moves more and #1 on the top, moves the most.

These sprites move horizontally at different speeds trying to create an illusion.

We will need 4 separate images (with some transparency to not cover each other completely) with the names: CULTURE\_1, CULTURE\_2, CULTURE\_3, CULTURE\_4, where CULTURE is your culture's ID, for example: baltic\_1, baltic\_2, baltic\_3, baltic\_4

Dimensions for a picture: 952 x 876

Leave some transparent space at the top/bottom for a better look. The full image start at the top of the screen and goes behind the text - not very nice.

These sprites should be imported from the same folder:

```
\MODULE_NAME\GUI\SpriteParts\ui_charactercreation\CharacterCreation\Culture\
```

Then we must create custom brushes to allow the game to use our newly created sprites.

For that we need to copy

```
\Mount & Blade II Bannerlord\Modules\Native\GUI\Brushes\CharacterCreation.xml
```

to

```
\OUR_MOD\GUI\Brushes\CharacterCreation.xml
```

and add our sprites to brushes: "Culture.Banner.Layer.4", "Culture.Banner.Layer.3", "Culture.Banner.Layer.2", "Culture.Banner.Layer.1".

Example for "Culture.Banner.Layer.3":

```
  <Brush Name="Culture.Banner.Layer.3">
    <Layers>
      <BrushLayer Name="Default"  />
    </Layers>
    <Styles>
      <Style Name="baltic">
        <BrushLayer Name="Default" Sprite="CharacterCreation\Culture\baltic_3" />
      </Style>
```

And that will be all to show our custom cultures pictures.

Demo:

## You were born into a family of... (1.2.12)

With the custom culture, this window is empty by default:

![](https://docs.bannerlordmodding.lt/pics/NyqeknN.png)

because menu selections are hardcoded for vanilla cultures only.

Click here to see menus for vanilla cultures

![](https://docs.bannerlordmodding.lt/pics/61myENz.png)
![](https://docs.bannerlordmodding.lt/pics/PuOXRlv.png)
![](https://docs.bannerlordmodding.lt/pics/feLgRDw.png)
![](https://docs.bannerlordmodding.lt/pics/Gdu5u5J.png)
![](https://docs.bannerlordmodding.lt/pics/co4ShqN.png)
![](https://docs.bannerlordmodding.lt/pics/hiHlbOV.png)

To make this menu work for your custom culture we need a simple Harmony patch (1.2.12):

```
[HarmonyPatch(typeof(SandboxCharacterCreationContent))]
[HarmonyPatch("VlandianParentsOnCondition")]
public class SandboxCharacterCreationContentVlandianParentsOnConditionPatch
{
    public static void Postfix(ref bool __result, SandboxCharacterCreationContent __instance)
    {
        if (__instance.GetSelectedCulture().StringId == "baltic") __result = true;
    }
}
```

This patch tells to use the Vlandia menu for custom baltic culture.

Select the best vanilla culture for your custom culture and apply this patch with the necessary changes to have this menu working.

## Coronation scene

Without changes coronation scene could look like this with the new culture:

![](https://docs.bannerlordmodding.lt/pics/2409180854.png)

The default troop id for the coronation guards is 'fighter\_sturgia' - so if you removed it - guards will have no clothes.

Use this patch to fix this, change cultures/troopIDs in the example to yours:

```
[HarmonyPatch(typeof(CampaignSceneNotificationHelper), "GetBodyguardOfCulture")]
public class CampaignSceneNotificationHelper_GetBodyguardOfCulture_Patch
{
    private static bool Prefix(ref SceneNotificationData.SceneNotificationCharacter __result, CultureObject culture)
    {
        string stringId = culture.StringId;
        string troopId = "balt_14";
        if (stringId == "baltic")
        {
            troopId = "balt_21";
        }
        else if (stringId == "crusader")
        {
            troopId = "crusader_26";
        }
        else if (stringId == "rus")
        {
            troopId = "rus_20";
        }
        __result = new SceneNotificationData.SceneNotificationCharacter(MBObjectManager.Instance.GetObject<CharacterObject>(troopId), null, default(BodyProperties), false, uint.MaxValue, uint.MaxValue, false);           
        return false;
    }
}
```

The title in the string should be set with such text variables in module\_strings.xml for each of your new culture:

```
<string id="str_liege_title.baltic" text="{=str_liege_title.baltic}Grand Duke" />
<string id="str_liege_title_female.baltic" text="{=str_liege_title_female.baltic}Grand Duchess" />
```

Fixed scene:

![](https://docs.bannerlordmodding.lt/pics/2409180859.png)

## Dialog Text/Voices

Make your culture's NPCs talk in different dialects.

Change your culture's accordingly.

```
[HarmonyPatch(typeof(ConversationManager), "FindMatchingTextOrNull")]
public static class ConversationManager_FindMatchingTextOrNull_Patch
{
    static void Prefix(CharacterObject character, out CultureObject __state)
    {
        // Save the original culture
        __state = character.Culture;

        // Temporarily assign the fake culture
        string fakeCulture = "";
        if (__state.StringId == "baltic" || __state.StringId == "latvian" || __state.StringId == "estonian")
        {
            fakeCulture = "battania";
        }
        else if (__state.StringId == "crusader")
        {
            fakeCulture = "vlandia";
        }
        else if (__state.StringId == "danish")
        {
            fakeCulture = "empire";
        }
        else if (__state.StringId == "rus" || __state.StringId == "polish")
        {
            fakeCulture = "sturgia";
        }
        // aserai / khuzait

        if (fakeCulture != "") character.Culture = Game.Current.ObjectManager.GetObjectTypeList<CultureObject>().FirstOrDefault(c => c.StringId == fakeCulture);
    }

    // Restore the original culture
    static void Postfix(CharacterObject character, CultureObject __state)
    {
        character.Culture = __state;
    }
}
```

Some users reported that this patch does not always work properly. Use it at your own risk.

## Other changes

* [Menu backgrounds](/guides/custom_menu_background/)
* [Starting positions](/guides/custom_start_positions/)
