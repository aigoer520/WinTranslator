// v1.7.3 探针（修复前复现）：DetectSource 对混合文本的判定——
// 推文场景：英文正文为主＋界面中文「11小时」几个字，旧实现只要含 1 个汉字就判 zh。
using System;
using WinTranslator;

static class DetectProbe
{
    static void Main()
    {
        // 复刻用户截图的识别文本（原文卡实测内容）
        string tweet = "Massimo @ @Rainmaker1973 . 11 小时\nShe wanted to reach the 14.5 foot wall ever since she started at the gym.";
        Console.WriteLine("tweet(EN body + 3 CJK ui chars) => " + TranslationEngine.DetectSource(tweet) + " / target " + TranslationEngine.DetectTarget(tweet));
        Console.WriteLine("pure EN => " + TranslationEngine.DetectSource("She wanted to reach the wall ever since she started at the gym."));
        Console.WriteLine("pure ZH => " + TranslationEngine.DetectSource("她想爬上那面墙，这件事从她进健身房那天就开始了。"));
        Console.WriteLine("ZH body + EN word => " + TranslationEngine.DetectSource("她想爬上那面 14.5 foot 的墙，这件事从她进健身房那天就开始了。"));
        Console.WriteLine("short ZH ui only => " + TranslationEngine.DetectSource("11小时 · 关注"));
        Console.WriteLine("JA with kanji => " + TranslationEngine.DetectSource("彼女はジムに通い始めた日からずっとその壁に登りたいと思っていた。"));
        Console.WriteLine("KO => " + TranslationEngine.DetectSource("그녀는 체육관을 시작한 날부터 그 벽에 오르고 싶어했다."));
        Console.WriteLine("RU => " + TranslationEngine.DetectSource("Она хотела забраться на эту стену с первого дня в спортзале."));
        Console.WriteLine("EN + 1 CJK char => " + TranslationEngine.DetectSource("The meeting is scheduled at 3 pm tomorrow afternoon 会议."));
    }
}
