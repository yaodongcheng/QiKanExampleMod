# Create a new Module

<!-- 源: https://docs.bannerlordmodding.lt/modules/create_a_new_module/ | 抓取日期: 2026-09-09 -->

* Based on [Lesser Scholar's Tutorial #1](https://youtu.be/gb3YA-1ml7E?t=736)
* Using [BUTR / Bannerlord.Module.Template](https://github.com/BUTR/Bannerlord.Module.Template)

## Install the template

In PowerShell run:

```
dotnet new --install Bannerlord.Templates
```

## Set Environment Variable

![](https://docs.bannerlordmodding.lt/pics/OrsHtAd.png)

## Create a Module

In PowerShell:

```
cd YOUR_FOLDER_WHERE_YOU_KEEP_ALL_YOUR_MODS
dotnet new blmodfx --name "YOUR_MOD_NAME_HERE"
```

## Open in the Visual Studio

Go to newly created module's folder and press on .csproj file

## If Breakpoints and Hot Reload does not work

The breakpoint will not currently be hit. No symbols have been loaded for this document.

To fix in the .csproj file change line:

```
    <TargetFramework>netstandard2.0</TargetFramework>
```

to

```
    <TargetFramework>net472</TargetFramework>
```
