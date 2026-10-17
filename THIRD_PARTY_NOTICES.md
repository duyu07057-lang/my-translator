# 第三方组件

文档解析在本地执行，不依赖 Python、Microsoft Word 或额外的服务器。

PDF 文字提取使用以下组件。便携版包含这些库的原始 DLL，未修改它们的源码。

| NuGet 包 | 固定版本 | 许可证 | 来源 |
| --- | --- | --- | --- |
| PdfPig | 0.1.11 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| Microsoft.Bcl.HashCode | 6.0.0 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Memory | 4.6.0 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Buffers | 4.6.0 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Numerics.Vectors | 4.6.0 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Runtime.CompilerServices.Unsafe | 6.1.0 | MIT | https://github.com/dotnet/maintenance-packages |

完整许可证与上游声明保留在：

- [PdfPig LICENSE](third_party/licenses/PdfPig-LICENSE.txt)
- [PdfPig NOTICES](third_party/licenses/PdfPig-NOTICES.txt)
- [Microsoft MIT LICENSE](third_party/licenses/Microsoft-MIT-LICENSE.txt)

PdfPig 许可文件来自包中记录的上游提交 `9cb3b71e62f0905e8b8c879f84188fc8e5013a91`；Microsoft 许可文件来自包中记录的上游提交 `d0c2a5a83211e271826172a6b0510c25a52dbd53`。

`dependencies.lock.json` 固定 NuGet 包版本和 SHA-256。构建时仅从 `api.nuget.org` 下载缺失包，校验后提取 .NET Framework 对应 DLL。`packages/` 是本地构建缓存，不提交到 Git。

Word (.docx) 文字提取和导出使用 .NET Framework 自带的 ZIP / XML API。测试样本是人工编写的示例，由 `tests/fixtures/generate.py` 生成；生成脚本可选依赖 ReportLab、python-docx 和 pypdf，运行软件及 C# 测试不需要它们。
