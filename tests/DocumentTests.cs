using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using MyTranslator;

public static class DocumentTests
{
    private static Action<bool, string> check;
    public static string FixturePath { get; private set; }
    public static string WorkPath { get; private set; }

    private static void ExpectError(Action action, string expected, string name)
    {
        try { action(); }
        catch (DocumentImportException ex) { check(ex.Message.Contains(expected), name); return; }
        throw new Exception("Expected document error: " + name);
    }

    private static void Entry(ZipArchive archive, string name, string content)
    {
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(content);
    }

    public static void Run(string artifacts, Action<bool, string> assert)
    {
        check = assert;
        WorkPath = Path.Combine(artifacts, "documents"); Directory.CreateDirectory(WorkPath);
        FixturePath = Path.GetFullPath(Path.Combine(artifacts, "..", "fixtures"));
        var importer = new DocumentImporter();
        string source = "FPGA improves throughput.\r\n\r\n中文和 Unicode 😀。";
        string utf8 = Path.Combine(WorkPath, "sample.txt");
        File.WriteAllText(utf8, source, new UTF8Encoding(false));
        var text = importer.Extract(utf8, null, CancellationToken.None);
        check(text.Text == source && text.Format == "TXT", "TXT import preserves Unicode and paragraphs");
        check(text.FileName == "sample.txt" && text.SourcePath == Path.GetFullPath(utf8), "import retains original identity");
        string utf16 = Path.Combine(WorkPath, "utf16.txt");
        File.WriteAllText(utf16, source, Encoding.Unicode);
        check(importer.Extract(utf16, null, CancellationToken.None).Text == source, "UTF-16 BOM text import");
        string gb = Path.Combine(WorkPath, "gb18030.txt");
        File.WriteAllText(gb, "定点运算和吞吐量。", Encoding.GetEncoding("GB18030"));
        check(importer.Extract(gb, null, CancellationToken.None).Text == "定点运算和吞吐量。", "legacy Chinese text encoding import");
        string md = Path.Combine(WorkPath, "sample.markdown");
        string markdown = "# Heading\n\n**bold** and $x^2$\n\n```verilog\nassign y = x;\n```";
        File.WriteAllText(md, markdown);
        check(importer.Extract(md, null, CancellationToken.None).Text == markdown, "Markdown import preserves markup and code");
        string empty = Path.Combine(WorkPath, "empty.txt"); File.WriteAllText(empty, " \r\n");
        ExpectError(() => importer.Extract(empty, null, CancellationToken.None), "没有", "empty document is rejected");
        string binary = Path.Combine(WorkPath, "binary.txt"); File.WriteAllBytes(binary, new byte[] { 0, 1, 2, 3 });
        ExpectError(() => importer.Extract(binary, null, CancellationToken.None), "二进制", "binary data with text extension is rejected");
        string oversize = Path.Combine(WorkPath, "oversize.txt"); File.WriteAllText(oversize, new string('a', 100001));
        ExpectError(() => importer.Extract(oversize, null, CancellationToken.None), "未截断", "oversized extracted text never silently truncates");
        string big = Path.Combine(WorkPath, "large.pdf");
        using (var stream = File.Create(big)) stream.SetLength(DocumentImporter.MaxFileBytes + 1);
        ExpectError(() => importer.Extract(big, null, CancellationToken.None), "50 MB", "document file size is bounded");
        File.Delete(big);
        ExpectError(() => importer.Extract(Path.Combine(WorkPath, "missing.txt"), null, CancellationToken.None), "不存在", "missing file gives a useful message");
        ExpectError(() => importer.Extract("legacy.doc", null, CancellationToken.None), ".docx", "legacy Word gets conversion guidance");
        ExpectError(() => importer.Extract("image.png", null, CancellationToken.None), "不支持", "unsupported extension is rejected");
        using (var token = new CancellationTokenSource())
        {
            token.Cancel();
            try { importer.Extract(utf8, null, token.Token); throw new Exception("Import ignored cancellation"); }
            catch (OperationCanceledException) { check(true, "document import honors cancellation"); }
        }
        var pdf = importer.Extract(Path.Combine(FixturePath, "sample.pdf"), null, CancellationToken.None);
        check(pdf.PageCount == 2 && pdf.PagesWithoutText == 0, "real two-page PDF import");
        check(pdf.Text.Contains("FPGA document translation") && pdf.Text.Contains("Second page"), "PDF extracts content from all pages");
        check(pdf.Text.Contains("中文提取测试") && pdf.Text.Contains("激活函数"), "PDF extracts Chinese text layer");
        ExpectError(() => importer.Extract(Path.Combine(FixturePath, "image-only.pdf"), null, CancellationToken.None), "OCR", "image-only PDF gives OCR guidance");
        var mixed = importer.Extract(Path.Combine(FixturePath, "mixed-pages.pdf"), null, CancellationToken.None);
        check(mixed.PageCount == 3 && mixed.PagesWithoutText == 1 && mixed.Warning.Contains("1 / 3"), "mixed PDF reports pages missing text");
        ExpectError(() => importer.Extract(Path.Combine(FixturePath, "password.pdf"), null, CancellationToken.None), "密码", "encrypted PDF gives unlock guidance");
        string brokenPdf = Path.Combine(WorkPath, "broken.pdf"); File.WriteAllText(brokenPdf, "not a pdf");
        ExpectError(() => importer.Extract(brokenPdf, null, CancellationToken.None), "PDF", "corrupt PDF gives a useful message");
        var docx = importer.Extract(Path.Combine(FixturePath, "sample.docx"), null, CancellationToken.None);
        check(docx.Text.Contains("FPGA document translation") && docx.Text.Contains("中文提取测试"), "Word imports English and Chinese paragraphs");
        check(docx.Text.Contains("throughput\t吞吐量"), "Word extracts table rows and cells");
        check(docx.Text.Contains("Synthetic header") && docx.Text.Contains("Synthetic footer"), "Word extracts referenced headers and footers");
        string custom = Path.Combine(WorkPath, "notes.docx");
        using (var stream = File.Create(custom))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Entry(archive, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Current text</w:t><w:tab/><w:t>tabbed</w:t><w:br/><w:t>line</w:t></w:r><w:del><w:r><w:delText>Removed text</w:delText></w:r></w:del><w:r><w:footnoteReference w:id=\"1\"/><w:endnoteReference w:id=\"2\"/></w:r></w:p></w:body></w:document>");
            Entry(archive, "word/footnotes.xml", "<w:footnotes xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:footnote w:id=\"1\"><w:p><w:r><w:t>Footnote text</w:t></w:r></w:p></w:footnote><w:footnote w:id=\"99\"><w:p><w:r><w:t>Unreferenced text</w:t></w:r></w:p></w:footnote></w:footnotes>");
            Entry(archive, "word/endnotes.xml", "<w:endnotes xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:endnote w:id=\"2\"><w:p><w:r><w:t>Endnote text</w:t></w:r></w:p></w:endnote></w:endnotes>");
        }
        var notes = importer.Extract(custom, null, CancellationToken.None);
        check(notes.Text.Contains("Footnote text") && notes.Text.Contains("Endnote text"), "Word extracts referenced footnotes and endnotes");
        check(!notes.Text.Contains("Unreferenced") && !notes.Text.Contains("Removed text"), "Word omits unreferenced notes and deleted revisions");
        check(notes.Text.Contains("Current text\ttabbed") && notes.Text.Contains("\nline"), "Word preserves tabs and line breaks");
        string invalidXml = Path.Combine(WorkPath, "invalid.docx");
        using (var stream = File.Create(invalidXml))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            Entry(archive, "word/document.xml", "<!DOCTYPE document [<!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">]><document>&xxe;</document>");
        ExpectError(() => importer.Extract(invalidXml, null, CancellationToken.None), "XML", "Word rejects external XML entities");
        string invalidZip = Path.Combine(WorkPath, "invalid-zip.docx"); File.WriteAllText(invalidZip, "not a zip");
        ExpectError(() => importer.Extract(invalidZip, null, CancellationToken.None), "损坏", "corrupt Word archive gives a useful message");
        string exported = Path.Combine(WorkPath, "exported.docx");
        string translated = "译文 <&> 特殊字符 😀\n\n第二段\t表格文字";
        DocumentExporter.Save(exported, translated);
        var roundTrip = importer.Extract(exported, null, CancellationToken.None);
        check(roundTrip.Text.Replace("\r\n", "\n") == translated, "Word export preserves Unicode, XML escapes, blank paragraphs, and tabs");
        using (var stream = File.OpenRead(exported))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            check(archive.GetEntry("[Content_Types].xml") != null && archive.GetEntry("_rels/.rels") != null, "exported Word has required package parts");
        DocumentExporter.Save(exported, "替换后的译文");
        check(importer.Extract(exported, null, CancellationToken.None).Text == "替换后的译文", "Word export safely replaces an existing output");
        DocumentExporter.Save(Path.Combine(WorkPath, "exported.txt"), translated);
        DocumentExporter.Save(Path.Combine(WorkPath, "exported.md"), markdown);
        check(File.ReadAllText(Path.Combine(WorkPath, "exported.txt")) == translated && File.ReadAllText(Path.Combine(WorkPath, "exported.md")) == markdown, "TXT and Markdown export preserve content");
        try { DocumentExporter.Save(Path.Combine(WorkPath, "wrong.pdf"), translated); throw new Exception("Unsupported export format accepted"); }
        catch (ArgumentException) { check(!File.Exists(Path.Combine(WorkPath, "wrong.pdf")), "export never disguises plain text as a PDF"); }
    }
}
