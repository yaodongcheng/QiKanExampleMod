# Modding sound

<!-- 源: https://docs.bannerlordmodding.lt/modding/sound/ | 抓取日期: 2026-09-09 -->

## Tutorials

* [Oficial guide - Audio Systems & Optimization](https://moddocs.bannerlord.com/audio-modding/audio-modding-systems/)
* [Oficial guide - Adding Custom Sounds](https://moddocs.bannerlord.com/playing-sounds/adding-custom-sounds/)
* [Forum post: Modding Sound & Music](https://forums.taleworlds.com/index.php?threads/modding-sound-music.433302/)
* [Audio Modding Guide for Bannerlord](https://www.nexusmods.com/mountandblade2bannerlord/mods/1223)
* [Lesser Scholar Youtube Tutorial Ep 12: Sound Effects](Link: https://youtu.be/SAONxyzDuic)
* [Bannerlord Music Modding Tutorial : Pt. 1](https://www.youtube.com/watch?v=nLGbbvoUYCI) by SixxTailsHD

## Resources

* [Free sounds](https://freesound.org/)

## Various

* [Discussion about the sound in the scenes](https://discord.com/channels/411286129317249035/697071354603765791/1101488088892579940)

## Folder structure

In ...\Modules\\*YOUR\_MOD\*\

```
\ModuleData\module_sounds.xml - custom definitions for our own sounds
\ModuleData\project.mbproj - tells engine to include our module_sounds.xml
\ModuleSounds - our audio files (.ogg, .wav)
```

## File format

### module\_sound.xml

```
<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="module_sound">
  <module_sounds>
    <module_sound name="YOUR_SOUND_ID" is_2d="true" sound_category="ui" path="SOUND_FILE_NAME.WAV|OGG" />
  </module_sounds>
</base>
```

#### Pitch

Can set pitch for the sound like this: min\_pitch\_multiplier="0.9" max\_pitch\_multiplier="1.1"

If values are different - random value between min-max is selected. If you need constant pitch - make both values equal.

### project.mbproj

```
<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="solution">
  <file id="soln_module_sound" name="ModuleData/module_sounds.xml" type="module_sound" />
</base>
```

## Sound Categories

```
From: \Native\ModuleData\module_sounds.xml
```

| Category | Duration | Comment |
| --- | --- | --- |
| mission\_ambient\_bed | persistent | General scene ambient |
| mission\_ambient\_3d\_big | persistent | Loud ambient sounds e.g. barn fire |
| mission\_ambient\_3d\_medium | persistent | Common ambient sounds e.g. fireplace |
| mission\_ambient\_3d\_small | persistent | Quiet ambient sounds e.g. torch |
| mission\_material\_impact | max 4 sec. | Weapon clash kind of sounds |
| mission\_combat\_trivial | max 2 sec. | Damage extra foley like armor layer |
| mission\_combat | max 8 sec. | Damage or bow release sounds |
| mission\_foley | max 4 sec. | Other kind of motion fillers |
| mission\_voice\_shout | max 8 sec. | War cries, shouted orders, horse whinnies etc. |
| mission\_voice | max 4 sec. | Grunts, exertions etc. |
| mission\_voice\_trivial | max 4 sec. | Small exertions like jumping |
| mission\_siege\_loud | max 8 sec. | Big explosions, destructions |
| mission\_footstep | max 1 sec. | Human footsteps, foley |
| mission\_footstep\_run | max 1 sec. | Human running (contributes to BASS) |
| mission\_horse\_gallop | max 2 sec. | Loud mount gallop footsteps (contributes to BASS) |
| mission\_horse\_walk | max 1 sec. | Quiet mount footsteps, walk |
| ui | max 4 sec. | All UI sounds |
| alert | max 10 sec. | Pseudo-3D in-mission alerts like warning bells |
| campaign\_node | persistent | Campaign map point sound like rivers etc. |
| campaign\_bed | persistent | Campaign amboent bed |

* Sounds that dont have valid categories wont be played!

## How to play a sound

### Read sound index

```
int soundIndex = SoundEvent.GetEventIdFromString("YOUR_SOUND_ID");
```

### Several ways to play a sound

```
UISoundsHelper.PlayUISound("event:/ui/inventory/take_all");
```

Worked ok:

```
mission.MakeSound(soundIndex, Vec3.Zero, false, true, -1, -1);
```

Did not work:

```
SoundEvent.PlaySound2D(soundIndex);
```

Works:

```
SoundEvent eventRef = SoundEvent.CreateEvent(soundIndex, mission.Scene);
eventRef.SetPosition(_mission.MainAgent.Position);
eventRef.Play();
```

## is2D option

Setting is2D to true makes it play at a constant volume throughout the entire scene.

is\_2d="true" in XML

## Sound in menus

```
args.MenuContext.SetPanelSound("event:/ui/panels/settlement_village");  // one-time sound on opening
args.MenuContext.SetAmbientSound("event:/map/ambient/node/settlements/2d/village");
```

Why does not work on wait-type menus?

## Sound Events

hunharibo: You don't need the event prefix. Only vanilla sounds use 'event:' to identify sounds in the fmod middleware. Since fmod is a licensed proprietary library, modders cant use that

Possible to play sound (events) with InformationManager.AddQuickInformation(text, 0, null, string SoundEventPath)

You need to null the SoundEvent when you leave the mission.

Same with MultiSelectionInquiryData and in the menus

In GauntletUI: HandleQueryCreated played with SoundEvent.PlaySound2D(soundEventPath);

```
SoundEvent.PlaySound2D("event:/ui/notification/quest_fail");
```

soundEventPath looks like:

```
"event:/ui/notification/levelup"
```

Defined in:

* [GAME\_FOLDER]\Modules\Native\ModuleData\sound\_event\_data.gen.xml (1.3.14)
* [GAME\_FOLDER]\Sounds\GUIDs.txt (1.2.12)

event:/ui list

event:/ui/campaign/autobattle\_defeat  
event:/ui/campaign/autobattle\_large  
event:/ui/campaign/autobattle\_small  
event:/ui/campaign/click\_party  
event:/ui/campaign/click\_party\_enemy  
event:/ui/campaign/click\_settlement  
event:/ui/campaign/click\_settlement\_enemy  
event:/ui/campaign/focus  
event:/ui/checkbox  
event:/ui/choice  
event:/ui/conversation  
event:/ui/crafting/craft\_success  
event:/ui/crafting/craft\_tab  
event:/ui/crafting/randomize  
event:/ui/crafting/refine\_success  
event:/ui/crafting/refine\_tab  
event:/ui/crafting/smelt\_success  
event:/ui/crafting/smelt\_tab  
event:/ui/cutscenes/child\_coming\_of\_age  
event:/ui/cutscenes/child\_newborn  
event:/ui/cutscenes/child\_newborn\_mother  
event:/ui/cutscenes/child\_newborn\_orphan  
event:/ui/cutscenes/death\_clan  
event:/ui/cutscenes/death\_clan\_war  
event:/ui/cutscenes/death\_old\_age  
event:/ui/cutscenes/death\_war\_defeat  
event:/ui/cutscenes/death\_war\_victory  
event:/ui/cutscenes/execution  
event:/ui/cutscenes/execution\_female  
event:/ui/cutscenes/kingdom\_created  
event:/ui/cutscenes/kingdom\_destroyed  
event:/ui/cutscenes/kingdom\_join  
event:/ui/cutscenes/marriage  
event:/ui/cutscenes/story\_banner\_assemble\_part\_01  
event:/ui/cutscenes/story\_banner\_assemble\_part\_02  
event:/ui/cutscenes/story\_banner\_find\_01  
event:/ui/cutscenes/story\_banner\_find\_02  
event:/ui/cutscenes/story\_become\_king  
event:/ui/cutscenes/story\_claim  
event:/ui/cutscenes/story\_conspiracy\_activate  
event:/ui/cutscenes/story\_conspiracy\_start  
event:/ui/cutscenes/story\_defeat  
event:/ui/cutscenes/story\_empire\_destroyed  
event:/ui/cutscenes/story\_empire\_united  
event:/ui/cutscenes/story\_pledge\_support  
event:/ui/cutscenes/story\_pledge\_support\_nonempire  
event:/ui/default  
event:/ui/dropdown  
event:/ui/empty  
event:/ui/endgame/end\_clan\_destroyed  
event:/ui/endgame/end\_retirement  
event:/ui/endgame/end\_victory  
event:/ui/endgame/tab\_battles  
event:/ui/endgame/tab\_companions  
event:/ui/endgame/tab\_crafting  
event:/ui/endgame/tab\_finances  
event:/ui/endgame/tab\_general  
event:/ui/filter  
event:/ui/inspect  
event:/ui/inventory/animal  
event:/ui/inventory/animal\_butcher  
event:/ui/inventory/bow  
event:/ui/inventory/crossbow  
event:/ui/inventory/helmet  
event:/ui/inventory/horse  
event:/ui/inventory/horsearmor  
event:/ui/inventory/leather  
event:/ui/inventory/leather\_lite  
event:/ui/inventory/onehanded  
event:/ui/inventory/perk  
event:/ui/inventory/pickup  
event:/ui/inventory/polearm  
event:/ui/inventory/quiver  
event:/ui/inventory/sack  
event:/ui/inventory/shield  
event:/ui/inventory/take\_all  
event:/ui/inventory/throwing  
event:/ui/inventory/twohanded  
event:/ui/inventory/unit  
event:/ui/item\_close  
event:/ui/item\_open  
event:/ui/mini\_popup  
event:/ui/mission/arena\_victory  
event:/ui/mission/ballista  
event:/ui/mission/batteringram  
event:/ui/mission/catapult  
event:/ui/mission/deploy  
event:/ui/mission/drinks  
event:/ui/mission/emptyslot  
event:/ui/mission/horns/attack  
event:/ui/mission/horns/move  
event:/ui/mission/horns/retreat  
event:/ui/mission/multiplayer/changeclass  
event:/ui/mission/multiplayer/defeat  
event:/ui/mission/multiplayer/gamestart  
event:/ui/mission/multiplayer/lastmanstanding  
event:/ui/mission/multiplayer/pointcapture  
event:/ui/mission/multiplayer/pointlost  
event:/ui/mission/multiplayer/pointsremoved  
event:/ui/mission/multiplayer/pointwarning  
event:/ui/mission/multiplayer/roundstart  
event:/ui/mission/multiplayer/victory  
event:/ui/mission/siege\_engine  
event:/ui/mission/siegetower  
event:/ui/multiplayer/click\_class  
event:/ui/multiplayer/click\_culture  
event:/ui/multiplayer/click\_item  
event:/ui/multiplayer/click\_purchase  
event:/ui/multiplayer/coin\_add  
event:/ui/multiplayer/levelup  
event:/ui/multiplayer/match\_ready  
event:/ui/multiplayer/pick\_sigil  
event:/ui/multiplayer/shop\_purchase  
event:/ui/multiplayer/shop\_purchase\_complete  
event:/ui/multiplayer/shop\_purchase\_proceed  
event:/ui/multiplayer/wear\_armor\_big  
event:/ui/multiplayer/wear\_armor\_small  
event:/ui/multiplayer/wear\_generic  
event:/ui/multiplayer/wear\_helmet  
event:/ui/multiplayer/xpbar  
event:/ui/multiplayer/xpbar\_stop  
event:/ui/notification/alert  
event:/ui/notification/army\_created  
event:/ui/notification/army\_dispersion  
event:/ui/notification/child\_born  
event:/ui/notification/coins\_negative  
event:/ui/notification/coins\_positive  
event:/ui/notification/death  
event:/ui/notification/education  
event:/ui/notification/hideout\_found  
event:/ui/notification/kingdom\_decision  
event:/ui/notification/levelup  
event:/ui/notification/marriage  
event:/ui/notification/peace  
event:/ui/notification/peace\_offer  
event:/ui/notification/quest\_fail  
event:/ui/notification/quest\_finished  
event:/ui/notification/quest\_start  
event:/ui/notification/quest\_update  
event:/ui/notification/ransom\_offer  
event:/ui/notification/relation  
event:/ui/notification/settlement\_owner\_change  
event:/ui/notification/settlement\_rebellion  
event:/ui/notification/settlement\_under\_siege  
event:/ui/notification/tournament\_end  
event:/ui/notification/trait\_change  
event:/ui/notification/truce  
event:/ui/notification/unlock  
event:/ui/notification/war\_declared  
event:/ui/oob/dropdown  
event:/ui/oob/formation\_archers  
event:/ui/oob/formation\_cavalry  
event:/ui/oob/formation\_cavalry\_horse\_archers  
event:/ui/oob/formation\_horse\_archers  
event:/ui/oob/formation\_infantry  
event:/ui/oob/formation\_infantry\_archers  
event:/ui/oob/officer\_drag  
event:/ui/oob/officer\_pick  
event:/ui/panels/battle/attack\_large  
event:/ui/panels/battle/attack\_medium  
event:/ui/panels/battle/attack\_small  
event:/ui/panels/battle/encounter\_attacker  
event:/ui/panels/battle/encounter\_defender  
event:/ui/panels/battle/retreat  
event:/ui/panels/battle/slide\_in  
event:/ui/panels/encyclopedia\_open  
event:/ui/panels/encyclopedia\_page  
event:/ui/panels/next  
event:/ui/panels/panel\_character\_open  
event:/ui/panels/panel\_clan\_open  
event:/ui/panels/panel\_inventory\_open  
event:/ui/panels/panel\_kingdom\_open  
event:/ui/panels/panel\_party\_open  
event:/ui/panels/panel\_quest\_open  
event:/ui/panels/panel\_trading\_loop  
event:/ui/panels/perk  
event:/ui/panels/previous  
event:/ui/panels/recruit  
event:/ui/panels/scoreboard\_flags  
event:/ui/panels/scoreboard\_skill  
event:/ui/panels/scoreboard\_tournament  
event:/ui/panels/settlement\_city  
event:/ui/panels/settlement\_hideout  
event:/ui/panels/settlement\_keep  
event:/ui/panels/settlement\_village  
event:/ui/panels/siege/besiege  
event:/ui/panels/siege/engine\_build  
event:/ui/panels/siege/engine\_build\_complete  
event:/ui/panels/siege/engine\_click  
event:/ui/panels/siege/lead\_assault  
event:/ui/panels/siege/raid  
event:/ui/panels/siege/sally\_out  
event:/ui/panels/skill\_add  
event:/ui/panels/skill\_new  
event:/ui/panels/tutorial  
event:/ui/panels/twopanel\_open  
event:/ui/panels/upgrade  
event:/ui/party/recruit prisoner  
event:/ui/party/recruit\_all  
event:/ui/party/upgrade  
event:/ui/persuasion/critical\_fail  
event:/ui/persuasion/critical\_success  
event:/ui/persuasion/ineffective  
event:/ui/persuasion/success  
event:/ui/popup  
event:/ui/reign/decision  
event:/ui/reign/vote  
event:/ui/sort  
event:/ui/stealth/stealth\_notification  
event:/ui/tab  
event:/ui/transfer

Some [info](https://discord.com/channels/411286129317249035/697071354603765791/1096586744301887629) about events

* the event:/ derives from the FMOD event paths that the game uses to play sound events
* however when we mod the game’s audio we don’t have access to the FMOD project and their events, so we have to replace entire events with singular oneshots
* the event:/ is part of an event path
* for example, “event:/Ambience/Rain” would be an event called Rain located in a folder called Ambience

Sound examples

ui/mission sounds

ui/multiplayer sounds

ui/notification sounds

alerts sounds

## Issues with sound volume using SoundEvent

Windwhistle: My findings so far:

When using SoundEvent to play a custom sound in module\_sounds.xml from the sound category "mission\_ambient\_3d\_small" and "mission\_ambient\_3d\_medium", if the attribute is\_2d is set to false, the volume of the sound at max volume (close to the sound source) is directly related to the distance at which it was spawned to the player.

So, if you set the position of the SoundEvent FARTHER from the player, it will be FAINTER at MAX volume. If you set the position of the SoundEvent CLOSER to the player, it will be LOUDER at MAX volume.

It draws confusion, as I do not mean that the sound becomes fainter as you travel farther away from it. That's normal. I am not talking about that. There's some videos if you scroll up that depict what I am talking about, because it's pretty hard to describe.

To fix this: I just set is\_2d to true instead of false, and the sound plays how I wanted it to play.

Why: I don't know. I'm going to test it out in a regular scene/mission to see if it's my code somehow, or if it's a genuine bug. It might not even be a bug but intentional, but it'd be weird if it were.

EDIT: it also suffers from the same problem in a regular scene. Setting the position repeatedly on tick also does not alleviate the issue. Making the file format .wav also did not help.

[Source](https://discord.com/channels/411286129317249035/677511186295685150/1123190415420555344)

## Delayed stop to prevent ambient sounds from looping

```
private async void DelayedStop(SoundEvent eventRef)
{
  await Task.Delay(soundDuration);
  eventRef.Stop();
}
```

How to get sound duration?

Stop looping example

```
SoundEvent eventRef = SoundEvent.CreateEvent(SoundEvent.GetEventIdFromString(SoundEffectOnUse), Scene);
eventRef.SetPosition(GameEntity.GetGlobalFrame().origin);
eventRef.Play();
DelayedStop(eventRef); // used to prevent ambient sounds from looping
```

## Short sounds for agents in Mission

```
agent.MakeVoice(SkinVoiceManager.VoiceType.Grunt, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
```

SkinVoiceManager.VoiceType examples

* Grunt - eeh
* Jump - yah
* Yell - eyah, hey, yeee
* Pain - uh, mhmph
* Death - uuuuhmmm
* Stun - yyy, aaaa, eeee
* Fear - yaaaaah, aaaah
* Climb - ?
* Focus - (silence?)
* Debacle - aaaaaaah, shriek/screech
* and many others...

## Custom Music

Create your own soundtrack.xml in YOURMOD/music/soundtrack.xml

You can take native file as a starting point from: `SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\Music\soundtrack.xml` or `Modules\NavalDLC\Music...`

```
using System;
using HarmonyLib;
using psai.net;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ModuleManager;

namespace Patches
{
    [HarmonyPatch]
    public static class MBMusicManagerPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MBMusicManager), "Initialize")]
        public static void OverrideCreation()
        {
            if (!NativeConfig.DisableSound)
            {
                string corePath = ModuleHelper.GetModuleFullPath("YOURMOD") + "music/soundtrack.xml";
                PsaiCore.Instance.LoadSoundtrackFromProjectFile(corePath);
            }
        }

        // this will only work if you have custom audio? with ID 401. By default comment this out to play default menu music
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MBMusicManager), "ActivateMenuMode")]
        public static bool UseTowMenuMusicId(ref MBMusicManager __instance)
        {
            Random rnd = new Random();
            int index = rnd.Next(401, 401);
            typeof(MBMusicManager).GetProperty("CurrentMode").SetValue(__instance, MusicMode.Menu);
            PsaiCore.Instance.MenuModeEnter(index, 0.5f);
            return false;
        }
    }
}
```

In your MOD/music/soundtrack.xml you can reference native audio like this:

Original:

```
<Path>medieval_04_DG2_ARO_the_maiden.wav</Path>
```

After the patch:

```
<Path>../../Native/Music/medieval_04_DG2_ARO_the_maiden.wav</Path>
```

Game adds '/PC' before file name automatically.

## Custom Music in Taverns

By culture:

```
Sandbox\ModuleData\settlement_tracks.xml
```

## Custom Music in Main Menu

Source [here](https://www.nexusmods.com/mountandblade2bannerlord/mods/5233)

1. Find the music
2. Convert to OGG
3. In \Mount & Blade II Bannerlord\music\Soundtrack.XML find Maintheme and under it: <TotalLengthInSamples>
4. Change the number to your sample length using:

   ![](https://docs.bannerlordmodding.lt/pics/2402262117.png)

   or multiply 44305 X {Your song length in seconds}
5. Backup \Mount & Blade II Bannerlord\music\PC\Maintheme.ogg
6. Rename your music ogg file to Maintheme.ogg and place it in \Mount & Blade II Bannerlord\music\PC\

## Custom Music in Culture selection menu

Requires patching:

```
public class CharacterCreationCultureStageView : CharacterCreationStageViewBase
{
.....
  private void OnCultureSelected(CultureObject culture)
  {
      MissionSoundParametersView.SoundParameterMissionCulture soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.None;
      if (culture.StringId == "aserai")
      {
          soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.Aserai;
      }
      else if (culture.StringId == "khuzait")
      {
          soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.Khuzait;
      }
      else if (culture.StringId == "vlandia")
      {
          soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.Vlandia;
      }
      else if (culture.StringId == "sturgia")
      {
          soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.Sturgia;
      }
      else if (culture.StringId == "battania")
      {
          soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.Battania;
      }
      else if (culture.StringId == "empire")
      {
          soundParameterMissionCulture = MissionSoundParametersView.SoundParameterMissionCulture.Empire;
      }

      SoundManager.SetGlobalParameter("MissionCulture",(float)soundParameterMissionCulture);
  }
...
}
```

More info by [-Mr. E-](https://discord.com/channels/411286129317249035/677511186295685150/1259676285756641401)

## Game Sound Settings

Get/set Sound/Music Volume:

```
NativeOptions.GetConfig(NativeOptions.NativeOptionsType.SoundVolume)
NativeOptions.GetConfig(NativeOptions.NativeOptionsType.MusicVolume)
NativeOptions.SetConfig(NativeOptions.NativeOptionsType.SoundVolume, _soundVolume);
NativeOptions.SetConfig(NativeOptions.NativeOptionsType.MusicVolume, _musicVolume);
```

## Manage Game Music

Stop the music with fade-out:

```
PsaiCore.Instance.StopMusic(true, 1.5f);
```

Continue playing the music:

```
PsaiCore.Instance.ReturnToLastBasicMood(true);
```

## Extract native sounds

Use [Fmod Bank Tools](https://www.nexusmods.com/rugbyleaguelive3/mods/2?tab=docs)

![](https://docs.bannerlordmodding.lt/pics/2409030815.png)

## Custom culture dialog voices

[Different dialects for your new culture's NPCs](https://docs.bannerlordmodding.lt/guides/custom_culture/#dialog-voices)

## NAudio

Artem:

This is for everyone who tries to add sounds to the game and is annoyed by the game restrictions, like sounds being too short, too quiet, etc.

<https://github.com/naudio/NAudio>

Install NAudio

RMB on your project:  
![](https://docs.bannerlordmodding.lt/pics/2404110856a.jpg)

![](https://docs.bannerlordmodding.lt/pics/2404110856b.jpg)

![](https://docs.bannerlordmodding.lt/pics/2404110856c.jpg)

![](https://docs.bannerlordmodding.lt/pics/2404110856d.jpg)

Example Play Audio method

```
using NAudio.Wave;

public static async void PlayNAudioSound(string audioFileName, double soundLevel = 1.0, double musicLevel = 1.0, bool soundLevelByNativeMusic = false, bool fadeOutNativeMusic = false)
{

    if (audioFileName.Length == 0) return;

    string _audioLocation;
    AudioFileReader _audioFile;
    WaveOutEvent _outputDevice = _outputDevice = new WaveOutEvent();

    string fullName = Directory.GetParent(Directory.GetParent(Directory.GetParent("YOUR_MODULE").FullName).FullName).FullName;
    _audioLocation = System.IO.Path.Combine(fullName, "Modules\\YOUR_MODULE\\ModuleSounds\\General\\");
    try
    {

        _audioFile = new AudioFileReader(_audioLocation + audioFileName + ".wav");

        // set sound level by native sound or music level
        if (soundLevelByNativeMusic)
        {
            _audioFile.Volume = NativeOptions.GetConfig(NativeOptions.NativeOptionsType.MusicVolume) * (float)musicLevel;
        }
        else
        {
            _audioFile.Volume = NativeOptions.GetConfig(NativeOptions.NativeOptionsType.SoundVolume) * (float)soundLevel;
        }

        // take into account Master Volume level
        _audioFile.Volume = _audioFile.Volume * NativeOptions.GetConfig(NativeOptions.NativeOptionsType.MasterVolume);

        _outputDevice.Init(_audioFile);

        if (fadeOutNativeMusic) PsaiCore.Instance.StopMusic(true, 1.5f); // stop native Music with fade-out

        _audioFile.Seek(0L, SeekOrigin.Begin);
        _outputDevice.Play();

        // calculate the duration of the audio
        // Get the total number of samples in the file
        long totalSamples = _audioFile.Length / (_audioFile.WaveFormat.BitsPerSample / 8 * _audioFile.WaveFormat.Channels);
        // Calculate the total duration in milliseconds
        double durationMs = (double)totalSamples / _audioFile.WaveFormat.SampleRate * 1000;

        await Task.Delay((int)durationMs);  // wait till audio stops playing

        if (fadeOutNativeMusic) PsaiCore.Instance.ReturnToLastBasicMood(true); // restore native Music

        // cleanup
        if (_outputDevice != null)
        {
            _outputDevice.Stop();
            _outputDevice.Dispose();
        }

    }
    catch
    {
            // ERROR   ($"ERROR: can't play NAudio sound: {audioFileName}. Missing folders/files? " + _audioLocation);
    }

}
```

Implementation example: [Artem's Assassination Mod](https://www.nexusmods.com/mountandblade2bannerlord/mods/5653)

hunharibo: I expanded on the NAudio integration idea a lot in TOR. Made a MixingSampleProvider so you can actually play multiple sound sources at the same time if you wanted to. It only takes 48khz stereo vorbis files though. [LINK](https://github.com/TheOldRealms/TOR_Core/tree/development/CSharpSourceCode/Audio). Also has rudimentary 3d spatial sound with stereo panning and distance attenuation

OGG for me worked only with NAudio v2.0.0 (crash otherwise)

(VS) Tools - NuGet Package Manager - Package Manager Console  
`Install-Package NAudio -Version 2.0.0`

CRASH:  
Type: System.IO.FileNotFoundException  
Message: Could not load file or assembly 'NAudio.Core, Version=2.0.0.0, Culture=neutral, PublicKeyToken=e279aa5131008a41' or one of its dependencies. The system cannot find the file specified.
