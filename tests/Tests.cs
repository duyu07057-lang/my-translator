using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyTranslator;

public sealed class FakeHandler : HttpMessageHandler
{
    public int Calls;
    public string LastRequestBody;
    public Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Calls++; LastRequestBody = await request.Content.ReadAsStringAsync(); return await Respond(Calls, request, token); }
}

public sealed class ControlledDocumentImporter : IDocumentImporter
{
    public readonly ManualResetEventSlim Started = new ManualResetEventSlim();
    public ImportedDocument Extract(string path, IProgress<DocumentProgress> progress, CancellationToken token)
    {
        if (Path.GetFileName(path) == "cancel-import.txt")
        {
            Started.Set(); token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)); token.ThrowIfCancellationRequested();
        }
        return new DocumentImporter().Extract(path, progress, token);
    }
}

public sealed class InlineProgress : IProgress<TranslationProgress>
{
    public List<TranslationProgress> Values = new List<TranslationProgress>();
    public void Report(TranslationProgress value) { Values.Add(value); }
}

public static class Tests
{
    static int passed;
    static string artifacts;

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS: " + name);
    }

    static TranslationOptions Options()
    {
        return new TranslationOptions {
            ApiKey = "test-key-never-real", Model = "deepseek-flash", SourceLanguage = "自动检测",
            TargetLanguage = "简体中文", Domain = "电子工程 / FPGA", Style = "严谨学术",
            Glossary = new List<GlossaryEntry> {
                new GlossaryEntry { Source = "FPGA", Target = "现场可编程门阵列", Language = "简体中文", Domain = "电子工程 / FPGA" },
                new GlossaryEntry { Source = "FPGA", Target = "wrong-language", Language = "英语", Domain = "所有领域" },
                new GlossaryEntry { Source = "FPGA", Target = "wrong-domain", Language = "简体中文", Domain = "法律" },
                new GlossaryEntry { Source = "absent", Target = "不匹配", Language = "简体中文", Domain = "所有领域" }
            }
        };
    }

    static HttpResponseMessage Response(string translation, string finish)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(Json.Serialize(new {
                choices = new[] { new { finish_reason = finish, message = new {
                    content = Json.Serialize(new { detected_language = "英语", translation = translation }) } } },
                usage = new { total_tokens = 123 }
            }), Encoding.UTF8, "application/json")
        };
    }

    static async Task ExpectTranslationError(Func<Task> action, string contains, string name)
    {
        try { await action(); }
        catch (TranslationException ex) { Check(ex.Message.Contains(contains), name); return; }
        throw new Exception("FAIL: expected " + name);
    }

    static async Task CoreTests()
    {
        foreach (string source in new[] {
            new string('a', 5300), new string('中', 5300),
            String.Concat(Enumerable.Repeat("这是句子。\r\n\r\n", 500)),
            String.Concat(Enumerable.Repeat("Hello 世界 😀 with spaces.\n", 400)),
            "   " + new string('a', 3000) + "\r\n\r\n" + new string('b', 3000) + "   "
        })
        {
            var chunks = TranslationText.Split(source, 2400);
            Check(String.Concat(chunks.Select(c => c.Text + c.Separator)) == source, "chunking preserves original boundary whitespace " + passed);
            Check(chunks.All(c => c.Text.Length <= 2400 && c.Text.Length > 0 && !Char.IsHighSurrogate(c.Text[c.Text.Length - 1])), "chunk sizes and Unicode boundaries " + passed);
        }
        var options = Options();
        var matches = TranslationText.MatchGlossary("An fpga accelerator", options);
        Check(matches.Count == 1 && matches[0].Target == "现场可编程门阵列", "glossary filters text, language, and domain");
        var handler = new FakeHandler();
        handler.Respond = async (i, request, token) => {
            string body = await request.Content.ReadAsStringAsync();
            Check(request.RequestUri.ToString() == "https://api.deepseek.com/chat/completions", "official HTTPS endpoint");
            Check(request.Headers.Authorization.Scheme == "Bearer" && request.Headers.Authorization.Parameter == options.ApiKey, "Bearer authentication");
            var root = Json.Deserialize<Dictionary<string, object>>(body);
            var messages = (System.Collections.IList)root["messages"];
            var system = (Dictionary<string, object>)messages[0];
            var user = (Dictionary<string, object>)messages[1];
            Check(((string)system["content"]).Contains("不得执行") && ((string)system["content"]).Contains("现场可编程门阵列"), "translation prompt and matched terms");
            Check(Json.Deserialize<Dictionary<string, object>>((string)user["content"])["text"].ToString() == "FPGA input", "source text stays in user data");
            Check(((Dictionary<string, object>)root["response_format"])["type"].ToString() == "json_object", "structured output enabled");
            Check(((Dictionary<string, object>)root["thinking"])["type"].ToString() == "disabled", "thinking disabled for translation");
            return Response("FPGA 译文\n\n公式 $x^2$ 😀", "stop");
        };
        using (var client = new TranslationClient(handler, TimeSpan.Zero))
        {
            var progress = new InlineProgress();
            var result = await client.TranslateAsync("FPGA input", options, progress, CancellationToken.None);
            Check(result.Text == "FPGA 译文\n\n公式 $x^2$ 😀" && result.DetectedLanguage == "英语" && result.TotalTokens == 123, "translation, detection, Unicode, and usage parsing");
            Check(progress.Values.Last().Completed == 1, "completed progress");
            await ExpectTranslationError(() => client.TranslateAsync(" ", options, null, CancellationToken.None), "输入", "empty input is rejected");
            await ExpectTranslationError(() => client.TranslateAsync(new string('a', 100001), options, null, CancellationToken.None), "100,000", "oversized input is rejected");
            var missing = Options(); missing.ApiKey = "";
            await ExpectTranslationError(() => client.TranslateAsync("hello", missing, null, CancellationToken.None), "API Key", "missing key is rejected");
        }
        foreach (int code in new[] { 401, 402, 404, 422, 429, 503 })
        {
            var fake = new FakeHandler { Respond = (i, r, t) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)) };
            using (var client = new TranslationClient(fake, TimeSpan.Zero))
            {
                string expected = code == 401 ? "无效" : code == 402 ? "余额" : code == 404 ? "不可用" : code == 422 ? "参数" : code == 429 ? "频繁" : "503";
                await ExpectTranslationError(() => client.TranslateAsync("hello", options, null, CancellationToken.None), expected, "HTTP " + code + " gives actionable message");
                Check(fake.Calls == (code >= 500 || code == 429 ? 3 : 1), "bounded retry count for " + code);
            }
        }
        var retryHandler = new FakeHandler { Respond = (i, r, t) => Task.FromResult(i == 1 ? new HttpResponseMessage((HttpStatusCode)429) : Response("你好", "stop")) };
        using (var client = new TranslationClient(retryHandler, TimeSpan.Zero))
        {
            var result = await client.TranslateAsync("hello", options, null, CancellationToken.None);
            Check(result.Text == "你好" && retryHandler.Calls == 2, "rate limit retries then succeeds");
        }
        var truncated = new FakeHandler { Respond = (i, r, t) => Task.FromResult(Response("partial", "length")) };
        using (var client = new TranslationClient(truncated, TimeSpan.Zero))
            await ExpectTranslationError(() => client.TranslateAsync("hello", options, null, CancellationToken.None), "不完整", "truncated translation is never accepted as complete");
        var malformed = new FakeHandler { Respond = (i, r, t) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid-json") }) };
        using (var client = new TranslationClient(malformed, TimeSpan.Zero))
            await ExpectTranslationError(() => client.TranslateAsync("hello", options, null, CancellationToken.None), "解析", "malformed response is handled");
        var empty = new FakeHandler { Respond = (i, r, t) => Task.FromResult(Response("", "stop")) };
        using (var client = new TranslationClient(empty, TimeSpan.Zero))
            await ExpectTranslationError(() => client.TranslateAsync("hello", options, null, CancellationToken.None), "不完整", "empty translation is handled");
        var pending = new FakeHandler { Respond = async (i, r, t) => { await Task.Delay(30000, t); return Response("late", "stop"); } };
        using (var client = new TranslationClient(pending, TimeSpan.Zero))
        using (var token = new CancellationTokenSource())
        {
            token.CancelAfter(30);
            try { await client.TranslateAsync("hello", options, null, token.Token); throw new Exception("Cancellation not observed"); }
            catch (OperationCanceledException) { Check(true, "in-flight request cancels immediately"); }
        }
        var multiHandler = new FakeHandler();
        multiHandler.Respond = async (i, request, token) => {
            var root = Json.Deserialize<Dictionary<string, object>>(await request.Content.ReadAsStringAsync());
            var messages = (System.Collections.IList)root["messages"];
            var user = (Dictionary<string, object>)messages[1];
            string source = (string)Json.Deserialize<Dictionary<string, object>>((string)user["content"])["text"];
            return Response(source, "stop");
        };
        using (var client = new TranslationClient(multiHandler, TimeSpan.Zero))
        {
            string original = String.Concat(Enumerable.Repeat("A paragraph with Unicode 测试.\r\n\r\n", 260));
            var result = await client.TranslateAsync(original, options, null, CancellationToken.None);
            Check(result.Text == original && result.CompletedChunks > 1 && result.TotalTokens == result.CompletedChunks * 123, "multi-chunk assembly preserves boundaries and sums usage");
        }
        var partial = new InlineProgress();
        var failingLater = new FakeHandler { Respond = (i, r, t) => Task.FromResult(i == 1 ? Response("已完成的段落", "stop") : new HttpResponseMessage(HttpStatusCode.Unauthorized)) };
        using (var client = new TranslationClient(failingLater, TimeSpan.Zero))
        {
            await ExpectTranslationError(() => client.TranslateAsync(new string('a', 5000), options, partial, CancellationToken.None), "无效", "later chunk failure is reported");
            Check(partial.Values.Last().Text.Contains("已完成的段落"), "completed output survives later failure");
        }

        string profile = Path.Combine(artifacts, "core-profile");
        var store = new SettingsStore(profile);
        var settings = new AppSettings { ApiKey = "test-secret-do-not-use", RememberKey = true };
        store.Save(settings);
        string saved = File.ReadAllText(Path.Combine(profile, "settings.json"));
        Check(!saved.Contains(settings.ApiKey) && store.Load().ApiKey == settings.ApiKey, "DPAPI round trip and no plaintext key on disk");
        settings.RememberKey = false; store.Save(settings);
        var savedSettings = Json.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(profile, "settings.json")));
        Check(String.IsNullOrEmpty(savedSettings.ProtectedApiKey), "disabling remember removes encrypted key");
        var records = new List<HistoryEntry> { new HistoryEntry { Source = "source", Translation = "译文" } };
        store.SaveHistory(records, false);
        Check(!File.Exists(Path.Combine(profile, "history.json")), "history is not persisted by default");
        store.SaveHistory(records, true);
        Check(store.LoadHistory(true).Count == 1, "history round trip");
        store.SaveHistory(records, false);
        Check(!File.Exists(Path.Combine(profile, "history.json")), "disabling history removes disk history");
        File.WriteAllText(Path.Combine(profile, "settings.json"), "invalid");
        var loaded = store.Load();
        Check(loaded != null && !String.IsNullOrEmpty(store.Warning) && File.ReadAllText(Path.Combine(profile, "settings.json")) == "invalid", "corrupt settings fall back without immediate overwrite");
    }

    static void Click(MainWindow main, string name)
    { main.Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }

    static async Task Capture(MainWindow main, string filename)
    {
        await main.View.Dispatcher.InvokeAsync(delegate { main.View.UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)main.View.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(artifacts, filename))) encoder.Save(stream);
    }

    static async Task UiTests(MainWindow main, FakeHandler handler, ControlledDocumentImporter importer)
    {
        await Capture(main, "01-home.png");
        Check(main.Control<FrameworkElement>("OutputPlaceholder").Visibility == Visibility.Visible, "empty output placeholder visible");
        Click(main, "TranslateButton");
        Check(main.Control<TextBlock>("StatusText").Text.Contains("输入"), "UI prevents empty translation");
        main.Control<TextBox>("SourceText").Text = "Hello";
        await main.TranslateAsync();
        Check(main.Control<FrameworkElement>("SettingsPage").Visibility == Visibility.Visible, "UI routes missing key to settings");
        await main.ImportDocumentAsync(Path.Combine(DocumentTests.FixturePath, "sample.pdf"));
        Check(handler.Calls == 0 && main.Control<TextBox>("SourceText").Text.Contains("中文提取测试"), "document extraction works locally without an API key");
        main.ShowPage("Settings");
        main.Control<PasswordBox>("ApiKeyInput").Password = "test-ui-key";
        main.Control<CheckBox>("RememberKey").IsChecked = true;
        Click(main, "SaveSettingsButton");
        Check(main.Control<Button>("ApiBadge").Content.ToString().Contains("已配置"), "UI saves configuration");
        await Capture(main, "02-settings.png");
        main.ShowPage("Glossary"); Click(main, "SeedTermsButton");
        Check(main.Control<DataGrid>("TermsGrid").Items.Count == 4, "UI adds example terminology");
        main.Control<TextBox>("TermSource").Text = "inference";
        main.Control<TextBox>("TermTarget").Text = "推理";
        Click(main, "AddTermButton");
        Check(main.Control<DataGrid>("TermsGrid").Items.Count == 5, "UI adds custom terminology");
        await Capture(main, "03-glossary.png");
        main.ShowPage("Translate"); Click(main, "ExampleButton");
        var operation = main.TranslateAsync();
        Check(!main.Control<Button>("TranslateButton").IsEnabled && main.Control<TextBox>("SourceText").IsReadOnly, "UI locks source and settings during request");
        await operation;
        Check(main.Control<TextBox>("OutputText").Text.Contains("现场可编程门阵列"), "UI displays API translation");
        Check(main.Control<TextBlock>("DetectedText").Text.Contains("英语"), "UI displays detected language");
        Check(main.Control<Button>("TranslateButton").IsEnabled && main.Control<Button>("CopyButton").IsEnabled, "UI restores controls after request");
        await Capture(main, "04-translated.png");
        main.ShowPage("History");
        Check(main.Control<ListBox>("HistoryList").Items.Count == 1, "UI records completed translation");
        await Capture(main, "05-history.png");
        main.Control<ListBox>("HistoryList").SelectedIndex = 0;
        Click(main, "RestoreHistoryButton");
        Check(main.Control<FrameworkElement>("TranslatePage").Visibility == Visibility.Visible, "UI restores history to translator");
        Click(main, "SwapButton");
        Check(main.Control<TextBox>("SourceText").Text.Contains("现场可编程门阵列") && main.Control<ComboBox>("TargetBox").SelectedItem.ToString() == "英语", "UI swaps text and language");
        main.Control<TextBox>("SourceText").Text = "cancel-test";
        var cancel = main.TranslateAsync();
        await Task.Delay(30);
        Click(main, "CancelButton"); await cancel;
        Check(main.Control<Button>("TranslateButton").IsEnabled && main.Control<TextBlock>("StatusText").Text.Contains("已停止"), "UI cancellation leaves usable controls");
        Click(main, "ClearButton");
        Check(main.Control<TextBox>("SourceText").Text == "" && main.Control<TextBox>("OutputText").Text == "", "UI clear resets editors");
        // Render at the minimum supported size to catch clipped controls.
        main.View.Width = 1080; main.View.Height = 740;
        await Capture(main, "06-minimum-size.png");
        Check(main.Control<Button>("TranslateButton").ActualWidth > 100, "UI remains usable at minimum size");

        Click(main, "DocumentNav");
        main.Control<ComboBox>("SourceBox").SelectedItem = "自动检测";
        main.Control<ComboBox>("TargetBox").SelectedItem = "简体中文";
        Check(main.Control<Border>("DocumentPanel").Visibility == Visibility.Visible && main.Control<TextBlock>("PageTitle").Text == "文档翻译", "document navigation opens the import workspace");
        await Capture(main, "07-document-empty-minimum.png");
        main.View.Width = 1280; main.View.Height = 840;
        int callsBefore = handler.Calls;
        await main.ImportDocumentAsync(Path.Combine(DocumentTests.FixturePath, "sample.pdf"));
        Check(handler.Calls == callsBefore, "document preview never sends an API request");
        Check(main.Control<TextBox>("SourceText").Text.Contains("中文提取测试") && main.Control<TextBox>("SourceText").Text.Contains("Second page"), "UI displays complete extracted PDF text");
        Check(main.Control<TextBlock>("DocumentFileName").Text == "sample.pdf" && main.Control<TextBlock>("DocumentSummary").Text.Contains("2 页"), "UI shows document identity and page count");
        Check(main.Control<TextBox>("OutputText").Text == "" && !main.Control<TextBox>("SourceText").IsReadOnly, "import clears stale output and allows source review");
        await Capture(main, "08-document-preview.png");
        await main.TranslateAsync();
        Check(handler.Calls == callsBefore + 1 && main.Control<TextBox>("OutputText").Text.Contains("现场可编程门阵列"), "imported document translates through the selected API workflow");
        Check(handler.LastRequestBody.Contains("Second page") && !handler.LastRequestBody.Contains(DocumentTests.FixturePath.Replace("\\", "\\\\")), "translation submits extracted text without the document path");
        await Capture(main, "09-document-translated.png");
        string priorSource = main.Control<TextBox>("SourceText").Text, priorOutput = main.Control<TextBox>("OutputText").Text;
        await main.ImportDocumentAsync(Path.Combine(DocumentTests.FixturePath, "image-only.pdf"));
        Check(main.Control<TextBlock>("StatusText").Text.Contains("OCR") && main.Control<TextBox>("SourceText").Text == priorSource && main.Control<TextBox>("OutputText").Text == priorOutput, "failed document import preserves previous editors");
        var pendingImport = main.ImportDocumentAsync("cancel-import.txt");
        bool started = await Task.Run(() => importer.Started.Wait(TimeSpan.FromSeconds(5)));
        Check(started && !main.Control<Button>("SelectDocumentButton").IsEnabled && main.Control<Button>("CancelButton").Content.ToString() == "停止提取", "UI remains responsive and exposes extraction cancellation");
        Click(main, "CancelButton"); await pendingImport;
        Check(main.Control<TextBlock>("StatusText").Text.Contains("取消") && main.Control<TextBox>("SourceText").Text == priorSource && main.Control<TextBox>("OutputText").Text == priorOutput, "cancelled document import preserves previous editors");
        Check(main.Control<Button>("SelectDocumentButton").IsEnabled && main.Control<Button>("TranslateButton").IsEnabled, "cancelled import restores usable controls");
        await main.ImportDocumentAsync(Path.Combine(DocumentTests.FixturePath, "mixed-pages.pdf"));
        Check(main.Control<TextBlock>("DocumentWarning").Text.Contains("1 / 3"), "UI persistently warns about PDF pages without text");
        await main.ImportDocumentAsync(Path.Combine(DocumentTests.FixturePath, "sample.docx"));
        Check(main.Control<TextBox>("SourceText").Text.Contains("throughput\t吞吐量") && main.Control<TextBox>("SourceText").Text.Contains("Synthetic header"), "UI previews Word tables and headers");
        main.Control<TextBox>("SourceText").AppendText("\nEdited source");
        Check(main.Control<TextBlock>("DocumentSummary").Text.Contains("已编辑"), "document metadata marks reviewed source edits");
        await main.TranslateAsync();
        DocumentExporter.Save(Path.Combine(DocumentTests.WorkPath, "ui-export.docx"), main.Control<TextBox>("OutputText").Text);
        Check(new DocumentImporter().Extract(Path.Combine(DocumentTests.WorkPath, "ui-export.docx"), null, CancellationToken.None).Text.Contains("现场可编程门阵列"), "document workflow exports a readable Word translation");
        main.View.Width = 1080; main.View.Height = 740;
        await Capture(main, "10-document-minimum-size.png");
        Check(main.Control<TextBox>("SourceText").ActualHeight > 100 && main.Control<Button>("TranslateButton").ActualWidth > 100, "document workspace fits the minimum supported window");
        foreach (string fileName in new[] { "sample.txt", "sample.markdown" }) {
            int previousCalls = handler.Calls;
            await main.ImportDocumentAsync(Path.Combine(DocumentTests.WorkPath, fileName));
            Check(handler.Calls == previousCalls, "local preview of " + fileName + " never calls API");
            await main.TranslateAsync();
            Check(handler.Calls == previousCalls + 1 && main.Control<TextBlock>("OutputState").Text == "已完成", "end-to-end import and translation of " + fileName);
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        artifacts = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(artifacts);
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        try
        {
            CoreTests().GetAwaiter().GetResult();
            DocumentTests.Run(artifacts, Check);
            var app = new Application();
            var handler = new FakeHandler { Respond = async (i, request, token) => {
                string body = await request.Content.ReadAsStringAsync();
                if (body.Contains("cancel-test")) await Task.Delay(30000, token);
                else await Task.Delay(50, token);
                return Response("现场可编程门阵列（FPGA）能够在边缘端实现低延迟推理。非均匀分段线性近似在保持精度的同时，降低了非线性激活函数的硬件开销。\n\n该加速器采用定点运算和流水线架构，在严格的功耗约束下提高吞吐量。", "stop");
            } };
            int exit = 0;
            using (var client = new TranslationClient(handler, TimeSpan.Zero))
            {
                string uiProfile = Path.Combine(artifacts, "ui-profile-" + Guid.NewGuid().ToString("N"));
                var importer = new ControlledDocumentImporter();
                var main = new MainWindow(new SettingsStore(uiProfile), client, importer);
                main.View.ShowInTaskbar = false;
                main.View.Loaded += async delegate {
                    try { await UiTests(main, handler, importer); }
                    catch (Exception ex) { Console.WriteLine(ex); exit = 1; }
                    finally { main.View.Close(); }
                };
                app.Run(main.View);
            }
            Console.WriteLine("\n" + passed + " checks passed. Network calls: mocked only.");
            File.WriteAllText(Path.Combine(artifacts, "test-result.txt"), passed + " checks passed; exit=" + exit + "; API calls mocked.\r\n" + DateTimeOffset.Now.ToString("o"));
            return exit;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}
