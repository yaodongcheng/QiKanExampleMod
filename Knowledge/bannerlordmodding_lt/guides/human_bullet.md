# Human-Bullet / Folded-Man problem

<!-- 源: https://docs.bannerlordmodding.lt/guides/human_bullet/ | 抓取日期: 2026-09-09 -->

[

Your browser does not support the video tag.
](https://docs.bannerlordmodding.lt/pics/human_bullet.webm)

## Solution

Instead of automatically patching any method that touches Agents by calling `Harmony.PatchAll` at `OnSubModuleLoad`, you have to patch manually at a later point,
such as `OnGameInitializationFinished` or `OnGameStart` by calling `Harmony.Patch`.

Refer to this for more details: <https://harmony.pardeike.net/articles/basics.html#manual-patching>

And don't forget to call `Harmony.Unpatch` at OnGameEnd

## action\_sets

Artem: When I added new actions via `action_sets.xslt` files this happened aswell

Solution: just use `action_sets.xml`

---

![](https://docs.bannerlordmodding.lt/pics/how-to-escape-death-v0-vwe7vmih1skg1.png)
