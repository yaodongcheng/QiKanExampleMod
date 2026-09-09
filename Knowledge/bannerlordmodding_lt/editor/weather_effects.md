# Dynamic Weather Effects

<!-- 源: https://docs.bannerlordmodding.lt/editor/weather_effects/ | 抓取日期: 2026-09-09 -->

![](https://docs.bannerlordmodding.lt/pics/ee_snow_rain.gif)

Set in the Terrain Properties (Alt+7):

![](https://docs.bannerlordmodding.lt/pics/2402201751.png)

If you need - download vanilla main\_map\_snow\_flowmap.png [here](https://docs.bannerlordmodding.lt/pics/main_map_snow_flowmap.png)

Make changes for your map, import and use your image.

This image encodes different information in Red, Green and Blue channels:

* **Red** is for snow
* **Green** is for clouds and precipitation
* **Blue** is where you don't want snow

![](https://docs.bannerlordmodding.lt/pics/weather_channels.gif)

## **Red Channel**

This channel encodes information where and when snow will appear. White color intensity determines when snow will be shown.

For example: intensity 70-75 is for a snow only in the winter. 255 - for eternal snow.

![](https://docs.bannerlordmodding.lt/pics/2402201805.png)

For the best-fastest result I used Photoshop's Clone Stamp Tool to create my snow map from the vanilla one:

![](https://docs.bannerlordmodding.lt/pics/2402201809.png)

## **Green Channel**

This channel is for for clouds and precipitation.

I don't know how to decode the vanilla bubbles, so again - copy/pasted to cover the whole area:

![](https://docs.bannerlordmodding.lt/pics/2402201813.png)

## **Blue Channel**

This channel is where you don't want snow. It's a simple mask, where white - zones where no snow will be present.

![](https://docs.bannerlordmodding.lt/pics/2402202201.png)

Usually used to cover the sea beds and lakes.

I used different levels of white (100-150-200) for different river coverage in winter:

![](https://docs.bannerlordmodding.lt/pics/2402201822.png)
