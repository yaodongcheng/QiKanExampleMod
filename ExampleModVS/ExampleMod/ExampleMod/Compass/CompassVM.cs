using System;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>罗盘刻度/方向字母条目（位置由 CompassHud 每帧注入）。刻度无文本、字母有文本，共用一类。</summary>
    public class CompassTickVM : ViewModel
    {
        /// <summary>世界方位角（度，0=北/+Y，顺时针为正；刻度 45° 间隔、字母 90° 间隔）。</summary>
        public float AngleDeg { get; private set; }

        private float _posX;
        private bool _isVisible;
        private string _text;

        public CompassTickVM(float angleDeg, string text = null)
        {
            AngleDeg = angleDeg;
            Text = text ?? "";
            IsVisible = false;
            PosX = 0f;
        }

        /// <summary>由 CompassHud 注入：带内 x 坐标（中心对齐偏移已计入）+ 可见性。</summary>
        public void SetCompass(float posX, bool visible)
        {
            if (Math.Abs(posX - _posX) > 0.01f) PosX = posX;
            if (visible != _isVisible) IsVisible = visible;
        }

        [DataSourceProperty]
        public float PosX
        {
            get => _posX;
            set { if (Math.Abs(value - _posX) > 0.01f) { _posX = value; OnPropertyChangedWithValue(value, "PosX"); } }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (value != _isVisible) { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (value != _text) { _text = value; OnPropertyChangedWithValue(value, "Text"); } }
        }
    }

    /// <summary>
    /// 顶部罗盘 VM：刻度带（8 刻度 + 4 方向字母）+ 任务人物图标列表 + 总可见性。
    /// 位置全部由 CompassHud 每帧注入（VM↔XML 同步铁律：新增 [DataSourceProperty] 必须同步改 Compass.xml）。
    /// </summary>
    public class CompassVM : ViewModel
    {
        /// <summary>刻度线方位角（45° 间隔，与字母错开 22.5°，避免刻度压在字母上）。</summary>
        public static readonly float[] TickAngles = { -157.5f, -112.5f, -67.5f, -22.5f, 22.5f, 67.5f, 112.5f, 157.5f };
        /// <summary>方向字母方位角（N=0 / E=90 / S=180 / W=-90）。</summary>
        public static readonly float[] LetterAngles = { 0f, 90f, 180f, -90f };

        public MBBindingList<CompassTickVM> TickItems { get; private set; }
        public MBBindingList<CompassTickVM> LetterItems { get; private set; }
        public MBBindingList<CompassIconVM> IconItems { get; private set; }

        private bool _isVisible;
        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (value != _isVisible) { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }

        public CompassVM()
        {
            TickItems = new MBBindingList<CompassTickVM>();
            LetterItems = new MBBindingList<CompassTickVM>();
            IconItems = new MBBindingList<CompassIconVM>();

            // 刻度 8 根（45° 间隔）
            foreach (float a in TickAngles)
                TickItems.Add(new CompassTickVM(a));
            // 方向字母 4 个（本地化，铁律 13：字母跟随语言 N/E/S/W ↔ 北/东/南/西）
            for (int i = 0; i < LetterAngles.Length; i++)
                LetterItems.Add(new CompassTickVM(LetterAngles[i], ""));

            RefreshLetterTexts();

            IsVisible = false;
        }

        /// <summary>
        /// 刷新方向字母文本（标准本地化：ResolveText + {=LWN_compass_north} + 双语言 XML）。
        /// 🔴 2026-08-20 实机（英文版仍显示中文）：VM 构造时解析一次会被缓存，游戏内切语言
        /// 不重建 VM → 必须由 CompassHud 每 ~60 帧（1 秒）调用本方法刷新，切语言 1 秒内生效。
        /// </summary>
        public void RefreshLetterTexts()
        {
            string[] letterKeys = { "LWN_compass_north", "LWN_compass_east", "LWN_compass_south", "LWN_compass_west" };
            string[] fallbacks = { "N", "E", "S", "W" };
            for (int i = 0; i < LetterItems.Count && i < letterKeys.Length; i++)
                LetterItems[i].Text = LWNTextHelper.ResolveText(letterKeys[i], fallbacks[i]);
        }
    }
}
