using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;

namespace WinTranslator
{
    /// <summary>
    /// v1.4.0 截图翻译自检：用随包引擎对内置测试图跑识别管线，逐项报结果。
    /// 目的：真机上哪一步坏了一眼定位（引擎缺失/带坐标输出失败/整文失败/翻译不通），不再靠猜。
    /// </summary>
    public static class SelfTest
    {
        public class Step
        {
            public string Name;
            public bool Ok;
            public string Detail;
        }

        public static Bitmap BuildTestImage()
        {
            var bmp = new Bitmap(720, 230);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (var f = new Font("Microsoft YaHei UI", 30f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.Black))
                {
                    g.DrawString("Hello Screenshot 123", f, b, 30, 30);
                    g.DrawString("截图翻译自检", f, b, 30, 120);
                }
            }
            return bmp;
        }

        public static async Task<List<Step>> Run(bool includeTranslate, string glmKey, int engineIndex)
        {
            var steps = new List<Step>();

            // 1) 引擎在不在、能不能启动
            string tessdataDir;
            string exe = OcrService.FindTesseract(out tessdataDir);
            if (exe == null)
            {
                steps.Add(new Step { Name = "识别引擎", Ok = false, Detail = "找不到 tesseract 引擎（软件目录下 tesseract 文件夹缺失）" });
                steps.Add(new Step { Name = "带坐标识别", Ok = false, Detail = "引擎缺失，跳过" });
                steps.Add(new Step { Name = "整文识别", Ok = false, Detail = "引擎缺失，跳过" });
            }
            else
            {
                string ver = "";
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using (var proc = Process.Start(psi))
                    {
                        string outText = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(10000);
                        int nl = outText.IndexOf('\n');
                        ver = (nl > 0 ? outText.Substring(0, nl) : outText).Trim();
                    }
                    steps.Add(new Step { Name = "识别引擎", Ok = true, Detail = ver + "（" + exe + "）" });
                }
                catch (Exception ex)
                {
                    steps.Add(new Step { Name = "识别引擎", Ok = false, Detail = "引擎启动失败：" + ex.Message });
                }

                using (var img = BuildTestImage())
                {
                    // 2) 带坐标识别（译图靠它）
                    try
                    {
                        var blocks = await Task.Run(() => OcrService.RecognizeBlocks((Bitmap)img.Clone()));
                        bool hit = false;
                        foreach (var b in blocks) if (b.Text.IndexOf("Hello", StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                        steps.Add(new Step
                        {
                            Name = "带坐标识别",
                            Ok = hit,
                            Detail = hit ? "取到 " + blocks.Count + " 个文字块，含测试词" : "取到 " + blocks.Count + " 块，但没认出测试词 Hello"
                        });
                    }
                    catch (Exception ex)
                    {
                        steps.Add(new Step { Name = "带坐标识别", Ok = false, Detail = ex.Message });
                    }

                    // 3) 整文识别
                    try
                    {
                        string text = await Task.Run(() => OcrService.Recognize((Bitmap)img.Clone()));
                        bool hit = text.IndexOf("Hello", StringComparison.OrdinalIgnoreCase) >= 0;
                        steps.Add(new Step { Name = "整文识别", Ok = hit, Detail = hit ? "识别正常" : "没认出测试词，原文：" + text.Replace("\n", " ") });
                    }
                    catch (Exception ex)
                    {
                        steps.Add(new Step { Name = "整文识别", Ok = false, Detail = ex.Message });
                    }
                }
            }

            // 4) 翻译连通
            if (includeTranslate)
            {
                try
                {
                    var engine = new TranslationEngine { GlmApiKey = glmKey ?? "" };
                    string prefer = ResultForm.PreferOf(engineIndex);
                    if (prefer == "glm" && string.IsNullOrEmpty(glmKey)) prefer = "auto";
                    var r = await engine.Translate("Hello", "en", "zh", prefer);
                    steps.Add(new Step
                    {
                        Name = "翻译连通",
                        Ok = !string.IsNullOrWhiteSpace(r.Text),
                        Detail = !string.IsNullOrWhiteSpace(r.Text) ? "Hello → " + r.Text + "（" + r.Provider + "）" : "翻译结果为空"
                    });
                }
                catch (Exception ex)
                {
                    steps.Add(new Step { Name = "翻译连通", Ok = false, Detail = ex.Message });
                }
            }
            return steps;
        }

        public static string Format(List<Step> steps)
        {
            var lines = new List<string>();
            foreach (var s in steps)
            {
                string line = (s.Ok ? "✓ " : "✗ ") + s.Name + "：" + s.Detail;
                lines.Add(line);
                MainForm.Diag("selftest " + line);
            }
            return string.Join("\r\n", lines.ToArray());
        }
    }
}
