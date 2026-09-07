using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

internal static class ThumbnailPerformanceTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static object NewThumbnailState(Type windowType)
    {
        var state = FormatterServices.GetUninitializedObject(windowType);
        foreach (var name in new[] { "thumbnailCache", "thumbnailCacheLru", "captureDateCache", "captureDateStampCache" })
        {
            var field = windowType.GetField(name, Private);
            field.SetValue(state, Activator.CreateInstance(field.FieldType));
        }
        windowType.GetField("thumbnailCacheLock", Private).SetValue(state, new object());
        windowType.GetField("captureDateCacheLock", Private).SetValue(state, new object());
        return state;
    }

    static long DecodeAll(Type windowType, object state, IList<string> files)
    {
        var loader = windowType.GetMethod("LoadCachedThumbnail", Private);
        var stopwatch = Stopwatch.StartNew();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file => loader.Invoke(state, new object[] { file, 384 }));
        stopwatch.Stop();
        return stopwatch.ElapsedMilliseconds;
    }

    public static int Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var windowType = assembly.GetType("MainWindow", true);
        var files = Directory.EnumerateFiles(Path.GetFullPath(args[1]), "*", SearchOption.TopDirectoryOnly)
            .Where(path => String.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase)
                || String.Equals(Path.GetExtension(path), ".jpeg", StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0) throw new Exception("No JPG files in benchmark folder.");

        var first = DecodeAll(windowType, NewThumbnailState(windowType), files);
        var second = DecodeAll(windowType, NewThumbnailState(windowType), files);
        var metadataState = NewThumbnailState(windowType);
        var metadataLoader = windowType.GetMethod("PreloadGalleryMetadata", Private);
        var stopwatch = Stopwatch.StartNew();
        metadataLoader.Invoke(metadataState, new object[] { files });
        stopwatch.Stop();
        var metadataFirst = stopwatch.ElapsedMilliseconds;
        var metadataWarmState = NewThumbnailState(windowType);
        windowType.GetMethod("LoadCaptureDateCache", Private).Invoke(metadataWarmState, null);
        stopwatch.Restart();
        metadataLoader.Invoke(metadataWarmState, new object[] { files });
        stopwatch.Stop();
        var metadataSecond = stopwatch.ElapsedMilliseconds;
        Console.WriteLine("THUMBNAILS files={0} first_ms={1} persistent_ms={2} metadata_first_ms={3} metadata_persistent_ms={4}",
            files.Count, first, second, metadataFirst, metadataSecond);
        if (second > Math.Max(1500, first)) throw new Exception("Persistent thumbnail cache did not improve the second load.");
        if (metadataSecond > Math.Max(1000, metadataFirst)) throw new Exception("Persistent capture-date cache did not improve the second load.");
        return 0;
    }
}
