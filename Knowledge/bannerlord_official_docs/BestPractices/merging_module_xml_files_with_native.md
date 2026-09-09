+++
title = "Merging Module XML Files with Native"

[menu.main]
identifier = "merging_module_xml_files_with_native"
parent = "bestpractices"
+++

With Bannerlord v1.3, we have implemented an XML merging algorithm that assists in merging XML files defined in other modules' module data without using XSLT. We can add new elements to the XMLs or change some attributes during merging, using their unique attributes and other information gathered from XSDs. Follow these steps to enable the merging algorithm:

1. Create an XML file that needs to be merged with the base game
2. Add new elements with the content of the mod; they will be added to the appropriate places in the final merged XML file that is going to be used by the game
3. Change elements that are already present in the base game

In the engine, merging of native and other modules' data is done in two ways.

## Merging for Native

These are the XML files that start with "soln". In ModuleData, they are defined in the "project.mbproj" file.

In the following code, we can see that this module registers:

* action_sets
* action_types
* movement_sets
* full_movement_sets
* combat_system
* skins

```xml
<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="solution">
  <outputDirectory>../MBModule/MBModule/</outputDirectory>
  <XMLDirectory>../WOTS/Modules/Naval DLC/</XMLDirectory>
  <ModuleAssemblyDirectory>../WOTS/bin/</ModuleAssemblyDirectory>
  <file id="soln_action_sets" name="ModuleData/action_sets.xml" type="action_set" />
  <file id="soln_action_types" name="ModuleData/action_types.xml" type="action_type" />
  <file id="soln_movement_sets" name="ModuleData/movement_sets.xml" type="movement_set" />
  <file id="soln_full_movement_sets" name="ModuleData/full_movement_sets.xml" type="full_movement_set" />
  <file id="soln_combat_system" name="ModuleData/native_parameters.xml" type="native_parameters" />
  <file id="soln_skins" name="ModuleData/skins.xml" type="skin" />
</base>
```

These files are automatically found by the system and merged with native.

## Managed XML Files

The other kind of merging is done with managed XML files such as items.xml, heroes.xml, and settlements.xml. They are defined in the SubModule.xml file located at the module's root. Here's an example of a SubModule file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Module>
  <Name value="NavalDLC" />
  <Id value="NavalDLC" />
  <Version value="v1.3.0" />
  <DefaultModule value="false" />
  <ModuleCategory value="Singleplayer" />
  <ModuleType value="OfficialOptional" />
  <DependedModules>
    ...
  </DependedModules>
  <SubModules>
    ...
  </SubModules>
  <Xmls>
    <XmlNode>
      <XmlName id="Items" path="items" />
      <IncludedGameTypes>
        <GameType value="Campaign" />
        <GameType value="CampaignStoryMode" />
      </IncludedGameTypes>
    </XmlNode>
  </Xmls>
</Module>
```

In the above example, we have registered an XMLNode to the game with the id="Items". So when we need merging, items.xml from the base game will merge with the items.xml from the mod. The only thing that needs to be done is to implement the items.xml file as usual, and they will be merged.

Let's have a look at the Torch item located in the native "items.xml":

```xml
<Item
    id="torch"
    name="{=ErOLa263}Torch"
    is_merchandise="false"
    body_name="bo_mace_a"
    recalculate_body="true"
    mesh="torch_g"
    prefab="torch_a_wm_only_flame"
    culture="Culture.empire"
    value="4"
    weight="0.2"
    Type="OneHandedWeapon"
    item_holsters="abdomen_left">
    <ItemComponent>
        <Weapon
            weapon_class="OneHandedAxe"
            weapon_balance="100"
            thrust_speed="78"
            speed_rating="97"
            missile_speed="0"
            weapon_length="65"
            swing_damage="6"
            swing_damage_type="Blunt"
            item_usage="banner"
            physics_material="metal_weapon">
            <WeaponFlags MeleeWeapon="true" />
        </Weapon>
    </ItemComponent>
    <Flags
        DropOnWeaponChange="true"
        ForceAttachOffHandPrimaryItemBone="true"
        HeldInOffHand="true"
        HasToBeHeldUp="true" />
</Item>
```

We can change some properties of this item by rewriting it in our own module. When the merge algorithm runs, it searches through the XML file and looks at the "unique" attributes of that element (which is "id" in this case). If a unique attribute is found, that element is changed, and if it is not found, the new item is added to the XML file. Let's say we wanted to change the above Torch item, which has the following unique attribute id: "torch". We want to make it available in shops, change its swing_damage to 9, and set the DropOnWeaponChange flag to false. We would insert the following code into our module's items.xml:

```xml
<Item
    id="torch"
    is_merchandise="true"
    body_name="bo_mace_a">
    <ItemComponent>
        <Weapon swing_damage="9">
        </Weapon>
    </ItemComponent>
    <Flags DropOnWeaponChange="false" />
</Item>
```

The algorithm then searches through the items and finds an item with a unique attribute that is already present in native files. It then starts the merging process. This is the resulting item:

```xml
<Item
    id="torch"
    name="{=ErOLa263}Torch"
    is_merchandise="true"
    body_name="bo_mace_a"
    recalculate_body="true"
    mesh="torch_g"
    prefab="torch_a_wm_only_flame"
    culture="Culture.empire"
    value="4"
    weight="0.2"
    Type="OneHandedWeapon"
    item_holsters="abdomen_left">
    <ItemComponent>
        <Weapon
            weapon_class="OneHandedAxe"
            weapon_balance="100"
            thrust_speed="78"
            speed_rating="97"
            missile_speed="0"
            weapon_length="65"
            swing_damage="9"
            swing_damage_type="Blunt"
            item_usage="banner"
            physics_material="metal_weapon">
            <WeaponFlags MeleeWeapon="true" />
        </Weapon>
    </ItemComponent>
    <Flags
        DropOnWeaponChange="false"
        ForceAttachOffHandPrimaryItemBone="true"
        HeldInOffHand="true"
        HasToBeHeldUp="true" />
</Item>
```

For each XML file, there is an XSD schema file that gives information about the structure of the XML file. The XSD files are located in the XmlSchemas folder. These files also tell us which attributes of an element are unique attributes. And there are some elements where the algorithm always prefers merging. In the XML files above, if we look at the corresponding XSD schemas, we will see that for the element "Item", the unique attribute is "id", for the element "ItemComponent", we should always prefer merging because it is restricted that there is only one "ItemComponent" element for each "Item". All these information is read from the xsd files by the algorithm. For example, let's look at the xsd schema of "ShipHulls":

```xml
<?xml version='1.0' encoding='UTF-8'?>
<xs:schema xmlns=""
  xmlns:xs="http://www.w3.org/2001/XMLSchema"
  xmlns:msdata="urn:schemas-microsoft-com:xml-msdata" id="ShipHulls">
  <xs:simpleType name="positiveIntType">
    <xs:restriction base="xs:int">
      <xs:minExclusive value="0"/>
    </xs:restriction>
  </xs:simpleType>
  <xs:element name="ShipHulls" msdata:IsDataSet="true">
    <xs:complexType>
      <xs:sequence>
        <xs:element name="ShipHull" maxOccurs="unbounded" minOccurs="0">
          <xs:complexType>
            <xs:sequence>
              <xs:element name="AvailableSlots" maxOccurs="1">
                <xs:annotation>
                  <xs:appinfo>
                    <appSpecificNote>AlwaysPreferMerge</appSpecificNote>
                  </xs:appinfo>
                </xs:annotation>
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="ShipSlot" maxOccurs="unbounded" minOccurs="0">
                      <xs:complexType>
                        <xs:attribute type="xs:string" name="id" use="required"/>
                        <xs:attribute type="xs:string" name="tag_id" use="required"/>
                      </xs:complexType>
                    </xs:element>
                  </xs:sequence>
                </xs:complexType>
                <!-- id attribute is unique for ShipSlot -->
                <xs:unique name="ShipSlot_unique_attribute">
                  <xs:selector xpath="ShipSlot"/>
                  <xs:field xpath="@tag_id"/>
                </xs:unique>
              </xs:element>
            </xs:sequence>
            <xs:attribute type="xs:string" name="id" use="required"/>
            <xs:attribute type="xs:string" name="mission_ship" use="required"/>
            <xs:attribute type="xs:string" name="name" use="required"/>
            <xs:attribute type="xs:string" name="ship_type" use="required"/>
            <xs:attribute type="xs:float" name="base_speed" use="required"/>
            <xs:attribute type="xs:int" name="value" use="required"/>
            <xs:attribute type="xs:string" name="default_group" use="required"/>
            <xs:attribute type="xs:boolean" name="can_navigate_shallow_water" use="required"/>
            <xs:attribute type="xs:float" name="production_build_weight" use="required"/>
            <xs:attribute type="xs:int" name="sea_worthiness" use="required"/>
            <xs:attribute type="xs:int" name="inventory_capacity" use="required"/>
            <xs:attribute type="xs:float" name="map_visual_scale" use="required"/>
            <!-- Ship's maximum hitpoints. -->
            <xs:attribute name="max_hitpoints" type="positiveIntType" use="required"/>
            <!-- Ship's total crew capacity. Including both the crew on ship's main deck and also the reserves within ship's lower decks -->
            <xs:attribute name="total_crew_capacity" type="positiveIntType" use="required"/>
            <!-- Ship's main deck crew capacity. Must match the count of crew spawn entities in ship's prefab -->
            <xs:attribute name="main_deck_crew_capacity" type="positiveIntType" use="required"/>
            <!-- Skeletal crew capacity. Minimum crew count to operate the ship -->
            <xs:attribute name="skeletal_crew_capacity" type="positiveIntType" use="required"/>
          </xs:complexType>
        </xs:element>
      </xs:sequence>
    </xs:complexType>
    <!-- id attribute is unique for ShipHull -->
    <xs:unique name="ShipHull_unique_attribute">
      <xs:selector xpath="ShipHull"/>
      <xs:field xpath="@id"/>
    </xs:unique>
  </xs:element>
</xs:schema>
```

In the schema above, we can see that "xs:unique" elements tells us which attributes of the element specified with "xpath" are unique. For example lets look at:

```xml
<!-- id attribute is unique for ShipHull -->
<xs:unique name="ShipHull_unique_attribute">
  <xs:selector xpath="ShipHull"/>
  <xs:field xpath="@id"/>
</xs:unique>
```

This tells that, "id" attribute of an element "ShipHull" is unique. So, the merging algorithm checks this "id" field while merging. There are also some annotations that can be seen in the xsd files such as:

```xml
<xs:element name="AvailableSlots" maxOccurs="1">
  <xs:annotation>
    <xs:appinfo>
      <appSpecificNote>AlwaysPreferMerge</appSpecificNote>
    </xs:appinfo>
  </xs:annotation>
```

This tells us that an "AvailableSlots" element occurs only once in its parent and when the algorithm encounters an "AvailableSlots" item while modifying the parent item with merging, it knows that the "AvailableSlots" present in the other element that we are trying to merge directly merges with the existing "AvailableSlots" without resulting in two "AvailableSlots" elements. Elements that are annotated like this do not have unique id's as well because we don't need one.
