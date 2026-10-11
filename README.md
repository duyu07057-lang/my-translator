# 译知 · My Translator

一个可直接运行的 Windows 原生桌面翻译工具，使用你自己的 DeepSeek API Key。自动识别源语言，翻译到所选目标语言，支持专业领域和自定义术语。

使用 C# / WPF 开发，无第三方 NuGet / npm 依赖，适用于日常文本、论文段落和技术文档翻译。

[下载 Windows 便携版](https://github.com/duyu07057-lang/my-translator/releases/latest) · [报告问题](https://github.com/duyu07057-lang/my-translator/issues)

![译知文本翻译界面](docs/images/home.png)

## 开始使用

1. 在 [Releases](https://github.com/duyu07057-lang/my-translator/releases/latest) 下载并解压便携版，双击包内 `MyTranslator.exe` 或 `Start.cmd`。
2. 进入左下角「API 设置」，填写自己的 DeepSeek API Key，然后点击「保存设置」。密钥不需要发给开发者或填写到源码中。
3. 返回「文本翻译」，选择目标语言、专业领域和翻译风格。
4. 粘贴文本，点击「开始翻译」，或按 **Ctrl + Enter**。
5. 译文出现后，可复制或导出为 TXT / Markdown。

获取密钥：[DeepSeek 官方平台](https://platform.deepseek.com/api_keys)。API 调用需要联网，并由 DeepSeek 按账户实际用量计费；本软件不提供免费额度。界面中「API 已配置」只代表填写了密钥，首次实际翻译时才验证密钥和网络。

运行环境：Windows 10 / 11，安装 [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)。EXE 为便携版，无需安装 Python、Node.js 或开发 SDK。将 EXE 与同目录的 `MyTranslator.exe.config` 一起保留。

便携包文件名为 `MyTranslator-Windows-v1.0.zip`。也可以按下方步骤从源码构建，构建后直接运行 `dist\MyTranslator.exe`，或双击源码根目录中的 `启动翻译软件.cmd`。

## 已有功能

| 功能 | 用法 |
| --- | --- |
| 自动检测语言 | 默认自动检测，也可手动选择源语言；识别结果显示在原文上方 |
| 多种目标语言 | 简体中文、繁體中文、英语、日语、韩语、法语、德语、西班牙语、俄语、葡萄牙语、阿拉伯语 |
| 专业领域 | 通用翻译、学术论文、电子工程 / FPGA、计算机 / 人工智能、医学 / 生物、法律、金融 / 商务 |
| 翻译风格 | 准确自然、严谨学术、简洁易懂 |
| 专业术语表 | 添加原文术语、指定译法、目标语言、适用领域；支持更新和删除 |
| 长文本 | 一次最多 100,000 个 UTF-16 字符；约每 2,400 字符分段，尽量沿换行或空白拆分 |
| 停止翻译 | 立即取消等待，保留已经完成的段落，并显示「部分译文」 |
| 导入文本 | UTF-8 TXT / Markdown，或带 BOM 的 UTF-16 文本；不支持直接读取 PDF / Word |
| 复制 / 导出 | 复制译文、导出 UTF-8 TXT 或 Markdown；部分译文的默认导出名称会包含「部分译文」 |
| 反向翻译 | 完成一次翻译后点击「交换」，再点击开始翻译 |
| 翻译历史 | 最多 50 条，只记录完整成功的翻译，支持重新打开和清空 |
| 错误提示 | 区分密钥无效、余额不足、限流、模型错误、连接异常、超时和输出截断 |

快捷键：**Ctrl + Enter** 翻译，**Ctrl + Shift + C** 复制译文，**F1** 打开设置。

## 专业文本怎样用

以 FPGA 论文段落为例：

1. 目标语言选择「简体中文」。
2. 专业领域选择「电子工程 / FPGA」，文风选择「严谨学术」。
3. 在「专业术语」中点击「加入电子工程示例术语」，或加入自己课题组习惯的译法，例如 `activation function → 激活函数`。
4. 翻译后检查公式、数值、单位、缩写和关键术语。

| 原文术语 | 指定译法 | 目标语言 | 适用领域 |
| --- | --- | --- | --- |
| FPGA | 现场可编程门阵列 | 简体中文 | 电子工程 / FPGA |
| activation function | 激活函数 | 简体中文 | 所有领域 |
| throughput | 吞吐量 | 简体中文 | 电子工程 / FPGA |
| fixed-point | 定点 | 简体中文 | 电子工程 / FPGA |

![专业术语管理界面](docs/images/glossary.png)

术语匹配不区分大小写，按原文包含关系查找；只有目标语言一致，且领域一致或设为「所有领域」的条目才会发送。相同原文术语、语言和领域的条目再次添加时会更新。术语表最多 500 条，每项最多 200 字符。术语是给模型的翻译约束，不能保证每次都完全遵守。

软件通过领域提示和术语表改善专业表达，没有额外训练专用模型，也不包含专业知识库。语种识别同样由模型完成，短文本或混合语言可能判断不准。长文本的各段独立翻译，跨段上下文、代码块、公式和 Markdown 格式可能受影响；关键文档需要人工复核。

## 密钥和本地数据

- 请求固定发送到 `https://api.deepseek.com/chat/completions`，没有中间转发服务器。
- 仅在点击「开始翻译」或触发对应快捷键时发送原文、语言、领域、风格及匹配术语；没有后台剪贴板监听，也没有遥测。
- API Key 默认只保留在当前会话。勾选「在这台电脑上保存密钥」后，使用 Windows DPAPI 的 `CurrentUser` 模式加密保存，不写入明文密钥。它不能防止当前 Windows 账户下的其他程序访问或解密。
- 也可以由进程环境变量 `DEEPSEEK_API_KEY` 提供密钥；仅在没有可用保存密钥时读取。
- 翻译历史默认只保留在本次会话。开启「保存翻译历史」并保存设置后，会把原文和译文以明文写入当前用户目录。
- 关闭历史保存并点击「保存设置」，会删除磁盘上的历史文件；当前会话的历史仍可查看。
- 本地目录：`%LOCALAPPDATA%\MyTranslator\`。`settings.json` 保存偏好、术语表和可选的加密密钥，`history.json` 保存可选的翻译历史。需要在不同 Windows 用户之间迁移时，重新填写 API Key。
- EXE 没有购买代码签名证书。Windows 在首次运行从网络取得的副本时，可能显示未知发布者提示。
- 仓库排除了本地配置、历史、环境变量文件、构建目录和测试产物；密钥只需在软件内填写，不要提交至 GitHub。

## 接口约定

默认模型为 `deepseek-flash`，设置页还可选择 `deepseek-v4-pro` 或手动填写官方支持的模型名称。模型名称根据 2026-10-11 查阅的官方文档设置，后续可在界面中更新。

每段使用一次非流式 Chat Completions 请求，在同一次请求中完成语种识别和翻译。启用 JSON 输出，关闭思考模式；输出上限为 8,192 tokens，温度为 0.2。程序校验输出字段；如果服务报告输出截断，不会把该段当作完整译文。

单次请求超时为 120 秒。HTTP 429 和 5xx 最多自动重试两次，重试等待可取消；已完成段落不会重新请求。其他错误不自动重试。取消可能发生在服务端已收到请求之后，因此不保证取消操作免除服务端已经产生的费用。

官方参考：[首次调用](https://api-docs.deepseek.com/)、[Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)、[JSON 输出](https://api-docs.deepseek.com/guides/json_mode/)。

## 从源码构建

在装有 .NET Framework 4.8 的 Windows 电脑上打开 PowerShell：

```powershell
git clone https://github.com/duyu07057-lang/my-translator.git
cd my-translator
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

程序输出至 `dist\MyTranslator.exe`。如果当前 PowerShell 允许执行本地脚本，也可以直接运行：

```powershell
.\build.ps1
```

若本机的脚本执行策略阻止运行，可在单次进程中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建直接使用 Windows 自带的 .NET Framework C# 编译器，无第三方 NuGet / npm 依赖，不修改系统配置。需要安装 .NET Framework 4.8。程序不需要管理员权限。

构建完成后运行 `.\package.ps1`，可以重新生成包含 EXE、运行配置、启动脚本、使用说明和界面截图的便携 ZIP。

```text
src/
  MainWindow.xaml    原生 WPF 界面及样式
  MainWindow.cs      页面交互、文件操作、翻译流程
  Core.cs            文本拆分、术语匹配、DeepSeek 请求和响应校验
  Settings.cs        DPAPI 密钥保存、偏好和历史记录
  Program.cs         应用入口
tests/
  Tests.cs           模拟 HTTP 和实际 WPF 窗口测试
build.ps1            构建和测试入口
dist/
  MyTranslator.exe   可运行程序
```

## 验证

```powershell
.\build.ps1 -Test
```

当前版本已在本地通过 **64 项检查**，覆盖分段与 Unicode、术语过滤、接口请求格式、语种和译文解析、认证和余额错误、限流重试、取消、截断拒绝、部分译文保留、DPAPI 加解密、历史保存，以及实际 WPF 页面的配置、翻译、术语添加、历史恢复、交换和取消交互。

测试不会连接真实 DeepSeek API，也不需要真实密钥。结果和实际窗口渲染图位于 `tests\artifacts\`。测试截图中的译文由模拟服务返回，不能作为真实模型的翻译质量证据。尚未使用真实账户验证联网翻译；首次真实验证需要用户在界面中填写有效密钥。

README 中的界面截图来自自动化桌面测试，使用测试配置和示例术语，不包含真实密钥或个人翻译历史。

## 当前范围与反馈

当前版本专注于文本翻译，不支持 PDF / Word 直接导入、图片 OCR 或划词翻译。欢迎通过 [Issues](https://github.com/duyu07057-lang/my-translator/issues) 反馈问题或提出需求，请提供 Windows 版本、复现步骤和脱敏后的示例文本，不要上传 API Key 或机密原文。
