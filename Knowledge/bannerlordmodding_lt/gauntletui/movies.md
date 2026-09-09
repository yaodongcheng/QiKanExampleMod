# Movies

<!-- 源: https://docs.bannerlordmodding.lt/gauntletui/movies/ | 抓取日期: 2026-09-09 -->

How can I get gauntletmovie which is currently open?

```
ScreenManager.TopScreen.Layers.FirstOrDefault().MoviesAndDataSources.FirstOrDefault()
```

This returns a tuple: Item1 GauntletMovie, Item2 ViewModel
