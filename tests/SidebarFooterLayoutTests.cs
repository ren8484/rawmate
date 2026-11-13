using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class SidebarFooterLayoutTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;

    static void Check(bool ok, string why)
    {
        checks++;
        if (!ok) throw new Exception(why);
    }

    static void Set(object state, string name, object value)
    {
        typeof(MainWindow).GetField(name, F).SetValue(state, value);
    }

    static object Get(object state, string name)
    {
        return typeof(MainWindow).GetField(name, F).GetValue(state);
    }

    static MainWindow State()
    {
        var state = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
        foreach (var field in typeof(MainWindow).GetFields(F | BindingFlags.DeclaredOnly))
        {
            var type = field.FieldType;
            if (type == typeof(object)) field.SetValue(state, new object());
            else if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null
                && (typeof(DependencyObject).IsAssignableFrom(type)
                    || (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))))
                field.SetValue(state, Activator.CreateInstance(type));
        }
        Set(state, "gpsSession", new GpsSourceSession());
        Set(state, "activeFilter", "all");
        Set(state, "gallerySortMode", "name_asc");
        Set(state, "gridTileWidth", 196d);
        Set(state, "thumbnailLoadQueued", true);
        Set(state, "filmThumbnailLoadQueued", true);
        Set(state, "navigatorUpdateQueued", true);
        return state;
    }

    static IEnumerable<FrameworkElement> Walk(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var element = child as FrameworkElement;
            if (element != null) yield return element;
            foreach (var descendant in Walk(child)) yield return descendant;
        }
    }

    static Rect Bounds(FrameworkElement element, FrameworkElement root)
    {
        return element.TransformToAncestor(root).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    [STAThread]
    static int Main()
    {
        try
        {
            // 604.5 DIP is the existing minimum-client evidence; 640 DIP models
            // 1920x1080 at 150%; 1009.5 DIP is the measured 4K/200% client height.
            var cases = new[]
            {
                new { Name = "minimum", Width = 987d, Height = 604.5d },
                new { Name = "1080p-150", Width = 1280d, Height = 640d },
                new { Name = "4k-200", Width = 1920d, Height = 1009.5d }
            };

            foreach (var model in cases)
            {
                var state = State();
                var root = (FrameworkElement)typeof(MainWindow).GetMethod("BuildUi", F).Invoke(state, null);
                root.Measure(new Size(model.Width, model.Height));
                root.Arrange(new Rect(0, 0, model.Width, model.Height));
                root.UpdateLayout();

                var settings = Walk(root).OfType<Button>()
                    .Single(button => Convert.ToString(button.Content) == "设置");
                var cleanup = (Button)Get(state, "recycleRejectedButton");
                var settingsBounds = Bounds(settings, root);
                var cleanupBounds = Bounds(cleanup, root);
                var at = " model=" + model.Name;

                Check(Math.Abs(settings.ActualHeight - 26) < .5, "settings height changed" + at);
                Check(settingsBounds.Left >= 0 && settingsBounds.Right <= 320.5,
                    "settings escaped sidebar horizontally" + at);
                Check(settingsBounds.Top >= 0 && settingsBounds.Bottom <= model.Height + .5,
                    "settings clipped vertically" + at);
                Check(cleanupBounds.Bottom <= settingsBounds.Top - 7.5,
                    "settings must remain at least 8 DIP below execute cleanup" + at);
                Check(cleanupBounds.Top >= 0 && cleanupBounds.Bottom <= model.Height + .5,
                    "execute cleanup clipped" + at);
            }

            Console.WriteLine("PASS: " + checks
                + " assertions; short 1080p/150% and tall 4K sidebar models; no physical DPI claim.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
