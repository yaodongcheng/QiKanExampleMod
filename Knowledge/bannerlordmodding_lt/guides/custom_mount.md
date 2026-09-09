# Custom Mount

<!-- 源: https://docs.bannerlordmodding.lt/guides/custom_mount/ | 抓取日期: 2026-09-09 -->

Artem:

To implemement custom mount you'd need:

* action\_sets.xml (for mount animations)
* action\_sets.xslt (for human animations for the new mount)
* action\_types.xslt (to reference the animations and to add types of animations (example actt\_idle, actt\_hit\_object, etc)
* monsters.xml (referencing bones, speed, weight and other important stuff)
* items.xml (mounts are just same as items so you need to add them as a item aswell)
* monster\_usage.xml (you need to create a new usage set for your new mount if not using horse skeleton)
* monster\_usage\_sets.xslt (needed to add mounting, dismounting, falling animations (otherwise it would crash))

and tinker with the skeleton editor inside modding tools.
