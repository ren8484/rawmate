using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Drawing;
using System.Drawing.Imaging;

internal static class CullModelTests
{
    static Type windowType;
    static string fixture;
    static int assertions;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    static object Call(object instance, string name, params object[] args)
    {
        return windowType.GetMethods(PrivateInstance | BindingFlags.Static)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(instance, args);
    }
    static object Field(object obj, string name)
    {
        return obj.GetType().GetField(name, BindingFlags.Public | PrivateInstance).GetValue(obj);
    }
    static void SetField(object obj, string name, object value)
    {
        obj.GetType().GetField(name, BindingFlags.Public | PrivateInstance).SetValue(obj, value);
    }
    static object NewState(string stateFile)
    {
        var instance = FormatterServices.GetUninitializedObject(windowType);
        foreach (var name in new[] { "pickedFiles", "rejectedFiles", "jpgOnlyFiles", "rawOnlyFiles", "retainedRawFiles", "selectedFiles" })
            windowType.GetField(name, PrivateInstance).SetValue(instance, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        windowType.GetField("galleryFiles", PrivateInstance).SetValue(instance, new List<string>());
        foreach (var name in new[] { "thumbnailCache", "thumbnailCacheLru" })
        {
            var field = windowType.GetField(name, PrivateInstance);
            field.SetValue(instance, Activator.CreateInstance(field.FieldType));
        }
        windowType.GetField("thumbnailCacheLock", PrivateInstance).SetValue(instance, new object());
        windowType.GetField("cullMarksPath", PrivateInstance).SetValue(instance, stateFile);
        return instance;
    }
    static string Root(string name)
    {
        var root = Path.Combine(fixture, name);
        Directory.CreateDirectory(Path.Combine(root, "jpg"));
        Directory.CreateDirectory(Path.Combine(root, "arw"));
        return root;
    }
    static string Jpg(string root, string stem) { return Path.Combine(root, "jpg", stem + ".JPG"); }
    static string Raw(string root, string stem) { return Path.Combine(root, "arw", stem + ".ARW"); }
    static void Pair(string root, string stem)
    {
        File.WriteAllText(Jpg(root, stem), "synthetic jpg " + stem);
        File.WriteAllText(Raw(root, stem), "synthetic raw " + stem);
    }
    static void Mark(object state, string jpg, string value) { Call(state, "SetDecision", jpg, value); }
    static string Decision(object state, string jpg) { return (string)Call(state, "DecisionOf", jpg); }
    static Func<string, bool> FakeRecycle(string failPath)
    {
        return path =>
        {
            if (String.Equals(path, failPath, StringComparison.OrdinalIgnoreCase)) return false;
            var bin = Path.Combine(fixture, "simulated-recycle");
            Directory.CreateDirectory(bin);
            File.Move(path, Path.Combine(bin, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path)));
            return true;
        };
    }
    static object Execute(object state, string root, string failPath)
    {
        var plan = Call(state, "BuildCleanupPlan", root);
        return Call(state, "ExecuteCleanupPlan", plan, FakeRecycle(failPath), new Func<bool>(() => (bool)Call(state, "SaveCullMarks")));
    }
    static int Count(object obj, string field) { return ((ICollection)Field(obj, field)).Count; }
    static string B64(string value) { return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)); }

    [STAThread]
    public static int Main(string[] args)
    {
        windowType = Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("MainWindow", true);
        fixture = Path.GetFullPath(args[1]);
        // Never reuse a previous run: all mutations are contained in this new fixture.
        if (Directory.Exists(fixture)) throw new Exception("Fixture must not already exist: " + fixture);
        Directory.CreateDirectory(fixture);
        var stateFile = Path.Combine(fixture, "marks.txt");
        var state = NewState(stateFile);
        var root = Root("outcomes");
        foreach (var mark in new[] { "P", "J", "R", "X", "U" })
        {
            Pair(root, mark);
            Mark(state, Jpg(root, mark), mark == "U" ? "" : mark);
        }
        Check(Directory.GetFiles(Path.Combine(root, "jpg")).Length == 5, "Marking moved JPG files");
        Check(Directory.GetFiles(Path.Combine(root, "arw")).Length == 5, "Marking moved RAW files");
        foreach (var mark in new[] { "P", "J", "R", "X", "" })
        {
            Mark(state, Jpg(root, "U"), mark);
            Check(Decision(state, Jpg(root, "U")) == mark, "Decisions are not exclusive");
        }
        windowType.GetField("activeFilter", PrivateInstance).SetValue(state, "picked");
        Check((bool)Call(state, "MatchesFilter", Jpg(root, "J")) && (bool)Call(state, "MatchesFilter", Jpg(root, "R")), "Keep filter excludes J/R");
        var gallery = (List<string>)Field(state, "galleryFiles");
        gallery.AddRange(new[] { "U", "P", "J", "R", "X" }.Select(s => Jpg(root, s)));
        Check((string)Call(state, "NextUndecidedPhoto", Jpg(root, "P")) == Jpg(root, "U"), "Forward navigation did not skip J/R");
        Check((string)Call(state, "PreviousUndecidedPhoto", Jpg(root, "X")) == Jpg(root, "U"), "Backward navigation did not skip J/R");
        Check((string)Call(state, "NextPhotoInCurrentSort", Jpg(root, "P")) == Jpg(root, "J"), "Browse mode skipped a decided photo");

        var modeUndecided = Root("mode-undecided");
        Pair(modeUndecided, "U");
        Call(state, "InitializeNavigationForFolder", modeUndecided);
        Check((bool)Field(state, "advanceToUndecided"), "Undecided folder did not start in culling mode");
        var modeCompleted = Root("mode-completed");
        Pair(modeCompleted, "P");
        Mark(state, Jpg(modeCompleted, "P"), "P");
        Call(state, "InitializeNavigationForFolder", modeCompleted);
        Check(!(bool)Field(state, "advanceToUndecided"), "Completed folder did not start in browse mode");
        SetField(state, "advanceToUndecided", true);
        Call(state, "InitializeNavigationForFolder", modeCompleted);
        Check((bool)Field(state, "advanceToUndecided"), "Same-folder refresh discarded the manual navigation mode");
        var modeEmpty = Root("mode-empty");
        Call(state, "InitializeNavigationForFolder", modeEmpty);
        Check((bool)Field(state, "advanceToUndecided"), "Empty folder did not start in culling mode");
        Check((bool)Call(state, "IsCullCompletionTransition", true, true, 1, 0), "Final undecided decision was not recognized as completion");
        Check(!(bool)Call(state, "IsCullCompletionTransition", true, false, 4, 0), "A 100% reclassification retriggered completion");
        Check(!(bool)Call(state, "IsCullCompletionTransition", true, true, 4, 1), "Incomplete batch was reported as complete");

        var navigation = Root("cleanup-navigation");
        foreach (var name in new[] { "A", "B", "C" }) Pair(navigation, name);
        Mark(state, Jpg(navigation, "B"), "X");
        var navigationOrder = new[] { Jpg(navigation, "A"), Jpg(navigation, "B"), Jpg(navigation, "C") };
        var navigationResult = Execute(state, navigation, null);
        Check((string)Call(state, "PreferredPhotoAfterCleanup", navigationOrder, Jpg(navigation, "B"), Field(navigationResult, "SuccessPaths")) == Jpg(navigation, "C"),
            "Cleanup did not continue with the next photo in current order");

        var failedNavigation = Root("failed-cleanup-navigation");
        Pair(failedNavigation, "A"); Pair(failedNavigation, "B");
        Mark(state, Jpg(failedNavigation, "A"), "R");
        var failedNavigationResult = Execute(state, failedNavigation, Jpg(failedNavigation, "A"));
        Check((string)Call(state, "PreferredPhotoAfterCleanup", new[] { Jpg(failedNavigation, "A"), Jpg(failedNavigation, "B") },
            Jpg(failedNavigation, "A"), Field(failedNavigationResult, "SuccessPaths")) == Jpg(failedNavigation, "A"),
            "Failed JPG cleanup moved away from the still-existing current photo");

        var result = Execute(state, root, null);
        Check(Count(result, "SuccessPaths") == 4 && Count(result, "FailedPaths") == 0, "Incorrect cleanup result");
        Check(File.Exists(Jpg(root, "P")) && File.Exists(Raw(root, "P")), "P was modified");
        Check(File.Exists(Jpg(root, "J")) && !File.Exists(Raw(root, "J")) && Decision(state, Jpg(root, "J")) == "J", "J outcome wrong");
        Check(!File.Exists(Jpg(root, "R")) && File.Exists(Raw(root, "R")) && Decision(state, Jpg(root, "R")) == "", "R outcome wrong");
        Check(!File.Exists(Jpg(root, "X")) && !File.Exists(Raw(root, "X")) && Decision(state, Jpg(root, "X")) == "", "X outcome wrong");
        Check(File.Exists(Jpg(root, "U")) && File.Exists(Raw(root, "U")), "Undecided modified");
        Check(((ICollection)Call(state, "CompletedRawOnlyInRoot", root)).Count == 1, "Surviving R lost its decision");
        Check(Count(Call(state, "BuildCleanupPlan", root), "Items") == 0, "Repeated cleanup is not empty");
        var check = Call(state, "InspectCorrespondence", root);
        Check((int)Field(check, "Paired") == 2 && (int)Field(check, "JpgOnly") == 1 && (int)Field(check, "RawOnly") == 1 && Count(check, "Issues") == 0, "Expected singles reported as anomalies");
        state = NewState(stateFile);
        Call(state, "LoadCullMarks");
        Check(Decision(state, Jpg(root, "J")) == "J" && ((ICollection)Call(state, "CompletedRawOnlyInRoot", root)).Count == 1, "Restart lost J/R");
        File.WriteAllText(Jpg(root, "R"), "new photo at old path");
        Check(Decision(state, Jpg(root, "R")) == "", "Reused JPG path inherited R");

        foreach (var mode in new[] { "J", "R", "X" })
        {
            var failing = Root("fail-" + mode);
            Pair(failing, mode);
            Mark(state, Jpg(failing, mode), mode);
            var failure = mode == "R" ? Jpg(failing, mode) : Raw(failing, mode);
            result = Execute(state, failing, failure);
            Check(Count(result, "FailedPaths") == 1 && File.Exists(Jpg(failing, mode)) && File.Exists(Raw(failing, mode)), mode + " failure destroyed a survivor");
            Check(Decision(state, Jpg(failing, mode)) == mode, mode + " failure cleared decision");
            Check(((ICollection)Call(state, "CompletedRawOnlyInRoot", failing)).Count == 0, mode + " failure created completed R");
            result = Execute(state, failing, null);
            Check(Count(result, "FailedPaths") == 0, mode + " retry failed");
        }
        var partial = Root("partial-X");
        Pair(partial, "PAIR");
        Mark(state, Jpg(partial, "PAIR"), "X");
        Execute(state, partial, Jpg(partial, "PAIR"));
        Check(File.Exists(Jpg(partial, "PAIR")) && !File.Exists(Raw(partial, "PAIR")) && Decision(state, Jpg(partial, "PAIR")) == "X", "Partial X lost retry anchor");
        Execute(state, partial, null);
        Check(!File.Exists(Jpg(partial, "PAIR")), "Partial X retry did not recycle JPG");

        var a = Root("A");
        var b = Root("B");
        Pair(a, "SAME");
        Pair(b, "SAME");
        Mark(state, Jpg(a, "SAME"), "X");
        Mark(state, Jpg(b, "SAME"), "P");
        Check(Count(Call(state, "BuildCleanupPlan", b), "Items") == 0, "Cross-directory X leaked into B");
        Execute(state, b, null);
        Check(File.Exists(Jpg(a, "SAME")) && File.Exists(Raw(b, "SAME")), "Cross-directory file was touched");

        var missing = Root("missing-R");
        File.WriteAllText(Jpg(missing, "PAIR"), "fixture");
        Mark(state, Jpg(missing, "PAIR"), "R");
        Check(Count(Call(state, "BuildCleanupPlan", missing), "Items") == 0, "R without RAW scheduled JPG for deletion");
        Check(Count(Call(state, "InspectCorrespondence", missing), "Issues") == 1, "Missing required RAW not reported");
        var duplicate = Root("duplicate");
        Pair(duplicate, "PAIR");
        File.WriteAllText(Path.Combine(duplicate, "jpg", "PAIR.jpeg"), "second jpg");
        Mark(state, Jpg(duplicate, "PAIR"), "J");
        Check(Count(Call(state, "BuildCleanupPlan", duplicate), "Items") == 0, "Ambiguous pairing scheduled cleanup");

        var changed = Root("changed-after-confirm");
        Pair(changed, "PAIR");
        Mark(state, Jpg(changed, "PAIR"), "X");
        var savedPlan = Call(state, "BuildCleanupPlan", changed);
        Mark(state, Jpg(changed, "PAIR"), "P");
        result = Call(state, "ExecuteCleanupPlan", savedPlan, FakeRecycle(null), new Func<bool>(() => true));
        Check(Count(result, "SuccessPaths") == 0 && File.Exists(Raw(changed, "PAIR")), "Changed decision was ignored");
        Mark(state, Jpg(changed, "PAIR"), "J");
        savedPlan = Call(state, "BuildCleanupPlan", changed);
        File.AppendAllText(Jpg(changed, "PAIR"), "modified");
        result = Call(state, "ExecuteCleanupPlan", savedPlan, FakeRecycle(null), new Func<bool>(() => true));
        Check(Count(result, "SuccessPaths") == 0 && File.Exists(Raw(changed, "PAIR")), "Changed file was ignored");
        result = Call(state, "ExecuteCleanupPlan", Call(state, "BuildCleanupPlan", changed), FakeRecycle(null), new Func<bool>(() => false));
        Check(Count(result, "SuccessPaths") == 0 && File.Exists(Raw(changed, "PAIR")), "Cleanup started despite persistence failure");

        // Interrupted R: survivor intent saved, JPG moved, final state save interrupted.
        var recovery = Root("recovery");
        Pair(recovery, "R");
        File.Move(Jpg(recovery, "R"), Path.Combine(fixture, "recovery-jpg"));
        var recoveryState = Path.Combine(fixture, "recovery-marks.txt");
        File.WriteAllLines(recoveryState, new[] { "RAWMate-CullMarks|2", "R|" + B64(Jpg(recovery, "R")), "K|" + B64(Raw(recovery, "R")) });
        var recovered = NewState(recoveryState);
        Call(recovered, "LoadCullMarks");
        Check(Decision(recovered, Jpg(recovery, "R")) == "", "Interrupted R recovery left stale JPG mark");
        Check(((ICollection)Call(recovered, "CompletedRawOnlyInRoot", recovery)).Count == 1, "Interrupted R lost survivor");
        var remapped = Path.Combine(recovery, "R.ARW");
        File.Move(Raw(recovery, "R"), remapped);
        Call(recovered, "RemapPhotoState", Raw(recovery, "R"), remapped);
        Check(((HashSet<string>)Field(recovered, "retainedRawFiles")).Contains(remapped), "Unclassification lost retained RAW");

        var interruptedChange = Root("interrupted-change");
        Pair(interruptedChange, "R");
        var interruptedChangeState = Path.Combine(fixture, "interrupted-change-marks.txt");
        File.WriteAllLines(interruptedChangeState, new[] { "RAWMate-CullMarks|2", "R|" + B64(Jpg(interruptedChange, "R")), "K|" + B64(Raw(interruptedChange, "R")) });
        var changedIntent = NewState(interruptedChangeState);
        Call(changedIntent, "LoadCullMarks");
        Mark(changedIntent, Jpg(interruptedChange, "R"), "P");
        Check(((HashSet<string>)Field(changedIntent, "retainedRawFiles")).Count == 0, "Changing interrupted R left a stale RAW survivor record");
        Call(state, "SaveCullMarks");
        Check(File.Exists(stateFile + ".bak"), "Atomic-save backup missing");

        var cacheRoot = Root("persistent-thumbnail");
        var cacheJpg = Jpg(cacheRoot, "CACHE");
        using (var bitmap = new Bitmap(640, 480))
        {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
            bitmap.Save(cacheJpg, ImageFormat.Jpeg);
        }
        Check(Call(state, "LoadCachedThumbnail", cacheJpg, 110) != null, "First thumbnail decode failed");
        var persistentCacheFile = (string)Call(state, "PersistentThumbnailPath", cacheJpg);
        Check(File.Exists(persistentCacheFile), "Persistent thumbnail was not written");
        var coldState = NewState(Path.Combine(fixture, "cold-cache-marks.txt"));
        Check(Call(coldState, "LoadCachedThumbnail", cacheJpg, 110) != null, "Cold process-style thumbnail cache load failed");
        File.Delete(persistentCacheFile);
        Console.WriteLine("PASS " + assertions + " assertions. Simulated recycle only; fixtures preserved: " + fixture);
        if (args.Length > 2 && args[2] == "--ui-fixture") MakeUiFixture(Path.Combine(fixture, "ui"));
        return 0;
    }

    static void MakeUiFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "jpg"));
        Directory.CreateDirectory(Path.Combine(root, "arw"));
        var colors = new[] { Color.SteelBlue, Color.SeaGreen, Color.DarkOrchid, Color.IndianRed, Color.DarkGoldenrod };
        var names = new[] { "01_DOUBLE", "02_JPG", "03_RAW", "04_REJECT", "05_UNDECIDED" };
        for (int i = 0; i < names.Length; i++)
        {
            using (var bmp = new Bitmap(i == 2 ? 800 : 1200, i == 2 ? 1200 : 800))
            using (var graphics = Graphics.FromImage(bmp))
            using (var font = new Font("Arial", 46))
            {
                graphics.Clear(colors[i]);
                graphics.DrawString(names[i], font, Brushes.White, 40, 80);
                bmp.Save(Jpg(root, names[i]), ImageFormat.Jpeg);
            }
            File.WriteAllText(Raw(root, names[i]), "Synthetic ARW pairing fixture (not camera RAW)");
        }
        Console.WriteLine("UI fixture: " + root);
    }
}
