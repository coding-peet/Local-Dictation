using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using LocalDictation.App;
using LocalDictation.App.Services;
using LocalDictation.App.Views;
using LocalDictation.Core;

static class WaveMarkChecks
{
    static readonly int[] Silhouette = [3, 8, 15, 7, 12, 5, 3];
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Run(string output, List<string> checks)
    {
        var originalTheme = ThemeManager.Choice;
        try
        {
            foreach (var theme in new[] { "Light", "Dark" })
                CheckTheme(theme, output, checks);
        }
        finally { ThemeManager.Apply(originalTheme); }
    }

    static void CheckTheme(string theme, string output, List<string> checks)
    {
        ThemeManager.Apply(theme);
        var controller = (AppController)RuntimeHelpers.GetUninitializedObject(typeof(AppController));
        var capture = (MicrophoneCapture)RuntimeHelpers.GetUninitializedObject(typeof(MicrophoneCapture));
        var runType = typeof(AppController).GetNestedType("Run", BindingFlags.NonPublic)!;
        var run = RuntimeHelpers.GetUninitializedObject(runType);
        var settings = new Settings();
        Set(run, "Settings", settings);
        Set(run, "Capture", capture);
        Set(run, "Review", new ReviewDocument());
        Set(controller, "active", run);
        Set(controller, "<Settings>k__BackingField", settings);
        Set(controller, "<Models>k__BackingField", new List<ModelInfo>());
        Set(controller, "<StateLabel>k__BackingField", "Ready");

        var banner = new BannerWindow(controller);
        try
        {
            Require(banner.Icon is BitmapFrame { PixelWidth: > 0 }, "The WPF window could not load the packaged icon.");
            var bars = (Rectangle[])typeof(BannerWindow).GetField("bars", Fields)!.GetValue(banner)!;
            var refresh = typeof(BannerWindow).GetMethod("RefreshContent", Fields)!;
            refresh.Invoke(banner, null);
            var idle = AssertShape(bars);
            AssertBrush(bars, "Muted");
            Render(banner, Path.Combine(output, $"wave-still-{theme}.png"));

            Set(run, "Recording", true);
            refresh.Invoke(banner, null);
            var update = typeof(BannerWindow).GetMethod("UpdateWave", Fields)!;
            update.Invoke(banner, [true]);
            var quiet = AssertShape(bars);
            AssertBrush(bars, "Recording");
            Render(banner, Path.Combine(output, $"wave-quiet-{theme}.png"));
            update.Invoke(banner, [true]);
            Require(quiet.SequenceEqual(AssertShape(bars)), "Equal input changed the recording silhouette.");

            Set(capture, "<Level>k__BackingField", .9f);
            update.Invoke(banner, [true]);
            var loud = AssertShape(bars);
            Require(quiet.Zip(loud).All(height => height.First < height.Second),
                "A louder real input value did not increase all seven bars proportionately.");
            Render(banner, Path.Combine(output, $"wave-loud-{theme}.png"));

            update.Invoke(banner, [false]);
            var reduced = AssertShape(bars);
            Set(capture, "<Level>k__BackingField", 0f);
            update.Invoke(banner, [false]);
            AssertBrush(bars, "Recording");
            Require(reduced.SequenceEqual(AssertShape(bars)) && !reduced.SequenceEqual(loud),
                "Reduced motion did not hold the recording silhouette still.");

            Set(run, "Recording", false);
            Set(capture, "<Level>k__BackingField", .9f);
            refresh.Invoke(banner, null);
            AssertBrush(bars, "Muted");
            Require(idle.SequenceEqual(AssertShape(bars)) && new WindowInteropHelper(banner).Handle == 0,
                "Non-recording meter responded to residual audio or acquired a native handle.");
            checks.Add($"Recognizable waveform for actual quiet/loud peaks, static reduced motion and muted idle in {theme}");
        }
        finally { banner.Close(); }
    }

    static double[] AssertShape(Rectangle[] bars)
    {
        Require(bars.Length == Silhouette.Length, "The banner must use the seven-bar waveform.");
        var heights = bars.Select(bar => bar.Height).ToArray();
        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            Require(Math.Abs(heights[i] / heights[2] - Silhouette[i] / 15d) < .0001,
                "The waveform lost its original outline.");
            Require(bar.Height >= bar.Width && bar.RadiusX == bar.Width / 2 && bar.RadiusY == bar.RadiusX &&
                    bar.VerticalAlignment == VerticalAlignment.Center && bar.Margin.Right > 0,
                "The waveform bars require round ends, centered heights, and space between bars.");
        }
        return heights;
    }

    static void AssertBrush(Rectangle[] bars, string role)
    {
        var brush = (SolidColorBrush)Application.Current.FindResource(role);
        Require(bars.All(bar => (bar.Fill as SolidColorBrush)?.Color == brush.Color),
            $"The waveform did not use the {role} theme color.");
    }

    static void Render(BannerWindow banner, string path)
    {
        var content = (FrameworkElement)banner.Content;
        content.Measure(new Size(banner.Width, banner.Height));
        content.Arrange(new Rect(0, 0, banner.Width, banner.Height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)banner.Width, (int)banner.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Fields)!.SetValue(target, value);

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
