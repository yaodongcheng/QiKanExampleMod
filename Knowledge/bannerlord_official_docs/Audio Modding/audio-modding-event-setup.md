+++
title = "Event Setup"
weight = 20
+++

_Last updated: April 3, 2026_

---

## Parameters

Parameters in FMOD Studio are variables that control how an event behaves during playback. They can drive changes in volume, pitch, filters, instrument playback, or event logic — making sounds dynamic and responsive rather than static.

Each parameter has a name, a range of values, and a type. During gameplay, your code can set or adjust these values, or they can be driven automatically by game states, distance, velocity, or other inputs.

Parameters are the main bridge between the game and the sound design. They let audio react directly to gameplay, giving you fine-grained, real-time control over how sound evolves.

You can use existing parameters or create your own presets. To view existing parameters, go to **Window > Preset Browser** or press **Ctrl+8** to open the Preset Browser.

### Adding Parameters

To add a custom parameter, click **New Parameter** or press **Ctrl+P**. This opens the **Add Parameter** dialog.

| Field | Description |
|---|---|
| **Parameter Type** | Defines what kind of values the parameter accepts (see types below) |
| **Parameter Name** | A unique, self-explanatory identifier |
| **Range** | Minimum and maximum values |
| **Labels** | Names for each label, if using a Labeled type |
| **Initial Value** | The default value when the event starts |
| **Parameter Scope** | Local (separate per instance) or Global (shared across all instances) |

**Parameter Types:**

| Type | Description |
|---|---|
| User: Continuous | Float values for smooth, gradual changes |
| User: Discrete | Integer values for stepped states |
| User: Labeled | String values for named categories |
| Built-in: Distance | Listener–emitter distance |
| Built-in: Distance (Normalized) | Distance scaled 0–1 by min/max |
| Built-in: Direction | Horizontal angle relative to listener facing |
| Built-in: Elevation | Vertical angle relative to listener |
| Built-in: Event Cone Angle | Emitter's facing direction relative to the listener |
| Built-in: Event Orientation | Difference in facing between listener and emitter |
| Built-in: Speed (Relative) | Emitter speed relative to listener |
| Built-in: Speed (Absolute) | World-space emitter speed |

**Additional options in the dialog:**

| Option | Description |
|---|---|
| Exposed recursively via event instrument | Makes the parameter accessible through nested event instruments |
| Read-only | The parameter can be monitored but not modified |
| Hold value during playback | Local held parameters can only be set before an event starts or when it is stopped/paused. Global held parameters cannot be changed by the game at all. |

---

### Global Parameters

Global parameters are project-wide variables that exist once, affect any event that references them, and can be read or modified from anywhere.

| Name | Type | Range / Default | Description |
|---|---|---|---|
| ArenaIntensity | Discrete | 1–3, default: 1 | Number of people in the arena audience. **1** = Low, **2** = Medium, **3** = High |
| CampaignCameraHeight | Continuous | 0.0–1000.0, default: 0.0 | Camera distance above campaign terrain |
| Daytime | Continuous | 0.0–24.0, default: 12.0 | Time of day in 24-hour format |
| MissionCombatMode | Labeled | default: False | **True** = Scene is in combat, **False** = Scene is non-combat |
| MissionCulture | Labeled | default: None | Culture assigned to the scene: None, Aserai, Battania, Empire, Khuzait, Sturgia, Vlandia, Nord _(War Sails DLC)_, Outlaw, Reserved A, Reserved B |
| MissionProsperity | Labeled | default: None | Prosperity level: None _(e.g. Hideouts)_, Low, Medium, High |
| Rainfall | Continuous | 0.0–1.0, default: 0.0 | Rain presence and intensity. **0.0** = no rain |
| SceneType | Labeled | default: Exterior | **Exterior** = Outdoor scene, **Interior** = Indoor scene |
| Season | Labeled | default: Summer | Summer, Fall, Winter, Spring |
| SlowMotionMode | Labeled | default: False | **False** = Normal speed, **True** = Slow motion enabled |
| SoundPreset | Labeled | default: Speakers | Audio output profile: Speakers, Headphones, TV, Night Mode |
| WaveHeight | Continuous | −10.0–10.0, default: −10.0 | Vertical height of waves |
| WaveStrength | Continuous | 0.0–10.0, default: 0.0 | Intensity of wave motion |
| WindStrength | Continuous | 0.0–10.0, default: 0.0 | Intensity of wind |

---

### Local Parameters

Local parameters belong to a specific event instance. Each instance keeps its own value and changes don't affect any other event.

| Name | Type | Range / Default | Description |
|---|---|---|---|
| agent_state | Labeled | default: nothing | Awareness level of agents during stealth missions. **nothing** = unaware, **neutral** = briefly noticed, **cautious** = detected signs, **super_cautious** = actively searching, **alarmed** = located and alerted |
| Alert Distance | Built-in: Distance | 0.0–10.0 | Distance at which mission alerts trigger relative to the player |
| Armor Type | Continuous | 0.0–0.5, default: 0.0 | Armor worn by player/agent. **0.0–0.15** = Cloth, **0.15–0.25** = Leather, **0.25–0.35** = Chain mail, **0.35–0.5** = Plate mail |
| ArmySize | Continuous | 0.0–20.0, default: 0.0 | Size of an army on the campaign map |
| battle_size | Continuous | 0.0–4.0, default: 0.0 | Total troop count in a campaign map battle |
| Crowd Size | Continuous | 0.0–1000.0, default: 0.0 | Number of troops gathered at a location |
| Depth | Continuous | 0.0–1.0, default: 0.0 | Water depth |
| Direction | Built-in: Direction | −180.0–180.0 | Angle between listener facing and line to emitter. **0** = in front, **±90** = to the side, **±180** = behind |
| Distance Ambient 20 | Built-in: Distance | 0.0–50.0 | Distance of 3D ambient events within 50 m |
| Distance Campaign Node | Built-in: Distance | 0.0–30.0 | Distance of 3D campaign node events within 30 m |
| Distance Combat 500 | Built-in: Distance | 0.0–500.0 | Distance of combat sound events within 500 m |
| Distance Gallop 500 | Built-in: Distance | 0.0–500.0 | Distance of mount gallop events within 500 m |
| Distance Horse Walk 35 | Built-in: Distance | 0.0–35.0 | Distance of mount walking events within 35 m |
| Distance Human Walk 150 | Built-in: Distance | 0.0–150.0 | Distance of human walking events within 150 m |
| Distance Impact 75 | Built-in: Distance | 0.0–75.0 | Distance of impact events within 75 m |
| Distance Siege 500 | Built-in: Distance | 0.0–500.0 | Distance of siege events within 500 m |
| Distance Siege Med 250 | Built-in: Distance | 0.0–250.0 | Distance of siege events within 250 m |
| Distance Tavern Music | Built-in: Distance | 0.0–100.0 | Distance of tavern/street music events within 100 m |
| Distance Voice Long | Built-in: Distance | 0.0–500.0 | Distance of voice events within 500 m |
| Distance | Built-in: Distance | 0.0–80.0 | Generic distance parameter (default 80 m) |
| Event Cone Angle | Built-in: Event Cone Angle | 0.0–180.0 | How far the listener is from the emitter's forward direction. **0** = directly in front, **90** = to the side, **180** = directly behind |
| Event Orientation | Built-in: Event Orientation | −180.0–180.0 | Difference in facing between listener and emitter. **0** = same direction, **±90** = perpendicular, **±180** = opposite |
| Force | Continuous | 0.0–0.1, default: 0.57 | Instant physical force applied to an object |
| ForceContinuous | Continuous | 0.0–1.0, default: 0.0 | Continuous physical force applied to an object |
| impactModifier | Continuous | 0.0–0.6, default: 0.0 | Defensive block modifier. **0.0** = None, **0.1** = Active block, **0.2** = Chamber block, **0.3** = Crush through |
| isMissile | Continuous | 0.0–1.0, default: 0.0 | Whether the object is a projectile |
| isPlayer | Continuous | 0.0–1.0 | Whether the agent is the player. **0.0** = NPC, **1.0** = Player |
| Loop | Continuous | 0.0–1.0, default: 0.0 | Whether an event loops (music/video) |
| mpMusicSwitcher | Continuous | 0.0–1.0, default: 0.0 | Multiplayer state music. **0.0** = Main menu, **1.0** = Loading state |
| Occlusion | Continuous | 0.0–1.0, default: 0.0 | Degree of sound occlusion. **0.0** = None, **1.0** = Full |
| Size | Continuous | 0.0–1.0, default: 0.0 | Mass of an object (affects water collision sounds) |
| Speed | Built-in: Speed (Relative) | 0.0–10.0 | Relative speed of a game object |
| VibrationSend | Continuous | 0.0–1.0, default: 1.0 | Intensity of controller vibration |

---

### War Sails Parameters

These parameters only exist in the War Sails DLC module and are not available without it.

| Name | Type | Range / Default | Description |
|---|---|---|---|
| Campaign Ship Damage | Continuous | 0.0–1.0, default: 0.0 | Damage sustained by the player's ship on the campaign map |
| RowingPower | Continuous | 0.0–500.0, default: 0 | Collective force applied by oarsmen |
| RudderStress | Continuous | 0.0–1.0, default: 0.0 | Stress applied to the ship's rudder during maneuvering |
| SailRotation | Continuous | 0.0–1.0, default: 0.0 | Rotation speed of the ship's mast |
| ShipScrapingStatus | Labeled | default: NotScraping | **Scraping** = Ship is scraping, **NotScraping** = No scraping |
| ShipSpeed | Continuous | 0.0–1.0, default: 0.0 | Current speed of the ship |
| StormIntensity | Continuous | 0.0–2.0, default: 0.0 | Storm intensity on the campaign map. **0.0** = Storm, **1.0** = Thunderstorm, **2.0** = Hurricane |

---

## Effects Presets

Effect presets are pre-configured DSP setups that cover common use cases without manual configuration from scratch. Instead of building every effect chain yourself, you can apply a preset — such as a compressor, EQ, or reverb — that comes with sensible defaults designed for a specific purpose.

Presets let you get immediate results and fine-tune from there. They speed up workflow, ensure consistency across the project, and give everyone a shared baseline for commonly used effects. You can also create your own custom presets, or modify the existing ones.

To browse presets, go to **Window > Effect Presets** or press **Ctrl+8**. In the Preset Browser, select the **Effects** tab to see all pre-configured presets currently used in **Mount & Blade II: Bannerlord**.

When adding a new preset, you have these options:

| Option | Description |
|---|---|
| **New Effect** | Add a single DSP effect and configure it |
| **New Send** | Add and configure a bus send |
| **New Sidechain** | Add and configure a sidechain input |
| **New Effect Chain** | Build a chain of multiple DSP effects and configure them together |

Once a preset is created, give it a unique, self-explanatory name so it can be easily identified and reused across the project.

### Effects Preset List

| Name | Type | Driving Parameter(s) |
|---|---|---|
| Alert Nod Spatializer | Spatializer | — |
| Alert Spatializer | Spatializer | Alert Distance |
| Ambient Arena Cheer | Spatializer | — |
| Ambient Arena | Spatializer | Distance Ambient 20 |
| Ambient Battle Gain | Gain | Distance Combat 500 |
| Ambient Battle | Spatializer | — |
| Ambient Big | Spatializer | — |
| Ambient Doors | Spatializer | — |
| Ambient Medium | Spatializer | — |
| Ambient Small | Spatializer | — |
| Animals Big | Spatializer | — |
| Animals Small | Spatializer | — |
| Bodyfall Spatializer | Spatializer | — |
| Campaign Ambient ADSR | Gain | — |
| Campaign Footsteps | Spatializer | — |
| Campaign Node | Spatializer | — |
| Combat Low Gain | Gain | Distance Combat 500 |
| Combat Low Spatializer | Spatializer | — |
| Combat Send Clean | Send | Distance Combat 500 |
| Combat Send Distant | Spatializer | Distance Combat 500 |
| Combat Spatializer | Spatializer | Distance Combat 500 |
| Combat Swing Foley | Spatializer | — |
| Foley Generic Distant | Spatializer | — |
| Foley Generic | Spatializer | — |
| Footstep Horse Send Clean with Direction | Send | Direction |
| Footstep Horse Send Clean | Send | Distance Gallop 500 |
| Footstep Horse Send Distance | Send | Distance Gallop 500 |
| Footstep Human Send Clean | Send | Distance Human Walk 150 |
| Footstep Human Send Distant | Send | Distance Human Walk 150 |
| Horse Gallop Gain | Gain | — |
| Horse Gallop Spatializer | Spatializer | Distance Gallop 500 |
| Horse Walk Gain with Direction | Gain | Direction |
| Horse Walk Gain | Gain | Distance Horse Walk 35 |
| Horse Gallop Spatializer (Duplicate) | Spatializer | — |
| Human Run Spatializer | Spatializer | — |
| Human Sneak | Spatializer | — |
| Human Walk Armor | Spatializer | — |
| Human Walk | Spatializer | — |
| Impact Material Gain | Gain | Distance Impact 75 |
| Impact Material Spatializer | Spatializer | — |
| isPlayer Send Foley | Send | isPlayer |
| isPlayer Send | Send | isPlayer |
| Physics Small | Spatializer | — |
| Quad Ambient Rotator | Panner | Direction |
| Quad to Stereo Mix | Channel Mix | — |
| Rainfall Gain | Gain | Rainfall |
| Siege Big Gain Clean | Gain | Distance Siege 500 |
| Siege Big Gain Distant | Gain | Distance Siege 500 |
| Siege Big Send Clean | Send | Distance Siege 500 |
| Siege Big Send Distant | Send | Distance Siege 500 |
| Siege Medium Spatializer | Spatializer | — |
| Siege Move ADSR | Gain | — |
| Siege-Big | Spatializer | Distance Siege 500 |
| Siege-Big-Updated | Spatializer | Distance Siege 500 |
| Siege-Medium | Spatializer | — |
| Spatializer (Generic) | Spatializer | — |
| Tavern Music Distancing | Effect Chain | SceneType |
| Vibration Combat High | Send | isPlayer |
| Vibration Combat Low | Send | isPlayer |
| Vibration Combat Med | Send | isPlayer |
| Vibration Gallops | Send | isPlayer |
| Vibration Notifications | Send | — |
| Vibration Siege Big | Send | Distance Siege 500 |
| Voice Horse Send Clean | Send | Distance Voice Long |
| Voice Horse Send Distance | Send | Distance Voice Long |
| Voice Long Clean Send | Send | Distance Voice Long |
| Voice Long Distance Send | Send | Distance Voice Long |
| Voice Long Spatializer | Spatializer | — |
| Voice Short Clean Send | Send | Distance |
| Voice Short Distance Send | Send | Distance |
| Voice Short Spatializer | Spatializer | — |
| Voice Tiny Spatializer | Spatializer | — |

---

## Snapshots

Snapshots let you control the overall mix dynamically based on game states. If you've ever seen a digital mixing console where pressing a button moves all the faders to new positions at once — that's exactly how FMOD snapshots work.

### Adding Snapshots

To add a snapshot, open the Mixer window via **Window > Mixer** or press **Ctrl+2**. On the left side of the Mixer, switch to the **Snapshots** tab to see existing snapshots or create new ones.

Right-click on an empty space to create a new snapshot. You'll be asked to choose between two types:

| Type | Description |
|---|---|
| **Overriding Snapshot** | Takes full control of the affected mix properties, replacing existing values until released |
| **Blending Snapshot** | Layers its changes on top of the current mix, combining with other active snapshots for smoother transitions |

When multiple snapshots are active simultaneously, priority is determined by their position in the **Priority List**. Snapshots higher in the list take precedence over those below. For example, `GameMinimized` sits above `CampaignMap` in the priority list, meaning it overrides campaign map audio rules whenever the game is minimized.

When a snapshot is selected, the faders in the Mixer change color — this signals that any fader you adjust will only apply when that snapshot is active, not in the normal mix. The same applies to DSP effects and bus sends — for example, you can add a single algorithmic reverb to a return bus and adjust its properties per snapshot.

In the `GameMinimized` snapshot, all faders are set to −∞ dB, so that whenever the player minimizes the game window, every sound is muted. Once the snapshot goes out of scope, the faders automatically return to their original positions.

### Snapshot Events

Snapshots can be triggered by code, but they can also be triggered by events — which is especially useful for reverb zones in a scene. For more on reverb zones, see the [Scene Audio]({{< ref "audio-modding-scene-audio.md" >}}) page.

To add a snapshot event, first create a **timeline event** (snapshot instruments cannot be placed in action events). Give the event a clear, descriptive name.

Right-click the track in the timeline where you want to place the snapshot instrument. If a snapshot already exists you'll see it listed; otherwise you can create a new one. For reverb control, always use **blending snapshots**.

Once placed, the snapshot instrument appears on the track. You can drag the left and right edges of the instrument to add fade-in and fade-out handles, which smooth the transition into and out of the snapshot.

---

## User Properties

Events in FMOD Studio can carry **User Properties** — custom variables you define to pass extra information to the game. These act as metadata markers, letting the game code check for them and apply special handling when needed.

Every User Property has a name and a value. The name is always a string (letters and/or numbers). The value is interpreted automatically: if it's purely numeric, it's treated as a floating-point number; if it contains any non-numeric characters, it's treated as a string.

This system lets sound designers embed flexible, game-relevant data directly inside events, reducing the need for programmers to hard-code rules. User Properties are defined in the event's deck area in FMOD Studio — each row has a name field and a value field.

| Property | Description |
|---|---|
| **isQuad** | Used for ambient sounds. When set to `True`, the system will use the quadraphonic version of the ambient instead of the stereo fallback. A matching quad setup must exist before enabling this. See [Quadraphonic Ambients]({{< ref "audio-modding-scene-audio.md#quadraphonic-ambients" >}}). |
| **Pausable** | Used by the event-based pausing system. Default is `True`. Set to `False` for persistent events such as ambiences or music that should not be paused. |
| **priorityMultiplier** | Adjusts the relative priority of an event within the priority system — raising or lowering its likelihood of being culled. See [Priority System]({{< ref "audio-modding-systems.md#priority-system" >}}). |
| **maxLength** | Prevents build errors for timeline events without a timeline or instrument. Defines a duration so FMOD has the necessary length data without a timeline track. Not needed for Action events. |
| **battle_ambient_group_id** | Assigns an event to a specific group in the Battle Ambient Sound System (BASS), allowing events to be managed and optimized collectively. See [Battle Ambient Sound System]({{< ref "audio-modding-systems.md#battle-ambient-sound-system" >}}). |
| **customMaxDistance** | Overrides the default maximum audible distance of a 3D event. |
| **ignoreMaxDistance** | Ignores the maximum distance restriction of a 3D event. Accepts `0` (False) or `1` (True). |
