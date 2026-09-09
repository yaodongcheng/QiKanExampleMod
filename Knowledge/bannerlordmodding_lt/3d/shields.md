# Shields

<!-- 源: https://docs.bannerlordmodding.lt/3d/shields/ | 抓取日期: 2026-09-09 -->

* Based on: [Bannerlord 3D Asset Workflow](https://docs.google.com/document/d/1aHBsO3mzkT0JsbCt9aCOh6CWAFATXwKtSaVb__TYIoo/edit)
* [Shield Tutorial by Major Roy Gaming](https://www.youtube.com/watch?v=SxHnJDHaKn0)
* [In-Game Shield Editor](https://github.com/cz9085/Shield-Editor) by cz9085
* [In-Game Item Editor](https://www.nexusmods.com/mountandblade2bannerlord/mods/13041) by sakinolemir

## Meshes

Shields require 3 meshes/models for full implementation: the mesh for visuals, and two physics meshes.

![](https://docs.bannerlordmodding.lt/pics/2409281124.png)

Note: they are moved to the sides for a preview, when putting into the game they should be aligned in the same space.

* bo\_cap\_SHIELDNAME (bo\_cap\_lt\_heater\_shield in the example) is likely the ragdoll hitbox for the shield
* bo\_SHIELDNAME (bo\_lt\_heater\_shield in the example) likely is the hitbox for projectiles and melee
* SHIELDNAME (lt\_heater\_shield in the example) - the model that is going to be seen by the player

Triangle count is important for the physics calculations, more polygons means a lot more calculation by the engine, so [low is key](/3d/polycount/). 

![](https://docs.bannerlordmodding.lt/pics/2409290923.png)  
Native shield's polycount:  
![](https://docs.bannerlordmodding.lt/pics/2409290920.png)

### Export

Final [export to FBX](/3d/export_to_fbx/) should look like this:

![](https://docs.bannerlordmodding.lt/pics/2409291759.png)

## Positioning

Position within the 3D space of your modeling program is important.

All three meshes in the same position:

![](https://docs.bannerlordmodding.lt/pics/2409290900.png)

It’s best to use a shield close to what you want to implement. Make sure to copy the position, center line, and hand grip.

Here I used native heater\_shield\_f to help me position my shield and adjust it's hand grips:

![](https://docs.bannerlordmodding.lt/pics/2409290903.png)

### Hand grips

![](https://docs.bannerlordmodding.lt/pics/2409290943.png)

Properly adjusting hand-grips is very important to make shield look normal in the game. I had to move my shield's handgrips closer to the center and increase one handgrip to minimize clipping in-game:

![](https://docs.bannerlordmodding.lt/pics/2409290939.png)

You will be able to fine-tune the position and rotation in the [XML settings](/3d/shields/#xml).

Bulky gloves good for testing grips and clipping:

![](https://docs.bannerlordmodding.lt/pics/2410080938.png)

## Material

### Naming

![](https://docs.bannerlordmodding.lt/pics/2409282003.png)

These physics meshes require their material to be named in the following way:

* bo\_cap\_SHIELDNAME needs its material to be called “wood”
* bo\_SHIELD needs a material “wood\_shield”
* SHIELDNAME material can have the same name: SHIELDNAME

Renaming is done in your 3D program, when imported to the Editor, it’ll automatically assign the material you have named.

Make sure you create a matching material in the Editor *before* you import your FBX. If not - you will get many errors and will have to assign material to all the LODs by hand.

### Orientation

![](https://docs.bannerlordmodding.lt/pics/2410011431.png)

If your texture is rotated 90 degrees - that would be the problem if you will want to place a sigil on the shield, because there is no way (at least in my knowledge) to rotate the sigil on the shield 90 degress in the Vector Argument 1 settings.

I had to rotate textures and UVs in the Blender to fix this.

### Material in the Editor

Example settings for the shield's material in the Editor:

![](https://docs.bannerlordmodding.lt/pics/2409290951.png)

* use\_tableau\_blending is used to add clan's sigil
* alpha\_test and Transparency settings - to make diffuse layer transparent for clan's sigil to appear
* Vector Arguments1 - to properly position clan's sigil
* Vertex Layout - Skinning should be disabled

If you don't need clan's sigil - you can use the same settings as above, or:

* use\_tableau\_blending can be off
* alpha\_test off
* Transparency settings - off/does not matter
* Vector Arguments1 - all 0/does not matter
* Vertex Layout - Skinning should be disabled

### Clan's Sigil

![](https://docs.bannerlordmodding.lt/pics/2409281117.png)

Emblem/sigil for shields is applied by adding an alpha channel to the diffuse texture of the shield material and painting alpha where the sigil is desired.

Alpha channel can be added in the Photoshop:

![](https://docs.bannerlordmodding.lt/pics/2409291739.png)

Alpha channel examples:

Alpha channels for shields can be extracted with TpacTool from the native assets:  
![](https://docs.bannerlordmodding.lt/pics/2409291745.png)  
When extracting with TpacTool, save as DDS, and when importing into Photoshop, mark Load Transparency as Alpha channel:  
![](https://docs.bannerlordmodding.lt/pics/2409291753a.jpg)

Download some of the native alphas from [here](https://drive.google.com/file/d/1kDHneCeIhIjpz_PvP_IPwc8Iq9UZ1QqV/view?usp=drive_link)

Save modified DiffuseMap texture as PSD to properly save Alpha channel and import PSD into the Editor:

![](https://docs.bannerlordmodding.lt/pics/2409291742.png)

When creating a material, make sure you have use\_tableau\_blending ON, alpha\_test ON and adjust transparency with Alpha Blend Mode: Factor and Alpha Test > 0:

![](https://docs.bannerlordmodding.lt/pics/2409291750.jpg)

The clans's sigil/emblem is added to the shield by adjusting Vector Argument 1 under the Vector Arguments tab on the material. x, y determines the position. z, w determines the scale.

From Preview Mesh select Plane for better visualization.

In Diffuse2Map add sigil\_test\_bounds\_for\_shields for better positioning (Inner circle shows the place where the sigil will be present). This is quick and easy way to properly position the sigil.

Take it from here:

![](https://docs.bannerlordmodding.lt/pics/sigil_test_bounds_for_shields.png)

![](https://docs.bannerlordmodding.lt/pics/2409291804.png)

If you need to turn the sigil upside down, use minus sign for the y field in the Vector Argument 1. Mirror reflection - use minus x. Couldn't find the options to rotate 90 degrees though...

When the sigil is placed properly - REMOVE the Diffuse2Map and Save your material. Otherwise you will see this yellow picture instead of your real sigil in-game.

### Problem: Red artifacts

![](https://docs.bannerlordmodding.lt/pics/2409291038.png)

Cause: BLACK is too black here :) (#000000 I guess)

Solution - make it less black:

![](https://docs.bannerlordmodding.lt/pics/2409291040.png)

## XML

```
<Item
    id="flat_heater_shield"
    name="{=ap2yulQ2}Flat Heater Shield"
    body_name="bo_cap_heater_shield_f"
    shield_body_name="bo_heater_shield_f"
    recalculate_body="false"
    mesh="heater_shield_f"
    culture="Culture.vlandia"
    using_tableau="true"
    weight="4.7"
    appearance="1"
    Type="Shield"
    item_holsters="shield:shield_2:shield_3:shield_4"
    has_lower_holster_priority="true"
    holster_position_shift="-0.10,0.1,0.025">
    <ItemComponent>
        <Weapon
            weapon_class="LargeShield"
            body_armor="10"
            thrust_speed="82"
            thrust_damage_type="Blunt"
            speed_rating="93"
            physics_material="wood_shield"
            item_usage="shield"
            position="0.0, 0.00, 0.00"
            rotation="0.0,10.0,40.00"
            weapon_length="90"
            center_of_mass="0.0,0.1,0.05"
            hit_points="310"
            modifier_group="shield">
            <WeaponFlags
                CanBlockRanged="true"
                HasHitPoints="true" />
        </Weapon>
    </ItemComponent>
    <Flags
        WoodenParry="true"
        HeldInOffHand="true"
        ForceAttachOffHandSecondaryItemBone="true" />
</Item>
```

Make sure to reference your physics meshes in `mesh`, `body_name` and `shield_body_name`.

`recalculate_body` - every Taleworlds shield has false, perhaps there is a hidden function here.

`weapon_length` - Each shield has a weapon\_length, 'kite\_shield\_e' here for an example, has a length of 118. When measured in Blender, this number comes to roughly 125, a clear discrepancy. I cannot extract the physics mesh to measure, so perhaps this is the length of that. Or there is another purpose behind the length, and an easy way to calculate it.

`weapon_class` - “LargeShield” The only option for `weapon_class`. Perhaps this is an unfinished feature, as the prefix of ‘Large’ suggests other shield types within the weapon\_class. Or perhaps this is only a way to differentiate shields from other equipment types.

`physics_material` - “wood\_shield” “metal\_shield” Changes the sound effects used by the shield.

`item_usage` - “shield” “hand\_shield” Unsure about the differences between the two, perhaps animation.

`using_tableau` ="true" - without it the sigil will not appear in-game

This should NOT be present: <Flags UseTeamColor="true"/>

### item\_usage

Possible values: “hand\_shield” / “shield”

Determines how the shield is held in the hand and affects the troop's pose.

`hand_shield` - The shield is held only with the palm of the left hand (e.g., round shields, large Battanian shields).

![](https://docs.bannerlordmodding.lt/pics/2409301742.png)

`shield` - The shield is held with the arm and the palm of the left hand (e.g., heater shields, kite shields).

![](https://docs.bannerlordmodding.lt/pics/2409301742b.png)

An example with item\_usage='shield' when correct value should be 'hand\_shield':

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/shield_wrong_hold.webm)

### position

Position when the shield is used.

![](https://docs.bannerlordmodding.lt/pics/2410080936.png)

### rotation

Rotation when the shield is used.

![](https://docs.bannerlordmodding.lt/pics/2410080931.png)

### holster\_position\_shift

Sets the shield's holstered position on the troop's back.

![](https://docs.bannerlordmodding.lt/pics/2409291045.png)

Adjustments:

![](https://docs.bannerlordmodding.lt/pics/2409291045a.png)

![](https://docs.bannerlordmodding.lt/pics/2409291045b.png)

![](https://docs.bannerlordmodding.lt/pics/2409291045c.png)

## Workflow

1. Export with TpacTool the most similar shield as EXAMPLE
2. Import into the Blender
3. Import own model into the Blender
4. Position own model in the Blender the same way as EXAMPLE model
5. Modify the handles/hand grips to match the EXAMPLE model as close as possible
6. Rename all parts properly
7. bo\_cap\_SHIELD material rename to "wood"
8. bo\_SHIELD material rename to "wood\_shield"
9. rename SHIELD material
10. generate LODs for SHIELD
11. Export FBX in BL format
12. Import textures in the Editor
13. Create material
14. Disable skinning
15. Import FBX
16. Adjust Sigil if necessary
17. Add XML
18. Position shield properly
19. Test in-game/fix/repeat
