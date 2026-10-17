using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MyTranslator
{
    public sealed class DocumentImportException : Exception
    {
        public DocumentImportException(string message) : base(message) { }
    }

    public sealed class ImportedDocument
    {
        public string FileName { get; set; }
        public string SourcePath { get; set; }
        public string Format { get; set; }
        public string Text { get; set; }
        public string Warning { get; set; }
        public int PageCount { get; set; }
        public int PagesWithoutText { get; set; }
        public string Summary { get { return Format + (PageCount > 0 ? " · " + PageCount + " 页" : "") + " · " + Text.Length.ToString("N0") + " 字符"; } }
    }

    public sealed class DocumentProgress
    {
        public string Message { get; set; }
        public int Completed { get; set; }
        public int Total { get; set; }
    }

    public interface IDocumentImporter
    {
        ImportedDocument Extract(string path, IProgress<DocumentProgress> progress, CancellationToken token);
    }

    public sealed class DocumentImporter : IDocumentImporter
    {
        public const long MaxFileBytes = 50L * 1024 * 1024;
        private const long MaxXmlBytes = 32L * 1024 * 1024;
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        public ImportedDocument Extract(string path, IProgress<DocumentProgress> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(path)) throw new DocumentImportException("请选择要导入的文档。");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".doc") throw new DocumentImportException("暂不支持旧版 .doc。请用 Word 或 WPS 另存为 .docx 后再导入。");
            if (!new[] { ".pdf", ".docx", ".txt", ".md", ".markdown" }.Contains(extension))
                throw new DocumentImportException("不支持这种文档格式。请选择 PDF、Word (.docx)、Markdown 或 TXT。");
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) throw new DocumentImportException("文档不存在或已移动，请重新选择。");
                if (file.Length > MaxFileBytes) throw new DocumentImportException("文档超过 50 MB，请拆分或压缩后导入。");
                Report(progress, "正在本地提取文档文字…", 0, 0);
                var result = new ImportedDocument { FileName = file.Name, SourcePath = file.FullName, Warning = "" };
                if (extension == ".pdf") ExtractPdf(file.FullName, result, progress, token);
                else if (extension == ".docx") ExtractDocx(file.FullName, result, progress, token);
                else
                {
                    result.Format = extension == ".txt" ? "TXT" : "Markdown";
                    result.Text = ReadText(file.FullName, token);
                }
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(result.Text)) throw new DocumentImportException("文档中没有可翻译的文字。若为扫描件或图片，请先进行 OCR。");
                CheckLength(result.Text.Length);
                Report(progress, "文字提取完成，请预览后点击开始翻译。", 1, 1);
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (DocumentImportException) { throw; }
            catch (UnauthorizedAccessException) { throw new DocumentImportException("没有权限读取该文档，请选择可以访问的文件。"); }
            catch (IOException) { throw new DocumentImportException("无法读取文档，请检查文件是否被占用或已经损坏。"); }
            catch (InvalidDataException) { throw new DocumentImportException("文档结构损坏或格式与扩展名不符，请重新导出文档。"); }
            catch (XmlException) { throw new DocumentImportException("Word 文档中的 XML 内容不合法，请用 Word 或 WPS 重新另存为 .docx。"); }
        }

        private static void Report(IProgress<DocumentProgress> progress, string message, int completed, int total)
        {
            if (progress != null) progress.Report(new DocumentProgress { Message = message, Completed = completed, Total = total });
        }

        private static void CheckLength(int length)
        {
            if (length > TranslationText.MaxInputLength)
                throw new DocumentImportException("提取的文字超过 100,000 字符，未截断导入。请按章节拆分文档后重试。");
        }

        private static string ReadText(string path, CancellationToken token)
        {
            try { return ReadTextWithEncoding(path, new UTF8Encoding(false, true), token); }
            catch (DecoderFallbackException)
            {
                try { return ReadTextWithEncoding(path, Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), token); }
                catch (DecoderFallbackException) { throw new DocumentImportException("无法识别文本编码，请先将文档另存为 UTF-8 后导入。"); }
            }
        }

        private static string ReadTextWithEncoding(string path, Encoding encoding, CancellationToken token)
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            using (var reader = new StreamReader(path, encoding, true))
            {
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    CheckLength(output.Length + count);
                    for (int i = 0; i < count; i++)
                        if (Char.IsControl(buffer[i]) && !Char.IsWhiteSpace(buffer[i]))
                            throw new DocumentImportException("文件包含二进制或异常控制字符，请确认是文本文件并另存为 UTF-8。");
                    output.Append(buffer, 0, count);
                }
            }
            return output.ToString();
        }

        private static void ExtractPdf(string path, ImportedDocument result, IProgress<DocumentProgress> progress, CancellationToken token)
        {
            result.Format = "PDF";
            var output = new StringBuilder();
            try
            {
                using (var document = PdfDocument.Open(path))
                {
                    result.PageCount = document.NumberOfPages;
                    if (result.PageCount > 500) throw new DocumentImportException("PDF 超过 500 页，请按章节拆分后导入。");
                    for (int i = 1; i <= document.NumberOfPages; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string text = ContentOrderTextExtractor.GetText(document.GetPage(i));
                        token.ThrowIfCancellationRequested();
                        if (String.IsNullOrWhiteSpace(text)) result.PagesWithoutText++;
                        else
                        {
                            if (output.Length > 0) output.AppendLine().AppendLine();
                            output.Append(text.Trim());
                            CheckLength(output.Length);
                        }
                        Report(progress, "正在提取 PDF · " + i + " / " + document.NumberOfPages + " 页", i, document.NumberOfPages);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (DocumentImportException) { throw; }
            catch (Exception ex)
            {
                if (ex.GetType().Name.IndexOf("Encrypt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ex.GetType().Name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new DocumentImportException("PDF 需要密码，请先解锁并另存为可读取的 PDF 后导入。");
                if (ex is IOException || ex is UnauthorizedAccessException) throw;
                throw new DocumentImportException("无法解析该 PDF，它可能已损坏、加密或缺少可读取的文字层。");
            }
            result.Text = output.ToString();
            result.Warning = result.PagesWithoutText > 0
                ? "有 " + result.PagesWithoutText + " / " + result.PageCount + " 页未提取到文字，可能是图片或空白页。若包含正文，请先 OCR 后重试，避免漏译。"
                : "文字已按页提取；多栏顺序、公式和表格请对照原文检查。";
        }

        private static XDocument ReadXml(ZipArchiveEntry entry)
        {
            if (entry.Length > MaxXmlBytes) throw new DocumentImportException("Word 文档中的文字部分过大，请按章节拆分后导入。");
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes };
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, options)) return XDocument.Load(reader);
        }

        private static void ExtractDocx(string path, ImportedDocument result, IProgress<DocumentProgress> progress, CancellationToken token)
        {
            result.Format = "Word (.docx)";
            var output = new StringBuilder();
            using (var file = File.OpenRead(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("word/document.xml");
                if (entry == null) throw new DocumentImportException("文件不是有效的 Word (.docx) 文档。加密文档请先解锁后另存。");
                var document = ReadXml(entry);
                var body = document.Root == null ? null : document.Root.Element(W + "body");
                if (body == null) throw new DocumentImportException("Word 文档缺少正文内容。");
                AppendBlocks(body.Elements(), output, token);
                Report(progress, "正在提取 Word 页眉、页脚和注释文字…", 1, 2);

                // Only follow internal parts actually referenced by the main document.
                var relationships = archive.GetEntry("word/_rels/document.xml.rels");
                if (relationships != null)
                {
                    var ids = new HashSet<string>(body.Descendants().Where(e => e.Name == W + "headerReference" || e.Name == W + "footerReference")
                        .Select(e => (string)e.Attribute(R + "id")).Where(id => id != null));
                    foreach (var relation in ReadXml(relationships).Root.Elements())
                    {
                        token.ThrowIfCancellationRequested();
                        if (!ids.Contains((string)relation.Attribute("Id")) || (string)relation.Attribute("TargetMode") == "External") continue;
                        string target = (string)relation.Attribute("Target");
                        Uri resolved;
                        if (String.IsNullOrEmpty(target) || !Uri.TryCreate(new Uri("https://docx.local/word/document.xml"), target, out resolved) || resolved.Host != "docx.local") continue;
                        string partName = Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
                        if (!partName.StartsWith("word/", StringComparison.Ordinal)) continue;
                        var part = archive.GetEntry(partName);
                        if (part != null)
                        {
                            output.AppendLine().AppendLine("[页眉 / 页脚]");
                            AppendBlocks(ReadXml(part).Root.Elements(), output, token);
                        }
                    }
                }
                AppendNotes(archive, body, "footnote", "脚注", output, token);
                AppendNotes(archive, body, "endnote", "尾注", output, token);
                result.Warning = "已提取正文、表格及引用的页眉页脚和脚注尾注；不还原原排版。";
                if (archive.Entries.Any(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)))
                    result.Warning += " 文档含图片，图片中的文字未识别。";
                if (body.Descendants(M + "oMath").Any()) result.Warning += " 公式仅提取文字片段，请对照原文检查。";
            }
            result.Text = output.ToString().Trim();
            CheckLength(result.Text.Length);
        }

        private static void AppendNotes(ZipArchive archive, XElement body, string kind, string title, StringBuilder output, CancellationToken token)
        {
            var ids = new HashSet<string>(body.Descendants(W + (kind + "Reference")).Select(e => (string)e.Attribute(W + "id")));
            var entry = archive.GetEntry("word/" + kind + "s.xml");
            if (entry == null || ids.Count == 0) return;
            foreach (var note in ReadXml(entry).Root.Elements(W + kind))
            {
                if (!ids.Contains((string)note.Attribute(W + "id"))) continue;
                output.AppendLine().AppendLine("[" + title + "]");
                AppendBlocks(note.Elements(), output, token);
            }
        }

        private static void AppendBlocks(IEnumerable<XElement> elements, StringBuilder output, CancellationToken token)
        {
            foreach (var element in elements)
            {
                token.ThrowIfCancellationRequested();
                if (element.Name == W + "p")
                {
                    AppendParagraph(element, output);
                    output.AppendLine();
                    foreach (var nested in element.Descendants(W + "p")) { AppendParagraph(nested, output); output.AppendLine(); }
                }
                else if (element.Name == W + "tbl")
                {
                    foreach (var row in element.Elements(W + "tr"))
                    {
                        bool first = true;
                        foreach (var cell in row.Elements(W + "tc"))
                        {
                            if (!first) output.Append('\t');
                            var content = new StringBuilder(); AppendBlocks(cell.Elements(), content, token);
                            output.Append(content.ToString().Trim()); first = false;
                        }
                        output.AppendLine();
                    }
                }
                else if (element.Name != W + "del" && element.Name != W + "moveFrom") AppendBlocks(element.Elements(), output, token);
                CheckLength(output.Length);
            }
        }

        private static void AppendParagraph(XElement paragraph, StringBuilder output)
        {
            foreach (var element in paragraph.Descendants())
            {
                if (element.Ancestors(W + "p").FirstOrDefault() != paragraph ||
                    element.Ancestors().Any(e => e.Name == W + "del" || e.Name == W + "moveFrom")) continue;
                if (element.Name == W + "t" || element.Name == M + "t") output.Append(element.Value);
                else if (element.Name == W + "tab") output.Append('\t');
                else if (element.Name == W + "br" || element.Name == W + "cr") output.AppendLine();
                else if (element.Name == W + "noBreakHyphen") output.Append('\u2011');
                CheckLength(output.Length);
            }
        }
    }

    public static class DocumentExporter
    {
        public static void Save(string path, string text)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (!new[] { ".txt", ".md", ".markdown", ".docx" }.Contains(extension)) throw new ArgumentException("导出仅支持 TXT、Markdown 和 Word (.docx)，请选择对应扩展名。");
            if (extension == ".docx") SaveDocx(path, text);
            else File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        // Creates a new text-only Word document. Never opens or executes the source document.
        private static void SaveDocx(string path, string text)
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var body = new XElement(w + "body");
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var paragraph = new XElement(w + "p");
                bool first = true;
                foreach (string part in line.Split('\t')) {
                    if (!first) paragraph.Add(new XElement(w + "r", new XElement(w + "tab")));
                    paragraph.Add(new XElement(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), part)));
                    first = false;
                }
                body.Add(paragraph);
            }
            body.Add(new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", 11906), new XAttribute(w + "h", 16838))));
            var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), new XElement(w + "document", body));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = File.Create(temporary))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    WriteEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                    WriteEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                    var entry = archive.CreateEntry("word/document.xml");
                    using (var stream = entry.Open()) document.Save(stream);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(content);
        }
    }
}
