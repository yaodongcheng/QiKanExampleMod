+++
title = "Audio Systems & Optimization"
weight = 40
+++

_Last updated: April 3, 2026_

---

## Voice System {#voice-system}

Non-dialogue vocalizations for humans, mounts, and animals (grunts, pain sounds, commands, etc.) are not hardcoded into game logic. Instead, they're managed through an external configuration file called `voice_definitions.xml`. This makes it straightforward to add or adjust voices without touching code. All declarations and definitions are written in XML and link directly to FMOD event paths.

**File path:**
```
..\Modules\Native\ModuleData\voice_definitions.xml
```

### Voice Type Declarations

Before a voice can be used, it must be declared under the `<voice_type_declarations>` section. This registers the voice type so it can be referenced later.

```xml
<voice_type_declarations>
  <voice_type name="Grunt" />
</voice_type_declarations>
```

Here, the voice type `Grunt` is declared and can now be used in one or more definitions.

### Voice Definitions

Once a type is declared, a **voice definition** links it to an FMOD event path and defines metadata such as pitch variation and usage restrictions.

```xml
<voice_definition
  name="male_01"
  sound_and_collision_info_class="human"
  only_for_npcs="true"
  min_pitch_multiplier="0.9"
  max_pitch_multiplier="1.1">

  <voice
    type="Grunt"
    path="event:/voice/combat/male/01/grunt"
    face_anim="grunt" />

</voice_definition>
```

### Definition Attributes

| Attribute | Description |
|---|---|
| `name` | Unique identifier for the voice definition |
| `sound_and_collision_info_class` | Class of entity this voice belongs to (e.g. `human`, `horse`) |
| `only_for_npcs` | If `true`, this voice cannot be assigned to the player |
| `min_pitch_multiplier` / `max_pitch_multiplier` | Defines the pitch variation range |

### Voice Node Details

Inside a `<voice_definition>`, one or more `<voice>` entries can be added:

| Attribute | Description |
|---|---|
| `type` | Must match a declared `<voice_type>` |
| `path` | FMOD event path triggered for this voice |
| `face_anim` | Facial animation tag associated with this voice (e.g. `grunt`) |

### Workflow Summary

1. Add a new `<voice_type>` under `<voice_type_declarations>`
2. Create a `<voice_definition>` with a name, class, restrictions, and pitch range
3. Add one or more `<voice>` elements linking the definition to FMOD event paths
4. Assign `face_anim` for animation syncing

---

## Physics Materials

The physics system manages all material-to-material interactions in the game and determines which sound events are triggered when collisions occur. Instead of handling each case independently, all physical interactions — wood striking metal, stone colliding with a projectile, metal on metal — are processed in a single unified system. This keeps logic consistent, reduces duplication, and makes it easier to maintain and expand audio coverage as the game grows.

### Material Interaction Handling

Each material type is defined in the physics system along with its interaction rules when colliding with other materials. For example:

| Interaction | Result |
|---|---|
| Wood vs. Metal | A metallic clash with resonant overtones |
| Stone vs. Missile | A sharp, brittle impact sound |
| Wood vs. Stone | A dull, muted collision |

When two materials collide, the system evaluates their properties and selects the correct FMOD event to play. This removes the need to hard-code every possible pairwise interaction and ensures audio scales automatically as new materials are introduced.

### Special Case: Water Interactions

Water requires unique treatment compared to solid materials. Unlike other pairings, "object + water" doesn't need a dedicated event for every possible object type. Regardless of what enters the water, the player primarily perceives it as a water sound — not as "body hitting water" or "stone hitting water."

To simplify implementation, the system uses a **single generic water event** parameterized by the physical properties of the object entering the water:

| Parameter | Description |
|---|---|
| **Volume** | Determines how large the displaced splash should sound |
| **Mass** | Scales the weight and depth of the impact |
| **Force** (velocity/acceleration) | Controls the intensity and sharpness of the splash |

By combining these variables, the system can produce a wide range of splash sounds from the same event — from a small pebble skipping across the surface to a heavy boulder crashing into deep water.

**Benefits of this approach:**

- **Unified logic** — All material interactions are handled consistently in one system
- **Scalability** — Adding new materials only requires defining their properties, not rewriting interaction logic
- **Efficiency** — Parameter-driven variations replace dozens of unique event definitions
- **Water optimization** — One generic event covers all possible water interactions, driven dynamically by physics parameters
- **Realism** — Splash sounds scale naturally with object size, weight, and velocity

---

## Priority System {#priority-system}

This system runs **before** FMOD's built-in priority system, acting as a pre-filter to reduce the CPU cost of evaluating large numbers of events. It applies its own rules to decide which sounds are eligible, and only those are passed on to FMOD for playback. FMOD's internal priority system still applies once events are submitted, so you can continue using it to refine prioritization among active events.

### Core Mechanism

The system maintains an array whose maximum size corresponds to the maximum number of events it's allowed to manage at once. Each candidate event is evaluated against a priority scheme based on:

| Parameter | Description |
|---|---|
| **Length** | Duration of the event — used to determine whether short transient sounds or long events should be favored |
| **Distance** | Spatial distance between emitter and listener. If this exceeds the event's defined max distance in FMOD, the sound is culled immediately and never passed to FMOD |
| **PriorityMultiplier** | A user-defined multiplier that adjusts the relative importance of an event compared to others |

These parameters are stored in the **SEDF (Sound Event Data File)** for every event, ensuring consistent evaluation.

### Distance Filtering

Events that fall outside their maximum audible distance are killed before FMOD ever processes them. This prevents FMOD from spending resources evaluating voices that can't be heard.

### Exceptions and Looping Events

Looping sounds need special handling:

- **Ambient sounds** are excluded from this system entirely and marked as persistent within FMOD — they are never culled by pre-priority logic.
- **Looped sounds** present a problem: if they're killed and restarted, they can't resume from their previous play position, causing synchronization issues.

To handle this, looped sounds can be **virtualized** instead of killed. Virtualization lets the event stop consuming CPU while still tracking its playback position, so when it returns to audible range it continues seamlessly from where it left off.

### User Property Overrides

User Properties provide additional flexibility (see [User Properties]({{< ref "audio-modding-event-setup.md#user-properties" >}})):

- **Ignore flags** — Distance or length checks can be bypassed for specific events if tagged accordingly
- **Custom multipliers** — A multiplier can be defined per event through User Properties, modifying its relative priority when compared with others

**Benefits:**

- **CPU efficiency** — Reduces FMOD's workload by culling inaudible or unnecessary sounds before they reach the engine
- **Flexible prioritization** — Length, distance, and multipliers allow fine-tuned control over event importance
- **Synchronization safety** — Virtualization ensures looped events remain consistent when reactivated
- **Designer control** — User Properties enable exceptions and weighting without code changes

---

## Battle Ambient Sound System {#battle-ambient-sound-system}

The Battle Ambient Sound System — abbreviated as **BASS** — is designed to optimize the playback of high-density sound effects, such as multiple sword clashes occurring in the same area. Instead of letting each individual sound play independently (which can quickly overwhelm both the mix and the CPU), BASS aggregates these events and replaces them with a single emitter that represents the group.

### How It Works

Within a defined region, BASS monitors the number of sound events triggered over a given period. When the count exceeds a specified threshold, the system suppresses the individual sounds in that region and spawns a single **group emitter event** in their place.

This emitter event includes a parameter reflecting the density of sounds in the area. As the parameter increases, the mix shifts from sparse individual clashes to dense, chaotic battle noise. Players still perceive an intense soundscape, but without the overhead of dozens of overlapping one-shots.

### Interaction with the Priority System

BASS operates **before** our proprietary engine's priority system. Normally, the priority system decides which sounds to cull when many voices compete for playback. In this workflow, BASS preemptively reduces polyphony at the source level — only the grouped emitter remains active, meaning the priority system has fewer voices to manage. This prevents unnecessary CPU/DSP usage and ensures consistent sound quality during crowded combat.

### BASS Group IDs

Use the `battle_ambient_group_id` user property (see [User Properties]({{< ref "audio-modding-event-setup.md#user-properties" >}})) to assign an event to a BASS group.

| ID | Group |
|---|---|
| 0 | Infantry move |
| 1 | Cavalry move |
| 2 | Weapons fight |
| 3 | Shield fight |
| 4 | Missile volley |
| 5 | Human fight |
| 6 | Human victory |
| 7 | Horse vocal |

**Examples:**
- Adding a new weapon event → set `battle_ambient_group_id` to **2**
- Adding a footstep event for a new mount type → set `battle_ambient_group_id` to **1**

By assigning the correct group ID, BASS fires one unified event per group instead of hundreds of individual ones, significantly reducing active event count and saving system resources.

**Benefits:**

- **Performance optimization** — Prevents CPU and memory spikes from excessive overlapping sounds
- **Mix clarity** — Replaces cluttered layers of transients with a single controlled emitter
- **Scalability** — Parameter-driven event design adapts automatically to combat density

---

## Performance and Optimization

In a CPU- and memory-intensive title like **Mount & Blade II: Bannerlord**, FMOD must be treated as part of the overall performance budget. Poorly managed audio banks, codecs, or event design can consume resources the game engine needs for everything else. Optimization is not a one-off task — it's an ongoing discipline throughout the entire modding process.

### Codec and Asset Strategy

Every decoded sample costs CPU, every oversized file costs memory and disk I/O, and every resample costs even more CPU. Codec strategy must be set deliberately, not left to chance.

FMOD lets you set a default codec for the entire audio build in the bank build settings (ensuring consistency across the project) and override codecs per asset when specific files demand different treatment.

FMOD Studio's practical PC formats are:

| Codec | Characteristics |
|---|---|
| **Vorbis** | Best compression, but higher decode cost. Best overall choice when CPU is available |
| **ADPCM** | Larger than Vorbis but almost free to decode. Good fallback when Vorbis is too CPU-heavy |
| **PCM** | No compression, no decode cost. Can quickly bloat memory — avoid unless strictly necessary |

#### Load Types

Each asset has a **Load Type** setting that determines how it lives in memory:

| Load Type | Description |
|---|---|
| **Compressed** | Loaded in compressed form and decompressed at playback. Less memory, slightly more CPU at play time |
| **Decompressed** | Decompressed into PCM when banks load, not at playback. Not recommended unless you have a specific need (e.g. very low-spec devices) |
| **Streaming** | Loaded in chunks from disk as needed. Good for long music and ambiences that aren't time-sensitive |

### Streaming and Bank Management

On PC, long assets like music or ambiences can balloon RAM usage if kept fully in memory. Streaming solves this by pulling audio in chunks from disk, but every stream adds CPU overhead and disk I/O pressure. Stream too many files at once and you risk stutters and thread contention, especially on HDDs or low-end systems. **Stream only what needs it.**

Banks are containers for events, assets, and metadata — they determine what FMOD loads into RAM at runtime. Poorly structured banks leave unnecessary assets in memory, extend load times, and create streaming bottlenecks. Each event should live in a single bank; the same event should never appear in multiple banks unless there's an unavoidable design requirement.

### Event, DSP, and Channel Management

Every event generates one or more voices, each consuming CPU and memory. Stereo, quad, or 5.1 events multiply the number of channels being mixed. Combine this with DSP on each path and costs grow fast. If you don't actively manage instances, channels, and DSP chains, your mix can choke the CPU long before your assets fill memory.

#### Controlling Event Instances and Channels

Although we already handle this with our custom priority system, you can set max instance limits on heavily triggered events if needed. For each event, define a sensible polyphony cap and choose a **stealing rule**:

| Rule | Behavior |
|---|---|
| **Oldest** | Cuts off the instance that has been playing the longest |
| **Newest** | Blocks the most recently triggered instance from starting |
| **Quietest** | Stops the currently lowest-volume instance |
| **Virtualize** | Suspends an instance into a no-CPU "virtual" state while keeping its timeline active. Useful for VO and music |
| **None** | Prevents new instances from starting if the max is already reached |
| **Furthest** | Stops the instance furthest from the listener |

Keep channel counts realistic:

- Use **mono** for any sound with no spatial width
- Use **stereo** only when width is essential (music, ambiences, UI)
- Save **quad or 5.1+** for true surround beds that justify the cost

Every extra channel means extra mixing, panning, and potential DSP. Don't waste channels on one-shots that will collapse to mono in most contexts anyway.

#### Managing DSP Load

DSP processing is the heaviest part of runtime audio performance. Effects like convolution reverb, FFT filters, and multiband dynamics consume significant CPU — often more than their audible benefit justifies. Apply DSP only when it provides clear value, such as shaping the overall mix in a way that can't be pre-baked. For common one-shot events, keep them clean or embed processing directly into the audio file.

Channel count multiplies DSP cost: a stereo convolution reverb is twice the work of mono, and a quad instance multiplies it further. Use mono effects wherever possible.

### Bus and Routing

Bus hierarchies should stay functional and minimal. Group major categories under dedicated sub-buses only where it serves a clear purpose. Use sends and sidechains intentionally — every extra routing hop adds to channel mixing and DSP load. Consolidate DSP chains at the bus level instead of stacking them across many individual events. Snapshots can then modify the properties of a single DSP effect across multiple states, significantly cutting down on total DSP instances required.

### Using Baked Effects

If a sound always needs coloration (EQ, distortion, static reverb), bake it into the source file. Reserve runtime DSP for genuinely interactive processing: parameter-driven filters, ducking, pitch modulation, or environmental reverb zones that change dynamically. This prevents the slow creep of redundant DSP costs.

### Importing Assets

Keep assets lean before they reach FMOD:

- **Trim silence** from samples
- **Normalize loudness** to a consistent LUFS baseline
- **Match the project sample rate** (48 kHz) to avoid hidden resampling and wasted CPU cycles

While FMOD encodes assets into its own formats at build time, the **source format** you import still affects workflow. Using `.wav` for everything bloats the project since .wav files are uncompressed. Switching to **`.flac`** for source assets can drastically reduce disk footprint while preserving lossless quality — this doesn't affect the final encoded format (PCM, Vorbis, ADPCM, etc.), but makes asset management lighter and faster.

### Testing for Edge Cases

Always test edge conditions: thousands of agents in combat, long campaign sessions, or rapid scene transitions. These scenarios reveal memory leaks, mismanaged banks, or runaway event counts before they become real-world problems reported by players.
