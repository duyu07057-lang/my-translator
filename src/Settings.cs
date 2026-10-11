using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MyTranslator
{
    public sealed class AppSettings
    {
        [ScriptIgnore]
        public string ApiKey { get; set; }
        public string ProtectedApiKey { get; set; }
        public bool RememberKey { get; set; }
        public string Model { get; set; }
        public string TargetLanguage { get; set; }
        public string Domain { get; set; }
        public string Style { get; set; }
        public bool SaveHistory { get; set; }
        public List<GlossaryEntry> Glossary { get; set; }

        public AppSettings()
        {
            ApiKey = ""; Model = "deepseek-flash"; TargetLanguage = "简体中文";
            Domain = "通用翻译"; Style = "准确自然"; Glossary = new List<GlossaryEntry>();
        }
    }

    public sealed class HistoryEntry
    {
        public string Timestamp { get; set; }
        public string Source { get; set; }
        public string Translation { get; set; }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public string Domain { get; set; }
        public string Summary { get { return Source == null ? "" : Source.Substring(0, Math.Min(90, Source.Length)).Replace("\r", " ").Replace("\n", " "); } }
        public string Label { get { return Timestamp + "    " + SourceLanguage + " → " + TargetLanguage + "    ·    " + Domain; } }
    }

    public sealed class SettingsStore
    {
        public string DirectoryPath { get; private set; }
        public string Warning { get; private set; }
        private string SettingsPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        private string HistoryPath { get { return Path.Combine(DirectoryPath, "history.json"); } }

        public SettingsStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyTranslator")) { }
        public SettingsStore(string directory) { DirectoryPath = directory; }

        public AppSettings Load()
        {
            var settings = new AppSettings();
            if (File.Exists(SettingsPath))
            {
                try
                {
                    settings = Json.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8)) ?? new AppSettings();
                    if (settings.RememberKey && !String.IsNullOrEmpty(settings.ProtectedApiKey))
                    {
                        try { settings.ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedApiKey), null, DataProtectionScope.CurrentUser)); }
                        catch (Exception) { Warning = "已保存的密钥无法解密，请重新填写 API Key。"; }
                    }
                }
                catch (Exception) { Warning = "配置文件无法读取，已使用默认设置；原文件未改动。"; }
            }
            if (settings.Glossary == null) settings.Glossary = new List<GlossaryEntry>();
            if (String.IsNullOrWhiteSpace(settings.ApiKey)) settings.ApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";
            return settings;
        }

        public void Save(AppSettings settings)
        {
            settings.ProtectedApiKey = settings.RememberKey && !String.IsNullOrWhiteSpace(settings.ApiKey)
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.ApiKey), null, DataProtectionScope.CurrentUser)) : null;
            AtomicWrite(SettingsPath, Json.Serialize(settings));
        }

        public List<HistoryEntry> LoadHistory(bool enabled)
        {
            if (!enabled || !File.Exists(HistoryPath)) return new List<HistoryEntry>();
            try { return Json.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryPath, Encoding.UTF8)) ?? new List<HistoryEntry>(); }
            catch (Exception) { Warning = "历史记录无法读取，原文件未改动。"; return new List<HistoryEntry>(); }
        }

        public void SaveHistory(List<HistoryEntry> history, bool enabled)
        {
            if (enabled) AtomicWrite(HistoryPath, Json.Serialize(history));
            else if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
        }

        private void AtomicWrite(string path, string content)
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
