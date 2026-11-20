using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 读 `GauntletLayer` 上挂了哪些 movie —— **两个大版本结构不同，必须分别走**（2026-11-20 实测）：
    ///   · 1.2.12：`public readonly List<Tuple&lt;IGauntletMovie, ViewModel&gt;&gt; MoviesAndDataSources`，没有 GetMovieIdentifier
    ///   · 1.5.x ：`private readonly MBList&lt;GauntletMovieIdentifier&gt; _movieIdentifiers` + `GetMovieIdentifier(name)`
    /// 之前只按 1.5.7 的字段名反射，在 1.2.12 上永远取不到 —— dump 0 个控件、血条也找不到。
    /// </summary>
    internal static class GauntletUiIntrospect
    {
        public static IEnumerable<(object Movie, object ViewModel)> GetMovies(ScreenLayer layer)
        {
            if (layer == null) yield break;

            // 1.2.12 路线
            object raw = null;
            var f1 = layer.GetType().GetField("MoviesAndDataSources", BindingFlags.Public | BindingFlags.Instance);
            if (f1 != null) raw = f1.GetValue(layer);
            if (raw is IEnumerable list1)
            {
                foreach (var item in list1)
                {
                    if (item == null) continue;
                    var t = item.GetType();
                    var movie = t.GetProperty("Item1")?.GetValue(item);
                    var vm = t.GetProperty("Item2")?.GetValue(item);
                    if (movie != null) yield return (movie, vm);
                }
            }

            // 1.5.x 路线
            FieldInfo f2 = null;
            var t2 = layer.GetType();
            while (t2 != null && f2 == null)
            {
                f2 = t2.GetField("_movieIdentifiers", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                t2 = t2.BaseType;
            }
            if (f2?.GetValue(layer) is IEnumerable list2)
            {
                foreach (var item in list2)
                {
                    if (item == null) continue;
                    var movie = item.GetType().GetProperty("Movie")?.GetValue(item);
                    if (movie != null) yield return (movie, null);
                }
            }
        }

        public static Widget GetRootWidget(object movie)
        {
            try { return movie?.GetType().GetProperty("RootWidget")?.GetValue(movie) as Widget; }
            catch { return null; }
        }

        public static string GetMovieName(object movie, string fallback)
        {
            try
            {
                var n = movie?.GetType().GetProperty("MovieName")?.GetValue(movie) as string;
                return string.IsNullOrEmpty(n) ? fallback : n;
            }
            catch { return fallback; }
        }
    }
}