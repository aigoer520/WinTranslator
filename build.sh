#!/bin/sh
# WinTranslator 命令行构建（Linux / Mono 的 mcs）
# 依赖：mono-devel（提供 mcs 与 4.5 参考程序集）
set -e
cd "$(dirname "$0")"
mcs -target:winexe -platform:anycpu -out:WinTranslator.exe \
  -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -r:System.Net.Http.dll -r:System.Web.Extensions.dll -r:System.Security.dll \
  src/*.cs
echo "构建完成：WinTranslator.exe"
echo "提醒：OCR 需要 TESSDATA_PREFIX 指向 tessdata 目录，界面运行需要 X 显示。"
