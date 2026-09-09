# Material Issues

<!-- 源: https://docs.bannerlordmodding.lt/3d/material_issues/ | 抓取日期: 2026-09-09 -->

## do\_not\_use\_vertex\_color\_as\_occlusion

When the model has Vertex Paint and black parts in Vertex Paint looks dark on the model:

![](https://docs.bannerlordmodding.lt/pics/2410041733.png)

That means that do\_not\_use\_vertex\_color\_as\_occlusion is OFF. Should be ON.

![](https://docs.bannerlordmodding.lt/pics/2410041736.png)

## disable\_vertex\_color\_alpha

Holes in the model:

![](https://docs.bannerlordmodding.lt/pics/2410041739.png)

Make disable\_vertex\_color\_alpha ON:

![](https://docs.bannerlordmodding.lt/pics/2410041740.png)
