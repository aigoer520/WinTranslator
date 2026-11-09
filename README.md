# WinTranslator

Windows 桌面翻译软件（对标网易有道词典的截图翻译形态）：框选屏幕任意区域 → OCR（Optical Character Recognition，光学字符识别）→ 翻译 → 译文按原版式贴回原图；另有划词翻译、查词（多词典切换）、生词本与收藏、多款皮肤。

- 语言／框架：C#（西夏普）／WinForms（视窗窗体）／.NET Framework 4.8，纯代码无设计器资源文件
- 已发布安装包：v1.8.2；**本仓库源码在此基础上包含 v1.8.3 候选修复**（截图译图残留清理、二值化兜底识别、空译文判定等，尚未打包发布）
- 翻译引擎：有道、必应、MyMemory 等免费接口免 Key（密钥）可用；百度、DeepL、智谱 GLM（大语言模型）等引擎在设置里自填 Key 后启用

## 目录结构

| 路径 | 内容 |
|---|---|
| `src/` | 全部源码（25 个 .cs）：主窗体、截图覆盖层、OCR 服务、翻译引擎、词典、生词本、皮肤 |
| `tests/` | 无头测试与探针源码＋固定回归样本图（`tests/assets/`，12 张） |
| `WinTranslator.sln` / `WinTranslator.csproj` | Visual Studio（可视化开发环境）工程，直接打开即可调试 |
| `build.bat` / `build.sh` | 命令行构建脚本（Windows 用 csc、Linux 用 Mono 的 mcs） |
| `使用说明.txt` | 面向使用者的功能说明 |

## 构建与调试

**Visual Studio（推荐，Windows）**

1. 打开 `WinTranslator.sln`，目标框架选 .NET Framework 4.8。
2. 按 F5 即以调试方式运行；断点、单步与其他 WinForms 程序相同。

**命令行（Windows）**

```bat
build.bat
```

脚本调用 .NET Framework 自带的 csc（C# 编译器）把 `src\*.cs` 编成 `WinTranslator.exe`。

**命令行（Linux／Mono）**

```bash
./build.sh
```

产物为 `WinTranslator.exe`，可用 `mono WinTranslator.exe` 运行（界面需 X 显示；无头测试见下）。

## 运行依赖：tesseract

OCR 运行时 **不在本仓库内**（Windows 版 DLL 合计约 171MB）。把 tesseract 5.4.x 的程序文件与 `tessdata`（含 `eng.traineddata`、`chi_sim.traineddata`）放到 exe 同目录的 `tesseract\` 文件夹即可；本地打包发行版里带的就是这一套，拷过来能直接用。

## 测试

`tests/` 下为无头测试入口与各类探针（引擎、词典、存储、OCR、覆盖层布局等）。Linux／Mono 下示例：

```bash
mcs -out:Test.exe -lib:<mono 4.5 库目录> \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -r:System.Net.Http.dll -r:System.Web.Extensions.dll -r:System.Security.dll \
  src/*.cs tests/TestMain.cs
```

（`src/Program.cs` 含正式入口，与测试入口二选一编译。）覆盖层／布局类探针需要 X 显示；回归样本固定放在 `tests/assets/`，改 OCR 或贴回逻辑前后应跑一遍对比。

## 关于密钥

所有付费引擎的 API Key（接口密钥）都由使用者在软件设置里自行填写、保存在本机用户目录，**源码中不含任何私人密钥**。有道网页版接口用到的常量取自其官网公开下发的客户端脚本，属公开参数。

## 作者

aigoer520。本项目为个人作品，持续迭代中。
