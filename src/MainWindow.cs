using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace MyTranslator
{
    public sealed class MainWindow
    {
        public Window View { get; private set; }
        private readonly SettingsStore store;
        private readonly TranslationClient client;
        private readonly AppSettings settings;
        private readonly List<HistoryEntry> history;
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool closing;
        private bool initializing = true;
        private string detectedLanguage = "";
        private string translatedSource = "";
        private string translatedTarget = "";
        private bool outputComplete;
        private static readonly string[] Languages = {
            "简体中文", "繁體中文", "英语", "日语", "韩语", "法语", "德语", "西班牙语", "俄语", "葡萄牙语", "阿拉伯语"
        };
        private static readonly string[] Domains = {
            "通用翻译", "学术论文", "电子工程 / FPGA", "计算机 / 人工智能", "医学 / 生物", "法律", "金融 / 商务"
        };
        private static readonly string[] Styles = { "准确自然", "严谨学术", "简洁易懂" };

        public MainWindow(SettingsStore store, TranslationClient client)
        {
            this.store = store;
            this.client = client;
            settings = store.Load();
            history = store.LoadHistory(settings.SaveHistory);
            using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("MainWindow.xaml"))
                View = (Window)XamlReader.Load(stream);

            Box("SourceBox").ItemsSource = new[] { "自动检测" }.Concat(Languages).ToArray();
            Box("SourceBox").SelectedIndex = 0;
            Fill("TargetBox", Languages, settings.TargetLanguage);
            Fill("DomainBox", Domains, settings.Domain);
            Fill("StyleBox", Styles, settings.Style);
            Fill("TermLanguage", Languages, settings.TargetLanguage);
            Fill("TermDomain", new[] { "所有领域" }.Concat(Domains).ToArray(), "所有领域");
            Box("ModelBox").ItemsSource = new[] { "deepseek-flash", "deepseek-v4-pro" };
            Box("ModelBox").Text = settings.Model;
            Control<PasswordBox>("ApiKeyInput").Password = settings.ApiKey ?? "";
            Control<CheckBox>("RememberKey").IsChecked = settings.RememberKey;
            Control<CheckBox>("SaveHistoryCheck").IsChecked = settings.SaveHistory;
            Label("DataPath").Text = "数据目录：" + store.DirectoryPath;

            Click("TranslateNav", delegate { ShowPage("Translate"); });
            Click("GlossaryNav", delegate { ShowPage("Glossary"); });
            Click("HistoryNav", delegate { ShowPage("History"); });
            Click("SettingsNav", delegate { ShowPage("Settings"); });
            Click("ApiBadge", delegate { ShowPage("Settings"); });
            Click("TermsHint", delegate { ShowPage("Glossary"); });
            Click("TranslateButton", async delegate { await TranslateAsync(); });
            Click("CancelButton", delegate { if (cancellation != null) { cancellation.Cancel(); Status("正在停止，已完成的译文会保留…", false); } });
            Click("CopyButton", delegate { CopyOutput(); });
            Click("ExportButton", delegate { ExportOutput(); });
            Click("PasteButton", delegate { Paste(); });
            Click("ImportButton", delegate { ImportText(); });
            Click("ClearButton", delegate { Clear(); });
            Click("SwapButton", delegate { Swap(); });
            Click("ExampleButton", delegate {
                Text("SourceText").Text = "Field-programmable gate arrays (FPGAs) enable low-latency inference at the edge. " +
                    "A nonuniform piecewise linear approximation reduces the hardware cost of nonlinear activation functions while maintaining accuracy.\n\n" +
                    "The accelerator uses fixed-point arithmetic and a pipelined architecture to improve throughput under tight power constraints.";
                Box("DomainBox").SelectedItem = "电子工程 / FPGA";
            });
            Click("SaveSettingsButton", delegate { SaveSettings(); });
            Click("GetKeyButton", delegate {
                try { Process.Start(new ProcessStartInfo("https://platform.deepseek.com/api_keys") { UseShellExecute = true }); }
                catch (Exception) { Status("无法打开浏览器，请手动访问 https://platform.deepseek.com/api_keys", true); }
            });
            Click("AddTermButton", delegate { AddTerm(); });
            Click("DeleteTermButton", delegate {
                var entry = Control<DataGrid>("TermsGrid").SelectedItem as GlossaryEntry;
                if (entry == null) { Status("请先选中一条术语。", true); return; }
                settings.Glossary.Remove(entry); RefreshTerms(); Persist("术语已删除");
            });
            Click("SeedTermsButton", delegate {
                PutTerm("FPGA", "现场可编程门阵列", "简体中文", "电子工程 / FPGA");
                PutTerm("activation function", "激活函数", "简体中文", "所有领域");
                PutTerm("throughput", "吞吐量", "简体中文", "电子工程 / FPGA");
                PutTerm("fixed-point", "定点", "简体中文", "电子工程 / FPGA");
                RefreshTerms(); Persist("已加入 4 条电子工程示例术语");
            });
            Click("RestoreHistoryButton", delegate { RestoreHistory(); });
            Control<ListBox>("HistoryList").MouseDoubleClick += delegate { RestoreHistory(); };
            Click("ClearHistoryButton", delegate {
                if (history.Count == 0) { Status("历史记录已经为空。", false); return; }
                if (MessageBox.Show(View, "清空所有翻译历史？此操作不会清除当前编辑区的文本。", "清空历史", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                history.Clear(); RefreshHistory(); PersistHistory("历史记录已清空");
            });

            Text("SourceText").TextChanged += delegate {
                Control<TextBlock>("SourcePlaceholder").Visibility = String.IsNullOrEmpty(Text("SourceText").Text) ? Visibility.Visible : Visibility.Collapsed;
                Label("SourceCount").Text = Text("SourceText").Text.Length.ToString("N0") + " 字符";
                if (!busy && !String.IsNullOrEmpty(translatedSource) && translatedSource != Text("SourceText").Text)
                {
                    Label("OutputState").Text = "待更新";
                    Status("原文已更改，当前译文对应上一次翻译。", false);
                }
            };
            Text("OutputText").TextChanged += delegate {
                bool empty = String.IsNullOrEmpty(Text("OutputText").Text);
                Control<StackPanel>("OutputPlaceholder").Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                Label("OutputCount").Text = Text("OutputText").Text.Length.ToString("N0") + " 字符";
                Control<Button>("CopyButton").IsEnabled = !empty;
                Control<Button>("ExportButton").IsEnabled = !empty;
            };
            foreach (string name in new[] { "TargetBox", "DomainBox", "StyleBox" })
                Box(name).SelectionChanged += delegate { if (!initializing) UpdatePreferences(); };
            View.PreviewKeyDown += async delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                { e.Handled = true; if (!busy) { ShowPage("Translate"); await TranslateAsync(); } }
                else if (e.Key == Key.F1) { e.Handled = true; ShowPage("Settings"); }
                else if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                { e.Handled = true; CopyOutput(); }
            };
            View.Closing += delegate {
                closing = true;
                if (cancellation != null) cancellation.Cancel();
            };
            RefreshTerms(); RefreshHistory(); RefreshBadge();
            Control<Button>("CopyButton").IsEnabled = false;
            Control<Button>("ExportButton").IsEnabled = false;
            initializing = false;
            if (!String.IsNullOrEmpty(store.Warning)) Status(store.Warning, true);
            else Status(String.IsNullOrWhiteSpace(settings.ApiKey) ? "就绪 · 请先在 API 设置中填写密钥" : "就绪 · " + settings.Model + " · 文本仅在点击翻译时发送", false);
        }

        public T Control<T>(string name) where T : FrameworkElement { return (T)View.FindName(name); }
        private TextBox Text(string name) { return Control<TextBox>(name); }
        private TextBlock Label(string name) { return Control<TextBlock>(name); }
        private ComboBox Box(string name) { return Control<ComboBox>(name); }
        private string Selected(string name) { return Convert.ToString(Box(name).SelectedItem); }
        private void Click(string name, RoutedEventHandler handler) { Control<Button>(name).Click += handler; }
        private void Fill(string name, string[] values, string selected)
        {
            Box(name).ItemsSource = values;
            Box(name).SelectedItem = values.Contains(selected) ? selected : values[0];
        }

        public void ShowPage(string page)
        {
            string[] pages = { "Translate", "Glossary", "History", "Settings" };
            string[] names = { "文本翻译", "专业术语", "翻译历史", "API 设置" };
            string[] descriptions = { "自动识别语种，让每一个专业表达准确传达。", "为你的领域建立词汇，让专业表述始终一致。", "回到之前的文字，继续理解与表达。", "连接你的模型，设定自己的翻译工作空间。" };
            for (int i = 0; i < pages.Length; i++)
            {
                bool active = pages[i] == page;
                Control<FrameworkElement>(pages[i] + "Page").Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                Control<Button>(pages[i] + "Nav").Background = new SolidColorBrush(active ? Color.FromRgb(42, 75, 80) : Colors.Transparent);
                if (active) { Label("PageTitle").Text = names[i]; Label("PageSubtitle").Text = descriptions[i]; Label("Breadcrumb").Text = "工作空间  /  " + names[i]; }
            }
        }

        private void Status(string value, bool error)
        {
            Label("StatusText").Text = value;
            Label("StatusText").Foreground = new SolidColorBrush(error ? Color.FromRgb(177, 75, 61) : Color.FromRgb(100, 130, 135));
        }

        private void RefreshBadge()
        {
            Control<Button>("ApiBadge").Content = String.IsNullOrWhiteSpace(settings.ApiKey) ? "●  待配置 API" : "●  API 已配置";
        }

        private bool Persist(string success)
        {
            try { store.Save(settings); Status(success, false); return true; }
            catch (Exception) { Status("设置已在本次会话生效，但无法写入本地文件，请检查目录权限或磁盘空间。", true); return false; }
        }

        private void PersistHistory(string success)
        {
            try { store.SaveHistory(history, settings.SaveHistory); Status(success, false); }
            catch (Exception) { Status("译文已保留在当前会话，但本地历史文件无法更新，请检查目录权限或磁盘空间。", true); }
        }

        private void UpdatePreferences()
        {
            settings.TargetLanguage = Selected("TargetBox"); settings.Domain = Selected("DomainBox"); settings.Style = Selected("StyleBox");
            Persist("翻译偏好已保存");
            if (!busy && !String.IsNullOrEmpty(translatedTarget) && translatedTarget != settings.TargetLanguage)
            {
                Label("OutputState").Text = "待更新";
                Status("目标语言已更改，当前译文对应上一次翻译。", false);
            }
        }

        private void SaveSettings()
        {
            string key = Control<PasswordBox>("ApiKeyInput").Password.Trim();
            string model = Box("ModelBox").Text.Trim();
            if (!Regex.IsMatch(model, "^[A-Za-z0-9_.:/-]{1,100}$")) { Status("请输入有效的模型名称，例如 deepseek-flash。", true); return; }
            if (key.Any(Char.IsWhiteSpace) || key.Any(Char.IsControl)) { Status("API Key 中不能包含空白或控制字符。", true); return; }
            settings.ApiKey = key; settings.Model = model;
            settings.RememberKey = Control<CheckBox>("RememberKey").IsChecked == true;
            settings.SaveHistory = Control<CheckBox>("SaveHistoryCheck").IsChecked == true;
            RefreshBadge(); RefreshHistory();
            if (Persist("设置已保存 · " + (String.IsNullOrEmpty(key) ? "尚未填写 API Key" : "可返回文本翻译开始使用")))
                PersistHistory("设置已保存 · " + (String.IsNullOrEmpty(key) ? "尚未填写 API Key" : "可返回文本翻译开始使用"));
        }

        public async Task TranslateAsync()
        {
            if (busy) return;
            string original = Text("SourceText").Text;
            if (String.IsNullOrWhiteSpace(original)) { Status("请先输入需要翻译的文本。", true); Text("SourceText").Focus(); return; }
            if (String.IsNullOrWhiteSpace(settings.ApiKey)) { ShowPage("Settings"); Status("请填写 API Key 并点击「保存设置」。", true); return; }
            var options = new TranslationOptions {
                ApiKey = settings.ApiKey, Model = settings.Model, SourceLanguage = Selected("SourceBox"),
                TargetLanguage = Selected("TargetBox"), Domain = Selected("DomainBox"), Style = Selected("StyleBox"),
                Glossary = settings.Glossary.Select(g => new GlossaryEntry { Source = g.Source, Target = g.Target, Language = g.Language, Domain = g.Domain }).ToList()
            };
            cancellation = new CancellationTokenSource();
            ToggleBusy(true);
            Text("OutputText").Clear(); detectedLanguage = ""; translatedSource = ""; translatedTarget = "";
            outputComplete = false; Label("OutputState").Text = "生成中";
            Label("DetectedText").Text = "正在识别…";
            Status("正在请求 DeepSeek…", false);
            var timer = Stopwatch.StartNew();
            var progress = new Progress<TranslationProgress>(p => {
                if (closing || !busy) return;
                Text("OutputText").Text = p.Text;
                detectedLanguage = p.DetectedLanguage;
                if (!String.IsNullOrEmpty(detectedLanguage)) Label("DetectedText").Text = "识别：" + detectedLanguage;
                var bar = Control<ProgressBar>("TaskProgress"); bar.Maximum = p.Total; bar.Value = p.Completed;
                Status("翻译中 · 已完成 " + p.Completed + " / " + p.Total + " 段 · 可以停止翻译", false);
            });
            try
            {
                var result = await client.TranslateAsync(original, options, progress, cancellation.Token);
                if (closing) return;
                Text("OutputText").Text = result.Text;
                detectedLanguage = result.DetectedLanguage;
                translatedSource = original; translatedTarget = options.TargetLanguage;
                outputComplete = true; Label("OutputState").Text = "已完成";
                Label("DetectedText").Text = "识别：" + detectedLanguage;
                history.Insert(0, new HistoryEntry { Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Source = original, Translation = result.Text, SourceLanguage = detectedLanguage, TargetLanguage = options.TargetLanguage, Domain = options.Domain });
                if (history.Count > 50) history.RemoveRange(50, history.Count - 50);
                RefreshHistory();
                PersistHistory("翻译完成 · " + timer.Elapsed.TotalSeconds.ToString("F1") + " 秒 · " + result.TotalTokens.ToString("N0") + " tokens · " + result.CompletedChunks + " 段");
            }
            catch (OperationCanceledException) { if (!closing) Status("已停止 · " + (Text("OutputText").Text.Length > 0 ? "当前为部分译文，可复制或导出。" : "尚未完成任何段落。"), false); }
            catch (TranslationException ex) { if (!closing) Status(ex.Message + PartialNotice(), true); }
            catch (Exception) { if (!closing) Status("翻译未完成，请检查网络和配置后重试。" + PartialNotice(), true); }
            finally
            {
                cancellation.Dispose(); cancellation = null;
                if (!closing) {
                    ToggleBusy(false);
                    if (!outputComplete) Label("OutputState").Text = Text("OutputText").Text.Length > 0 ? "部分译文" : "译文";
                    if (String.IsNullOrEmpty(detectedLanguage)) Label("DetectedText").Text = "尚未识别";
                }
            }
        }

        private string PartialNotice() { return Text("OutputText").Text.Length > 0 ? " 当前为部分译文，已完成内容可复制或导出。" : ""; }

        private void ToggleBusy(bool value)
        {
            busy = value;
            foreach (string name in new[] { "TranslateButton", "SourceBox", "TargetBox", "DomainBox", "StyleBox", "PasteButton", "ImportButton", "ClearButton", "SwapButton", "ExampleButton", "SettingsPage", "RestoreHistoryButton", "GlossaryPage" })
                Control<FrameworkElement>(name).IsEnabled = !value;
            Text("SourceText").IsReadOnly = value;
            Control<Button>("CancelButton").Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Control<ProgressBar>("TaskProgress").Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("TranslateButton").Content = value ? "正在翻译…" : "开始翻译    ↵";
        }

        private void CopyOutput()
        {
            if (String.IsNullOrEmpty(Text("OutputText").Text)) { Status("还没有可以复制的译文。", true); return; }
            try { Clipboard.SetText(Text("OutputText").Text); Status((outputComplete ? "译文" : "部分译文（尚未完成）") + "已复制到剪贴板", false); }
            catch (Exception) { Status("剪贴板正在被其他程序使用，请稍后重试。", true); }
        }

        private void ExportOutput()
        {
            if (String.IsNullOrEmpty(Text("OutputText").Text)) return;
            var dialog = new SaveFileDialog { Title = "导出译文", FileName = (outputComplete ? "译文-" : "部分译文-") + DateTime.Now.ToString("yyyyMMdd-HHmm"), Filter = "文本文件 (*.txt)|*.txt|Markdown (*.md)|*.md", DefaultExt = ".txt" };
            if (dialog.ShowDialog(View) != true) return;
            try { File.WriteAllText(dialog.FileName, Text("OutputText").Text, new UTF8Encoding(false)); Status("译文已导出至 " + dialog.FileName, false); }
            catch (Exception) { Status("无法写入所选文件，请检查权限或换一个保存位置。", true); }
        }

        private void Paste()
        {
            if (busy) return;
            try
            {
                if (!Clipboard.ContainsText()) { Status("剪贴板中没有文本。", true); return; }
                string value = Clipboard.GetText();
                if (value.Length > TranslationText.MaxInputLength) { Status("剪贴板文本超过 100,000 字符，请拆分后粘贴。", true); return; }
                Text("SourceText").Text = value;
            }
            catch (Exception) { Status("暂时无法读取剪贴板，请稍后重试。", true); }
        }

        private void ImportText()
        {
            if (busy) return;
            var dialog = new OpenFileDialog { Title = "导入 UTF-8 或带 BOM 的 Unicode 文本", Filter = "文本 / Markdown (*.txt;*.md)|*.txt;*.md" };
            if (dialog.ShowDialog(View) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) { Status("文件过大，请导入不超过 100,000 字符的文本。", true); return; }
                string value;
                using (var reader = new StreamReader(dialog.FileName, new UTF8Encoding(false, true), true)) value = reader.ReadToEnd();
                if (value.Length > TranslationText.MaxInputLength) { Status("文件超过 100,000 字符，请拆分后导入。", true); return; }
                Text("SourceText").Text = value; Status("已导入 " + Path.GetFileName(dialog.FileName), false);
            }
            catch (DecoderFallbackException) { Status("文件不是 UTF-8 文本，请先在记事本中另存为 UTF-8 后导入。", true); }
            catch (Exception) { Status("无法读取文件，请检查文件权限或文件是否被占用。", true); }
        }

        private void Clear()
        {
            if (busy) return;
            Text("SourceText").Clear(); Text("OutputText").Clear();
            detectedLanguage = ""; translatedSource = ""; translatedTarget = "";
            outputComplete = false; Label("OutputState").Text = "译文";
            Label("DetectedText").Text = "翻译时识别"; Status("编辑区已清空", false);
        }

        private void Swap()
        {
            if (busy) return;
            if (String.IsNullOrEmpty(Text("OutputText").Text)) { Status("请先完成一次翻译再交换。", true); return; }
            if (String.IsNullOrEmpty(translatedTarget)) { Status("当前是未完成的部分译文，请完成翻译后再交换。", true); return; }
            string previousTarget = translatedTarget;
            string previousSource = detectedLanguage;
            string output = Text("OutputText").Text;
            Clear();
            Text("SourceText").Text = output;
            Box("SourceBox").SelectedItem = Languages.Contains(previousTarget) ? previousTarget : "自动检测";
            string normalized = previousSource == "中文" || previousSource == "汉语" ? "简体中文" : previousSource;
            if (Languages.Contains(normalized)) Box("TargetBox").SelectedItem = normalized;
            Status("已交换原文与语言，可以开始反向翻译。", false);
        }

        private void AddTerm()
        {
            string source = Text("TermSource").Text.Trim(), target = Text("TermTarget").Text.Trim();
            if (source.Length == 0 || target.Length == 0) { Status("请填写原文术语和指定译法。", true); return; }
            if (settings.Glossary.Count >= 500) { Status("术语表最多保存 500 条，请先删除不需要的条目。", true); return; }
            PutTerm(source, target, Selected("TermLanguage"), Selected("TermDomain"));
            Text("TermSource").Clear(); Text("TermTarget").Clear(); RefreshTerms(); Persist("术语已保存，同名且同语言、同领域的条目会更新译法");
        }

        private void PutTerm(string source, string target, string language, string domain)
        {
            var previous = settings.Glossary.FirstOrDefault(g => String.Equals(g.Source, source, StringComparison.OrdinalIgnoreCase) && g.Language == language && g.Domain == domain);
            if (previous != null) previous.Target = target;
            else if (settings.Glossary.Count < 500) settings.Glossary.Add(new GlossaryEntry { Source = source, Target = target, Language = language, Domain = domain });
        }

        private void RefreshTerms() { Control<DataGrid>("TermsGrid").ItemsSource = settings.Glossary.ToArray(); }
        private void RefreshHistory()
        {
            Control<ListBox>("HistoryList").ItemsSource = history.ToArray();
            Label("HistoryEmpty").Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Label("HistoryNote").Text = settings.SaveHistory ? "已开启本地历史保存，最多保留最近 50 条记录。" : "默认仅保留本次会话的记录。可在 API 设置中开启本地历史保存。";
        }

        private void RestoreHistory()
        {
            if (busy) { Status("请等待当前翻译结束后打开记录。", true); return; }
            var entry = Control<ListBox>("HistoryList").SelectedItem as HistoryEntry;
            if (entry == null) { Status("请先选中一条翻译记录。", true); return; }
            Text("SourceText").Text = entry.Source;
            Text("OutputText").Text = entry.Translation;
            detectedLanguage = entry.SourceLanguage; translatedSource = entry.Source; translatedTarget = entry.TargetLanguage;
            outputComplete = true; Label("OutputState").Text = "已完成";
            Box("SourceBox").SelectedItem = Languages.Contains(entry.SourceLanguage) ? entry.SourceLanguage : "自动检测";
            if (Languages.Contains(entry.TargetLanguage)) Box("TargetBox").SelectedItem = entry.TargetLanguage;
            if (Domains.Contains(entry.Domain)) Box("DomainBox").SelectedItem = entry.Domain;
            Label("DetectedText").Text = "识别：" + detectedLanguage;
            ShowPage("Translate"); Status("已打开 " + entry.Timestamp + " 的翻译记录", false);
        }
    }
}
