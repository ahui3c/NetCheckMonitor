using System;
using System.Globalization;

namespace NetCheck
{
    internal static class L
    {
        private static readonly bool traditionalChinese = DetectTraditionalChinese();

        public static bool TraditionalChinese { get { return traditionalChinese; } }
        public static string HtmlLanguage { get { return traditionalChinese ? "zh-Hant" : "en"; } }
        public static string T(string traditionalChineseText, string englishText) { return traditionalChinese ? traditionalChineseText : englishText; }

        internal static bool IsTraditionalChineseCulture(string name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            return name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase);
        }

        private static bool DetectTraditionalChinese()
        {
            string testOverride = Environment.GetEnvironmentVariable("NETCHECK_UI_LANGUAGE");
            string name = String.IsNullOrEmpty(testOverride) ? CultureInfo.InstalledUICulture.Name : testOverride;
            return IsTraditionalChineseCulture(name);
        }
    }
}
