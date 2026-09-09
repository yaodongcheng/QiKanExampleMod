# Custom Dialog Backgrounds

<!-- 源: https://docs.bannerlordmodding.lt/guides/custom_dialog_backgrounds/ | 抓取日期: 2026-09-09 -->

![](https://docs.bannerlordmodding.lt/pics/2404160933.jpg)

I was unable to change dialog backgrounds for town scenes and lordhalls(keeps) with custom cultures yet. Details bellow.

## Structure

### Cubemaps

These backgrounds are a low quality projections on a cube - [cubemaps](https://en.wikipedia.org/wiki/Cube_mapping).

![](https://docs.bannerlordmodding.lt/pics/2404160941.png)

With TpacTool we can see them under Texture with search 'conv\_':

![](https://docs.bannerlordmodding.lt/pics/2404160944.jpg)

TpacTool does not want to export to PNG, exporting to DDS, exports 6 2048x2048 images for specified cubemap.

### Atmospheres

These cubemaps/skyboxes/textures are used in the atmosphere files:

![](https://docs.bannerlordmodding.lt/pics/2404160955.jpg)

Example for conv\_snow\_noon\_skybox:

```
<atmosphere>
<values>
    <value name="name" value="conv_snow_noon_0"/>
    <value name="cloud_amount" value="0.100"/>
    <value name="middle_gray" value="0.150"/>
    <value name="temperature" value="40.000"/>
    <value name="humidity" value="0.000"/>
    <value name="color_grade_name" value="cg_blue_20"/>
    <value name="time_of_day" value="8.000"/>
    <value name="fall_density" value="0.000"/>
    <value name="snow_density" value="0.000"/>
    <value name="is_indoor" value="false"/>
    <value name="global_envmap_multiplier" value="1.000"/>
    <value name="global_envmap_color_factor" value="0.659, 0.820, 1.000"/>
    <value name="prt_multiplier" value="1.000"/>
    <value name="do_not_render_envmap" value="true"/>
    <value name="skybox_background_texture_name" value="conv_snow_noon_skybox"/>
    <value name="skybox_sun_texture_name" value="sun_core2"/>
    <value name="lens_flare_dirt_texture_name" value="lens_dirt"/>
    <value name="lens_flare_star_texture_name" value="lens_star"/>
    <value name="skybox_panaroma_type" value="2"/>
    <value name="season" value="summer"/>
</values>
...
```

### Scene

The scene *scn\_conversation\_tableau* used in the dialogs are here:

![](https://docs.bannerlordmodding.lt/pics/2404160959.jpg)

Editor crashes for me when I try to open this scene

### Code

This code decides what atmosphere to use:

```
public class DefaultMapConversationDataProvider : IMapConversationDataProvider
{
    string IMapConversationDataProvider.GetAtmosphereNameFromData(MapConversationTableauData data)
    {
        string text;
        if (data.TimeOfDay <= 3f || data.TimeOfDay >= 21f)
        {
            text = "night";
        }
        else if (data.TimeOfDay > 8f && data.TimeOfDay < 16f)
        {
            text = "noon";
        }
        else
        {
            text = "sunset";
        }
        if (data.Settlement == null || data.Settlement.IsHideout)
        {
            if (data.IsCurrentTerrainUnderSnow)
            {
                return "conv_snow_" + text + "_0";
            }
            switch (data.ConversationTerrainType)
            {
            case TerrainType.Steppe:
                return "conv_steppe_" + text + "_0";
            case TerrainType.Desert:
                return "conv_desert_" + text + "_0";
            case TerrainType.Forest:
                return "conv_forest_" + text + "_0";
            }
            return "conv_plains_" + text + "_0";
        }
        else
        {
            string text2 = Enum.GetName(typeof(CultureCode), data.Settlement.Culture.GetCultureCode()).ToLower();
            if (data.IsInside)
            {
                return "conv_" + text2 + "_lordshall_0";
            }
            return string.Concat(new string[] { "conv_", text2, "_town_", text, "_0" });
        }
    }
}
```

### Custom Cultures

Culture is taken into account when town, castle or keep is visited.

When we have custom culture, this code:

```
string text2 = Enum.GetName(typeof(CultureCode), data.Settlement.Culture.GetCultureCode()).ToLower();
```

Returns 'invalid' instead of the culture name, because of hardcoded enum:

![](https://docs.bannerlordmodding.lt/pics/2404161115.jpg)

```
public enum CultureCode
{
    Invalid = -1,
    Empire,
    Sturgia,
    Aserai,
    Vlandia,
    Khuzait,
    Battania,
    Nord,
    Darshi,
    Vakken,
    AnyOtherCulture
}
```

so instead of atmosphere name: conv\_CUSTOM\_CULTURE\_lordshall\_0, we get conv\_invalid\_lordshall\_0 and then default scene's atmosphere is shown:

![](https://docs.bannerlordmodding.lt/pics/2404161007.jpg)

with hardcoded conv\_vlandia\_lordshall\_skybox

That would be kind-of-ok, BUT...

But this is ONLY if you have not visited any other dialog before. If you already talked with somebody in the fields at noon, then comming to the town with custom culture and going to the keep and talking with some lord, will show this lord as in the fields at noon.

I was unable to Harmony-patch IMapConversationDataProvider.GetAtmosphereNameFromData(MapConversationTableauData data) to solve this for now. Something something about interface changing name on compile, Harmony could not find the method... (Helping hand is welcome here)

So I was unable to change dialog backgrounds for town scenes and lordhalls(keeps) with custom cultures.

## Custom background

I change the backgrounds by overwriting native cubemaps:

![](https://docs.bannerlordmodding.lt/pics/2404161031.jpg)

### Custom cubemap

With the hope to get a better quality I took the [cubemap by NPC99.](https://forums.taleworlds.com/index.php?threads/world-mapping-for-bannerlord.432974/page-3#post-9800031), increased it 800%, so each rectangle is 2048x2048, and put my own image on it:

![](https://docs.bannerlordmodding.lt/pics/2404161035.jpg)

I guess this abomination requires explanation :)

### View in the Editor

Editor shows only this rectangle:

![](https://docs.bannerlordmodding.lt/pics/2404161043.jpg)

So I put nice picture on it to avoid looking at nonsense in the Editor:

![](https://docs.bannerlordmodding.lt/pics/2404161045.jpg)

### Camera projection on the cubemap

Each atmosphere has different angle how skybox is rotated, so camera looks at different parts of the skybox (your cubemap):

```
    <sun>
        <value name="skybox_rotation" value="226.000"/>
```

I use *improved* cubemap to find out where camera is looking:

![](https://docs.bannerlordmodding.lt/pics/2404161050.jpg)

I test in the widescreen, because usual 16:9 resolution shows only part of the screen and using it you will make a mess for wide-screen users.

Let's say I get such result:

![](https://docs.bannerlordmodding.lt/pics/2404161053.jpg)

That means I need to adjust where I put my new background to cover whole screen space:

![](https://docs.bannerlordmodding.lt/pics/2404161055b.jpg)

By placing my picture only on the necessary space I save a lot of time avoiding to creat full cubemap, that will not be used anyway.

### Quality

I spent a lot of time trying to get a better quality (and finally gave up).

Currently it seems resolution and color palette are reduced on a skybox/cubemap to look like crap.

So final quality is terrible (especially at night images):

![](https://docs.bannerlordmodding.lt/pics/2404161108.jpg)

What I have tried with zero effect:

* Increasing the resolution of the texture for a cubemap
* Moving camera backwards
* Changing various atmosphere settings (does not impact cubemap at all, only characters)

Same shitty quality with camera backwards:
![](https://docs.bannerlordmodding.lt/pics/2404161137.jpg)

### Texture/DDS format / Gamma

TpacTool shows that native skyboxe/cubemap's texture format is BC6H\_UF16.

ChatGPT: The BC6H\_UF16 format is a compressed texture format used primarily for high dynamic range (HDR) image data, and it's part of the DirectX and OpenGL graphics APIs. The format is specifically designed to handle HDR images effectively, enabling higher precision in storing and displaying image data with a wide range of luminance levels.

I was unable to get such format when importing DDS cubemaps.

I tried many formats and the ONLY one that imports AND shows up is this:

![](https://docs.bannerlordmodding.lt/pics/2404161123.jpg)

*Photoshop DDS plugins [here](https://drive.google.com/file/d/1sR03Bbf6uhSejA5uila3LkvQNZyo6QlP/view).*

Close Photoshop  
Copy files to `C:\Program Files\Adobe\Adobe Photoshop 2025\Plug-ins`  
Start Photoshop

Others do not import into the Editor at all, or imports but shows up as totally black.

The problem with this format is that it's Linear - that means messed up Gamma:

![](https://docs.bannerlordmodding.lt/pics/2404161127.jpg)

To compensate Linear gamma in Photoshop I did this:

![](https://docs.bannerlordmodding.lt/pics/2404161129.jpg)

ChatGPT: When dealing with issues related to the import of DDS textures in a linear color space and encountering gamma problems like overly bright images one of the fix is to preprocess the textures. Adjust the Gamma Correction value to something around 0.454 (which is the inverse of 2.2, commonly used for sRGB gamma).

### Texture import

When your cubemap is properly positioned, gamma adjusted, saved to proper format, you can import the texture under the same name you want to overwrite.

Set these to match the native settings:

![](https://docs.bannerlordmodding.lt/pics/2404161134.jpg)

## Final words

I hope there is a better way to do this because currently to change such a simple thing takes a lot of time and effort. Implementation is weird and cumbersome (or I do not understand it's purpose with all the cubemaps/atmospeheres/etc)

![](https://docs.bannerlordmodding.lt/pics/2404161140.jpg)Merry men with the background from the '90s.
