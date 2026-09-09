# dnSpy

<!-- 源: https://docs.bannerlordmodding.lt/resources/dnspy/ | 抓取日期: 2026-09-09 -->

.NET debugger and assembly editor

## Download

* [Original discontinued version](https://github.com/dnSpy/dnSpy/releases)
* [Unofficial revival](https://github.com/dnSpyEx/dnSpy/releases)

## Guides

* [How to DEBUG Bannerlord](https://www.nexusmods.com/mountandblade2bannerlord/mods/2667)
* [Read the Game Code with dnSpy](https://www.youtube.com/watch?v=SUBcBx9WWgA&list=PLzebdAxJeltRwfJ8jzsNolgHkRvLjoCRC&index=8)

## Setup

* [Setup video](https://youtu.be/SUBcBx9WWgA?list=PLzebdAxJeltRwfJ8jzsNolgHkRvLjoCRC)

### What DLLs to Open

* M&B/bin/Win64\_Shipping\_Client - all DLLs that start with TaleWorlds...
* M&B/Modules/Native/bin/Win64\_Shipping\_Client - all DLLs
* M&B/Modules/Sandbox/bin/Win64\_Shipping\_Client - all DLLs
* M&B/Modules/NavalDLC/bin/Win64\_Shipping\_Client - all DLLs (DLC)

## How to attach dnSpy to Bannerlord and catch the exception

* Launch the Bannerlord launcher (not the game itself!)
* Run dnSpy
* In dnSpy, choose the command Debug - Attach to process...
* Select the process corresponding to Bannerlord, then click Attach to confirm
* If all goes well, you will see a "Running..." status confirming that dnSpy is attached to the Bannerlord process
* Start the game. Play until a critical error occurs
* You will see an error message on the screen

[Source with pictures here by LogRaam](https://www.nexusmods.com/mountandblade2bannerlord/mods/2667)

### 1.3+

Add /no\_watchdog to the Steam

![](https://docs.bannerlordmodding.lt/pics/2512070856.png)

Attach dnSpy not in the Launcher but when game is in the main menu state.

Dejan

We have also made these two modding related changes with v121, for anyone interested:  
Added the ability to detach the crash dump upload system (watchdog) at runtime via Utilities.DetachWatchdog routine.  
Added a command line functionality "no\_watchdog" which prevents the game from initializing the system.

## Not working breakpoints

![](https://docs.bannerlordmodding.lt/pics/2402181413.png)

In the module's .csproj file make sure you have

```
<TargetFramework>net472</TargetFramework>
```

and not

```
<TargetFramework>net472,netstandard2.0</TargetFramework>
```

or anything else. Recompile and should be ok.

## Crash on the game start

![](https://docs.bannerlordmodding.lt/pics/2408271435.png)

Bad prefix/postfix argument:

```
    public static void Postfix(MenuCallbackArgs args)

    //should be:

    public static void Postfix()
```

What is interesting, game runs fine if not dnSpy attached.

---

Crash only when attached to the dnSpy.

![](https://docs.bannerlordmodding.lt/pics/2403121926.png)

The cause is RTS Camera mod. Disable it and game will start.

## IL Instructions / OpCodes

![](https://docs.bannerlordmodding.lt/pics/2404290840.png)
