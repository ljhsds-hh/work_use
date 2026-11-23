using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace XiaoDing.UI
{
    /// <summary>
    /// 全局配色与排版。全应用统一微软雅黑 UI（楷体笔画过细，小字号下会断裂发虚），纸感暖白配墨绿，支持浅色与深色两套配色。
    /// </summary>
    public static class Theme
    {
        private static bool _dark;

        public static Color Paper { get; private set; }
        public static Color PaperMuted { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceMuted { get; private set; }

        public static Color Border { get; private set; }
        public static Color BorderStrong { get; private set; }

        public static Color Text { get; private set; }
        public static Color TextMuted { get; private set; }
        public static Color TextFaint { get; private set; }

        public static Color Brand { get; private set; }
        public static Color BrandHover { get; private set; }
        public static Color BrandDark { get; private set; }
        public static Color BrandSoft { get; private set; }
        public static Color BrandOn { get; private set; }
        /// <summary>品牌渐变高光，用于主按钮/品牌块的顶部提亮。</summary>
        public static Color BrandShine { get; private set; }
        /// <summary>极浅品牌底色，用于徽标、计数气泡。</summary>
        public static Color BrandTint { get; private set; }

        public static Color Amber { get; private set; }
        public static Color AmberSoft { get; private set; }
        public static Color Danger { get { return Amber; } }
        public static Color OverlayDim { get; private set; }

        public static Color Selection { get; private set; }
        public static Color ShadowTint { get; private set; }

        private static readonly string FontFamilyName = ResolveFontFamily();

        public static Font FontBody { get; private set; }
        public static Font FontSmall { get; private set; }
        public static Font FontTitle { get; private set; }
        public static Font FontButton { get; private set; }
        public static Font FontHero { get; private set; }
        public static Font FontRowTitle { get; private set; }
        public static Font FontTag { get; private set; }
        public static Font FontMeta { get; private set; }
        public static Font FontSection { get; private set; }
        public static Font FontNav { get; private set; }

        public static float ScaleFactor { get; private set; }

        public static bool IsDark
        {
            get { return _dark; }
        }

        public static void Initialize()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                ScaleFactor = Math.Max(1f, g.DpiX / 96f);
            }

            FontBody = new Font(FontFamilyName, 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontSmall = new Font(FontFamilyName, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontTitle = new Font(FontFamilyName, 12.5f, FontStyle.Bold, GraphicsUnit.Point);
            FontButton = new Font(FontFamilyName, 10f, FontStyle.Regular, GraphicsUnit.Point);
            FontHero = new Font(FontFamilyName, 15f, FontStyle.Bold, GraphicsUnit.Point);
            FontRowTitle = new Font(FontFamilyName, 11f, FontStyle.Regular, GraphicsUnit.Point);
            FontTag = new Font(FontFamilyName, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            FontMeta = new Font(FontFamilyName, 10f, FontStyle.Regular, GraphicsUnit.Point);
            FontSection = new Font(FontFamilyName, 10f, FontStyle.Bold, GraphicsUnit.Point);
            FontNav = new Font(FontFamilyName, 10.5f, FontStyle.Regular, GraphicsUnit.Point);

            ApplyTheme(false);
        }

        /// <summary>切换浅色/深色。切换后需要重建窗口才能让系统控件跟随。</summary>
        public static void ApplyTheme(bool dark)
        {
            _dark = dark;

            if (dark)
            {
                Paper = Color.FromArgb(22, 24, 26);
                PaperMuted = Color.FromArgb(16, 18, 21);
                Surface = Color.FromArgb(30, 33, 36);
                SurfaceMuted = Color.FromArgb(35, 38, 41);
                Border = Color.FromArgb(64, 70, 76);
                BorderStrong = Color.FromArgb(86, 93, 100);
                Text = Color.FromArgb(240, 237, 230);
                TextMuted = Color.FromArgb(176, 169, 158);
                TextFaint = Color.FromArgb(143, 136, 125);
                Brand = Color.FromArgb(79, 169, 138);
                BrandHover = Color.FromArgb(99, 190, 158);
                BrandDark = Color.FromArgb(61, 139, 113);
                BrandSoft = Color.FromArgb(30, 46, 40);
                BrandOn = Color.FromArgb(18, 22, 21);
                BrandShine = Color.FromArgb(108, 200, 166);
                BrandTint = Color.FromArgb(38, 58, 50);
                Amber = Color.FromArgb(224, 138, 92);
                AmberSoft = Color.FromArgb(46, 35, 32);
                OverlayDim = Color.FromArgb(6, 7, 8);
                Selection = Color.FromArgb(38, 48, 41);
                ShadowTint = Color.Black;
            }
            else
            {
                Paper = Color.FromArgb(246, 243, 236);
                PaperMuted = Color.FromArgb(239, 235, 225);
                Surface = Color.FromArgb(255, 253, 248);
                SurfaceMuted = Color.FromArgb(250, 247, 240);
                Border = Color.FromArgb(221, 214, 201);
                BorderStrong = Color.FromArgb(198, 188, 169);
                Text = Color.FromArgb(30, 27, 23);
                TextMuted = Color.FromArgb(87, 80, 74);
                TextFaint = Color.FromArgb(110, 102, 92);
                Brand = Color.FromArgb(31, 92, 74);
                BrandHover = Color.FromArgb(44, 122, 98);
                BrandDark = Color.FromArgb(24, 73, 59);
                BrandSoft = Color.FromArgb(228, 239, 233);
                BrandOn = Color.White;
                BrandShine = Color.FromArgb(52, 133, 107);
                BrandTint = Color.FromArgb(233, 242, 237);
                Amber = Color.FromArgb(180, 86, 42);
                AmberSoft = Color.FromArgb(246, 231, 220);
                OverlayDim = Color.FromArgb(12, 14, 16);
                Selection = Color.FromArgb(236, 241, 237);
                ShadowTint = Color.FromArgb(70, 56, 38);
            }
        }

        /// <summary>按当前屏幕 DPI 换算设计像素。</summary>
        public static int S(int pixels)
        {
            return (int)Math.Round(pixels * ScaleFactor);
        }

        public static float SF(float pixels)
        {
            return pixels * ScaleFactor;
        }

        public static TextRenderingHint TextHint
        {
            get { return TextRenderingHint.AntiAliasGridFit; }
        }

        /// <summary>按字号取楷体。</summary>
        public static Font DisplayFont(float size, FontStyle style)
        {
            return new Font(FontFamilyName, size, style, GraphicsUnit.Point);
        }

        private static string ResolveFontFamily()
        {
            // 楷体属于书法字体，笔画细且粗细不均，9-11pt 下 GDI+ 栅格化容易断线发虚；
            // 微软雅黑 UI 是系统界面字体，笔画等宽、专为小字号优化，Win10/11 自带。
            string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "KaiTi", "STKaiti", "楷体" };
            string found = FindInstalled(candidates);
            return found ?? SystemFonts.MessageBoxFont.FontFamily.Name;
        }

        private static string FindInstalled(string[] candidates)
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
            {
                FontFamily[] families = fonts.Families;
                foreach (string candidate in candidates)
                {
                    foreach (FontFamily family in families)
                    {
                        if (string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }
    }
}
