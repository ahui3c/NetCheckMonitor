using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NetCheck
{
    internal static class LanguagePreferenceStore
    {
        internal const string TraditionalChinese = "zh-TW";
        internal const string English = "en-US";

        internal static string Load()
        {
            string testOverride = Environment.GetEnvironmentVariable("NETCHECK_UI_LANGUAGE");
            string normalized = Normalize(testOverride);
            if (normalized != null) return normalized;
            try { return Normalize(File.ReadAllText(PreferencePath(), Encoding.UTF8).Trim()); }
            catch { return null; }
        }

        internal static void Save(string language)
        {
            string normalized = Normalize(language);
            if (normalized == null) throw new ArgumentException("Unsupported interface language.", "language");
            string path = PreferencePath();
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, normalized, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null, true);
                else File.Move(temp, path);
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
        }

        internal static string Normalize(string language)
        {
            if (String.IsNullOrWhiteSpace(language)) return null;
            if (language.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || language.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                || language.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)) return TraditionalChinese;
            if (language.Equals("en", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return English;
            return null;
        }

        internal static bool RunStorageSelfTest(string path)
        {
            string previousFile = Environment.GetEnvironmentVariable("NETCHECK_UI_LANGUAGE_FILE");
            string previousLanguage = Environment.GetEnvironmentVariable("NETCHECK_UI_LANGUAGE");
            try
            {
                Environment.SetEnvironmentVariable("NETCHECK_UI_LANGUAGE_FILE", path);
                Environment.SetEnvironmentVariable("NETCHECK_UI_LANGUAGE", null);
                if (File.Exists(path)) File.Delete(path);
                bool firstRun = Load() == null;
                Save(TraditionalChinese);
                bool chinese = Load() == TraditionalChinese;
                Save(English);
                bool english = Load() == English;
                return firstRun && chinese && english;
            }
            finally
            {
                Environment.SetEnvironmentVariable("NETCHECK_UI_LANGUAGE_FILE", previousFile);
                Environment.SetEnvironmentVariable("NETCHECK_UI_LANGUAGE", previousLanguage);
            }
        }

        private static string PreferencePath()
        {
            string overridePath = Environment.GetEnvironmentVariable("NETCHECK_UI_LANGUAGE_FILE");
            if (!String.IsNullOrWhiteSpace(overridePath)) return overridePath;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Monitor", "language.dat");
        }
    }

    internal static class L
    {
        private static readonly bool traditionalChinese = LanguagePreferenceStore.Load() == LanguagePreferenceStore.TraditionalChinese;

        public static bool TraditionalChinese { get { return traditionalChinese; } }
        public static string HtmlLanguage { get { return traditionalChinese ? "zh-Hant" : "en"; } }
        public static string T(string traditionalChineseText, string englishText) { return traditionalChinese ? traditionalChineseText : englishText; }

        internal static bool IsTraditionalChineseLanguage(string name)
        {
            return LanguagePreferenceStore.Normalize(name) == LanguagePreferenceStore.TraditionalChinese;
        }
    }

    internal sealed class LanguageSelectionForm : Form
    {
        internal string SelectedLanguage { get; private set; }

        internal LanguageSelectionForm()
        {
            Text = "選擇介面語言 / Choose Language";
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(480, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = "選擇介面語言 / Choose interface language", Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 24) };
            var hint = new Label { Text = "此設定之後可以在設定頁面變更。\nYou can change this later in Settings.", AutoSize = false, Location = new Point(31, 70), Size = new Size(420, 48), ForeColor = Color.DimGray };
            var chineseButton = new Button { Text = "繁體中文", Location = new Point(54, 142), Size = new Size(160, 46) };
            var englishButton = new Button { Text = "English", Location = new Point(266, 142), Size = new Size(160, 46) };
            chineseButton.Click += delegate { SelectLanguage(LanguagePreferenceStore.TraditionalChinese); };
            englishButton.Click += delegate { SelectLanguage(LanguagePreferenceStore.English); };
            Controls.AddRange(new Control[] { title, hint, chineseButton, englishButton });
        }

        private void SelectLanguage(string language)
        {
            SelectedLanguage = language;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
