using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WinTranslator;

internal static class DbgProbe2
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        GlassMotion.Enabled = false;
        var store = new Store();
        Console.WriteLine("settings: FontSize=" + store.Settings.FontSize + " UiFontScale=" + store.Settings.UiFontScale + " UiFont=" + store.Settings.UiFontFamily);
        var main = new MainForm();
        main.Show();
        Application.DoEvents();
        Thread.Sleep(300);
        var pop = new DictPopupForm(store, s => { }, () => { }, null);
        pop.ShowFor("persistence", new Point(400, 300));
        for (int i = 0; i < 60 && !pop.LoadedOk; i++) { Application.DoEvents(); Thread.Sleep(200); }
        Console.WriteLine("LoadedOk=" + pop.LoadedOk + " formH=" + pop.Height);
        var card = (Control)typeof(DictPopupForm).GetField("_card", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pop);
        Console.WriteLine("cardH=" + card.Height);
        foreach (Control c in card.Controls)
        {
            string txt = (c.Text ?? "").Replace("\n", "\\n");
            if (txt.Length > 34) txt = txt.Substring(0, 34) + "...";
            Console.WriteLine(c.GetType().Name + " @(" + c.Location.X + "," + c.Location.Y + ") " + c.Width + "x" + c.Height + " [" + txt + "]");
        }
        foreach (Control c in card.Controls)
            if (c is TextBox) Console.WriteLine("TEXTBOX font=" + c.Font.Size + "pt name=" + c.Font.Name + " h=" + c.Height + " text=" + (c.Text.Length > 20 ? c.Text.Substring(0, 20) : c.Text));
        Application.DoEvents();
        Thread.Sleep(200);
        var bmp = new Bitmap(pop.Width, pop.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(pop.Location, Point.Empty, pop.Size);
        bmp.Save("/tmp/shots/dbg-popup.png");
        Console.WriteLine("saved shot " + bmp.Width + "x" + bmp.Height);
        return 0;
    }
}
