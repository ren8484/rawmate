using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;

// Compiles with RAWMate.cs and selects this entry point, exercising production
// internal classes directly. Fake tool responses are NOT real RAW-write evidence.
internal static class GpsModelTests
{
    private static int count;
    private static string fixture;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr reserved);
    private static readonly GpsMetadata athens = new GpsMetadata(37.983810, 23.727540, null, null, "source");
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    private static void Throws(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch { failed = true; }
        Check(failed, message);
    }
    private static string Xml(string type, GpsMetadata gps, string extra)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:File=\"urn:File\" xmlns:GPS=\"urn:GPS\" xmlns:ExifIFD=\"urn:ExifIFD\" xmlns:ExifTool=\"urn:ExifTool\" xmlns:System=\"urn:System\"><rdf:Description>" +
            "<System:FileName>scratch</System:FileName><File:FileType>" + type + "</File:FileType><File:ImageDataHash>" + new string('a', 64) + "</File:ImageDataHash>" +
            "<ExifIFD:ExposureTime>0.01</ExifIFD:ExposureTime><ExifIFD:Model>ILCE-6700</ExifIFD:Model><ExifTool:Validate>0 0 0</ExifTool:Validate>" +
            (gps == null ? "" : "<GPS:GPSLatitude>" + Math.Abs(gps.Latitude).ToString("R", CultureInfo.InvariantCulture) + "</GPS:GPSLatitude><GPS:GPSLatitudeRef>" + (gps.Latitude < 0 ? "S" : "N") + "</GPS:GPSLatitudeRef><GPS:GPSLongitude>" + Math.Abs(gps.Longitude).ToString("R", CultureInfo.InvariantCulture) + "</GPS:GPSLongitude><GPS:GPSLongitudeRef>" + (gps.Longitude < 0 ? "W" : "E") + "</GPS:GPSLongitudeRef><GPS:GPSStatus>A</GPS:GPSStatus>") +
            extra + "</rdf:Description></rdf:RDF>";
    }
    private static GpsSnapshot Parse(string text) { return ExifToolAdapter.ParseXml(Encoding.UTF8.GetBytes(text), "photo"); }
    private sealed class FakeRunner : IGpsToolRunner
    {
        internal readonly List<string[]> Calls = new List<string[]>();
        internal readonly Dictionary<string, GpsMetadata> Gps = new Dictionary<string, GpsMetadata>(StringComparer.OrdinalIgnoreCase);
        internal bool FailWrite, WrongGps, ChangedPixels, ChangedMetadata, ChangedMakerNotes, ReadFailure, BadVersion;
        internal Action<string> DuringWrite;
        public GpsToolResult Run(IList<string> args)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "-ver") return new GpsToolResult { Output = Encoding.UTF8.GetBytes(BadVersion ? "not-exiftool" : "13.59\n") };
            string path = args.Last();
            bool temporary = Path.GetFileName(path).StartsWith(".rawmate-gps-", StringComparison.Ordinal);
            if (args[0] == "-overwrite_original")
            {
                Check(temporary, "Tool received an original for write");
                if (FailWrite) return new GpsToolResult { ExitCode = 1, Error = "injected write error" };
                double lat = Double.Parse(args[1].Split('=')[1], CultureInfo.InvariantCulture) * (args[2].EndsWith("S") ? -1 : 1);
                double lon = Double.Parse(args[3].Split('=')[1], CultureInfo.InvariantCulture) * (args[4].EndsWith("W") ? -1 : 1);
                Gps[path] = new GpsMetadata(WrongGps ? 0 : lat, lon, null, null, path);
                byte[] pixels = File.ReadAllBytes(path); pixels[48] = (byte)'A'; File.WriteAllBytes(path, pixels);
                if (DuringWrite != null) DuringWrite(path);
                return new GpsToolResult { Output = Encoding.UTF8.GetBytes("1 image files updated") };
            }
            if (ReadFailure && temporary) return new GpsToolResult { Error = "injected read error" };
            if (args[0] == "-b") return new GpsToolResult { Output = Encoding.UTF8.GetBytes(temporary && ChangedMakerNotes ? "changed" : "binary invariant " + args[1]) };
            GpsMetadata gps; Gps.TryGetValue(path, out gps);
            string maker=Convert.ToBase64String(Encoding.UTF8.GetBytes(temporary&&ChangedMakerNotes?"changed":"binary invariant -MakerNotes"));
            string xml = Xml(Path.GetExtension(path).Equals(".arw", StringComparison.OrdinalIgnoreCase) ? "ARW" : "JPEG", gps, "<GPS:GPSMapDatum>WGS-84</GPS:GPSMapDatum><ExifIFD:MakerNoteSony rdf:datatype=\"http://www.w3.org/2001/XMLSchema#base64Binary\">"+maker+"</ExifIFD:MakerNoteSony>");
            if (temporary && ChangedPixels) xml = xml.Replace(new string('a', 64), new string('b', 64));
            if (temporary && ChangedMetadata) xml = xml.Replace("0.01", "0.02");
            return new GpsToolResult { Output = Encoding.UTF8.GetBytes(xml) };
        }
    }
    private static string Root(string name)
    {
        string path = Path.Combine(fixture, name);
        Directory.CreateDirectory(Path.Combine(path, "jpg")); Directory.CreateDirectory(Path.Combine(path, "arw")); return path;
    }
    private static string MakeFile(string root, string subdir, string name)
    {
        string path = Path.Combine(root, subdir, name);
        if (!GpsFileSafety.IsJpg(path)) { File.WriteAllText(path, "untouched RAW/XMP"); return path; }
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Rgb24, null, new byte[192], 24)));
        byte[] image; using(var stream = new MemoryStream()) { encoder.Save(stream); image = stream.ToArray(); }
        byte[] exif = new byte[54]; exif[0]=255;exif[1]=225;exif[3]=52;
        Encoding.ASCII.GetBytes("Exif").CopyTo(exif,4);
        exif[10]=73;exif[11]=73;exif[12]=42;exif[14]=8;
        exif[18]=1;exif[20]=0x25;exif[21]=0x88;exif[22]=4;exif[24]=1;exif[28]=26;
        exif[36]=1;exif[38]=9;exif[40]=2;exif[42]=2;exif[46]=(byte)'V';
        using(var stream=File.Create(path)){stream.Write(image,0,2);stream.Write(exif,0,exif.Length);stream.Write(image,2,image.Length-2);}
        return path;
    }
    private static void TestCoordinates()
    {
        foreach (double value in new[] { 0.0, 90.0, -90.0, 180.0, -180.0, 31.23042, -31.23042, 121.47370, -121.47370, 12.9999999999 })
        {
            bool latitude = Math.Abs(value) <= 90;
            string reference = latitude ? value < 0 ? "S" : "N" : value < 0 ? "W" : "E";
            var dms = GpsMetadata.ToDms(value);
            var back = GpsMetadata.FromDms(dms, reference, latitude);
            Check(back.HasValue && Math.Abs(back.Value - value) <= GpsMetadata.Tolerance, "DMS round trip " + value);
            Check((double)(uint)dms[2] / (uint)(dms[2] >> 32) < 60, "Seconds carry not normalized");
        }
        Check(GpsMetadata.FromDms(null, "N", true) == null, "Null DMS");
        Check(GpsMetadata.FromDms(new ulong[] { 1, 1, 1 }, "N", true) == null, "Zero denominator");
        Check(GpsMetadata.FromDms(GpsMetadata.ToDms(31), "E", true) == null, "Wrong latitude ref");
        Check(GpsMetadata.FromDms(GpsMetadata.ToDms(121), "N", true) == null, "Latitude range");
        Check(GpsMetadata.FromDms(new[] { 1UL << 32, (1UL << 32) | 60, 1UL << 32 }, "N", true) == null, "Invalid minutes");
        Check(GpsMetadata.FromDms(new[] { 1UL << 32, 1UL << 32, (1UL << 32) | 60 }, "N", true) == null, "Invalid seconds");
        Throws(delegate { GpsMetadata.ToDms(Double.NaN); }, "NaN DMS");
        Throws(delegate { new GpsMetadata(91, 0, null, null, ""); }, "Latitude range");
        Throws(delegate { new GpsMetadata(0, 181, null, null, ""); }, "Longitude range");
        Throws(delegate { new GpsMetadata(Double.PositiveInfinity, 0, null, null, ""); }, "Infinity coordinate");
        Throws(delegate { new GpsMetadata(0, 0, Double.NaN, null, ""); }, "NaN altitude");
        Throws(delegate { new GpsMetadata(0, 0, null, DateTime.Now, ""); }, "Non-UTC GPS time");
        Check(GpsMetadata.ReadWic(new BitmapMetadata("jpg"), "empty") == null, "Missing WIC GPS");
        Check(new GpsMetadata(0, 0, -50, DateTime.UtcNow, "x").Altitude == -50, "Optional altitude model");
    }
    private static void TestXmlAndCommands()
    {
        foreach (var gps in new[] { athens, new GpsMetadata(-31.23042, -121.47370, null, null, ""), new GpsMetadata(90, 180, null, null, "") })
            Check(gps.SameCoordinates(Parse(Xml("JPEG", gps, "")).Gps), "XML GPS hemisphere/range");
        Check(Parse(Xml("JPEG", null, "")).Gps == null, "Missing GPS");
        var partial = Parse(Xml("JPEG", null, "<GPS:GPSLatitude>31</GPS:GPSLatitude>"));
        Check(partial.Gps == null && partial.HasCoordinateFields, "Partial GPS must require overwrite confirmation");
        Throws(delegate { Parse(Xml("JPEG", athens, "<GPS:GPSLatitude>31</GPS:GPSLatitude>")); }, "Duplicate GPS rejected");
        Throws(delegate { Parse(Xml("JPEG", null, "<ExifTool:Warning>minor</ExifTool:Warning>")); }, "Warnings rejected");
        Throws(delegate { Parse(Xml("JPEG", null, "").Replace(new string('a', 64), "")); }, "Missing image hash rejected");
        Throws(delegate { Parse(Xml("TIFF", null, "")); }, "Unrecognized raw format rejected");
        Throws(delegate { Parse("<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///C:/Windows/win.ini'>]><x>&leak;</x>"); }, "XML external entities prohibited");
        var args = ExifToolAdapter.WriteArguments(Path.Combine(fixture, ".rawmate-gps-测试.JPG"), new GpsMetadata(-31.23, -121.47, 100, DateTime.UtcNow, "source"));
        Check(args.Count == 8 && args[6] == "--" && args[5] == "-EXIF:GPSStatus#=A", "Explicit five-tag write boundary");
        Check(args[2].EndsWith("S") && args[4].EndsWith("W"), "Write directions");
        Check(!args.Any(x => x.Contains("Altitude") || x.Contains("Time") || x.Contains("tagsFromFile") || x.Contains("GPS:all")), "No unrelated GPS copying");
        var fake = new FakeRunner(); var adapter = new ExifToolAdapter(fake);
        Throws(delegate { adapter.WriteTemporary(Path.Combine(fixture, "original.JPG"), athens); }, "Direct original writes rejected");
        Check(fake.Calls.Count == 1, "Rejected original must not invoke write");
        Throws(delegate { new ExifToolAdapter(new FakeRunner { BadVersion = true }); }, "Bad version rejected");
        Throws(delegate { new ExifToolAdapter(Path.Combine(fixture, "missing.exe")); }, "Unconfigured/missing executable rejected");
    }
    private static void TestPairAndSession()
    {
        var root = Root("pair"); var other = Root("other");
        string source = MakeFile(root, "jpg", "SOURCE.JPG"), jpg = MakeFile(root, "jpg", "目标.JPG");
        var fake = new FakeRunner(); fake.Gps[source] = athens; var adapter = new ExifToolAdapter(fake);
        Check(new GpsEditPlan(root, source, jpg, adapter).Targets.Count == 1, "No RAW -> JPG only");
        string raw = MakeFile(root, "arw", "目标.arw");
        var plan = new GpsEditPlan(root, source, jpg, adapter);
        Check(plan.Targets.Count == 1, "RAW present still JPG only");
        Check(!fake.Calls.Any(call => call.Last().EndsWith(".arw", StringComparison.OrdinalIgnoreCase)), "No RAW read/sync path");
        Throws(delegate { adapter.WriteTemporary(Path.Combine(root,"arw",".rawmate-gps-"+Guid.NewGuid().ToString("N")+".ARW"),athens); }, "ARW temporary write rejected");
        Throws(delegate { new GpsEditPlan(other, source, jpg, adapter); }, "Cross-root target rejected");
        Throws(delegate { new GpsEditPlan(root, source, source, adapter); }, "Source itself rejected");
        Throws(delegate { new GpsEditPlan(root, jpg, source, adapter); }, "Source without GPS rejected");
        var session = new GpsSourceSession(); session.SelectRoot(root); session.SetSource(source, adapter);
        Check(session.IsValid(), "Source selected"); session.SelectRoot(root);
        Check(session.IsValid(), "Same root preserves source"); session.SelectRoot(other);
        Check(session.Source == null && !session.IsValid(), "Root switch clears source immediately");
        Throws(delegate { session.CreatePlan(jpg,adapter); }, "No session source cannot apply");
        session.SelectRoot(root); session.SetSource(source, adapter); File.AppendAllText(source, "changed");
        Check(!session.IsValid(), "Changed source invalidated");
        session.SelectRoot(null); Check(session.Source == null, "Empty root clears source");
        Throws(delegate { plan.ValidateUnchanged(plan.Targets[0]); }, "Plan detects changed source");
        session.SelectRoot(root); session.SetSource(source, adapter); session.SelectRoot(root + "\\");
        Check(session.IsValid(), "Trailing slash is not a root change");
        File.Delete(source);Check(!session.IsValid() && session.Source==null,"Deleted source cannot remain active");
        string hardLink = Path.Combine(root, "jpg", "hardlink.JPG");
        Check(CreateHardLink(hardLink, jpg, IntPtr.Zero), "Create isolated hard-link fixture");
        Throws(delegate { GpsFileSafety.CheckPath(jpg); }, "Hard-linked original refused");
        Throws(delegate { GpsFileSafety.CheckPath(hardLink); }, "Hard-link alternate name refused");
    }
    private static void TestTransaction(string scenario, Action<FakeRunner> configure, Action<string> parser, bool expectedSuccess, bool existingGps, bool overwrite)
    {
        string root = Root(scenario), source = MakeFile(root, "jpg", "SOURCE.JPG"), jpg = MakeFile(root, "jpg", "TARGET.JPG");
        var fake = new FakeRunner(); fake.Gps[source] = athens;
        if (existingGps) fake.Gps[jpg] = new GpsMetadata(31, 121, null, null, jpg);
        var adapter = new ExifToolAdapter(fake); var plan = new GpsEditPlan(root, source, jpg, adapter);
        string original = GpsFileSafety.Hash(jpg); configure(fake);
        var transaction = new GpsFileTransaction(adapter, parser);
        var result = transaction.Execute(plan, plan.Targets[0], overwrite);
        Check(result.Success == expectedSuccess, scenario + " outcome: " + result.Message);
        Check(expectedSuccess ? GpsFileSafety.Hash(jpg) != original : GpsFileSafety.Hash(jpg) == original, scenario + " original preservation");
        Check(!expectedSuccess || Directory.GetFiles(Path.GetDirectoryName(jpg), ".rawmate-gps-*").Length == 0, scenario + " success scratch cleanup");
        if (!expectedSuccess && !existingGps) Check(!String.IsNullOrEmpty(result.RecoveryPath) && File.Exists(result.RecoveryPath), scenario + " failure evidence retained");
        Check(!File.Exists(jpg + ".rawmate-gps-backup") && !File.Exists(jpg + ".rawmate-gps-transaction"), scenario + " successful/failed-precommit cleanup");
    }
    private static void TestTransactions()
    {
        Action<string> parser = delegate(string path) { Check(new FileInfo(path).Length > 0, "Parse callback sees nonempty scratch"); };
        TestTransaction("success", f => { }, parser, true, false, false);
        TestTransaction("overwrite-denied", f => { }, parser, false, true, false);
        TestTransaction("overwrite-approved", f => { }, parser, true, true, true);
        TestTransaction("write-fails", f => f.FailWrite = true, parser, false, false, false);
        TestTransaction("wrong-gps", f => f.WrongGps = true, parser, false, false, false);
        TestTransaction("pixels-change", f => f.ChangedPixels = true, parser, false, false, false);
        TestTransaction("exposure-change", f => f.ChangedMetadata = true, parser, false, false, false);
        TestTransaction("maker-change", f => f.ChangedMakerNotes = true, parser, false, false, false);
        TestTransaction("reread-fails", f => f.ReadFailure = true, parser, false, false, false);
        TestTransaction("parse-fails", f => { }, p => { throw new IOException("injected parser failure"); }, false, false, false);
        var root = Root("pair-results"); string source = MakeFile(root, "jpg", "SOURCE.JPG"), jpg = MakeFile(root, "jpg", "PAIR.JPG"), raw = MakeFile(root, "arw", "PAIR.ARW");
        var fake = new FakeRunner(); fake.Gps[source] = athens; var adapter = new ExifToolAdapter(fake);
        var plan = new GpsEditPlan(root, source, jpg, adapter); var transaction = new GpsFileTransaction(adapter, parser);
        string rawHash = GpsFileSafety.Hash(raw);
        Check(transaction.Execute(plan, plan.Targets[0], false).Success, "Independent JPG success");
        Check(plan.Targets.Count == 1 && GpsFileSafety.Hash(raw)==rawHash, "ARW entirely untouched");
        var nextPlan = new GpsEditPlan(root, source, jpg, adapter);
        File.WriteAllText(jpg + ".rawmate-gps-backup", "interrupted original");
        Check(!transaction.Execute(nextPlan, nextPlan.Targets[0], true).Success, "Existing recovery backup blocks writes");
        Check(File.ReadAllText(jpg + ".rawmate-gps-backup") == "interrupted original", "Recovery backup preserved");
        Throws(delegate { new GpsFileTransaction(adapter, null); }, "Parsing verification mandatory");
        // Originals fit legacy MAX_PATH; the longer GUID scratch path does not.
        // Exercises a real File.Copy creation failure without ACL changes.
        string longRoot=Root(new string('l',225-fixture.Length-1));
        string longSource=MakeFile(longRoot,"jpg","SOURCE.JPG"),longTarget=MakeFile(longRoot,"jpg","TARGET.JPG");
        var longFake=new FakeRunner();longFake.Gps[longSource]=athens;var longAdapter=new ExifToolAdapter(longFake);
        var longPlan=new GpsEditPlan(longRoot,longSource,longTarget,longAdapter);string longHash=GpsFileSafety.Hash(longTarget);
        Check(!new GpsFileTransaction(longAdapter,parser).Execute(longPlan,longPlan.Targets[0],false).Success,"GUID temporary creation failure blocks write");
        Check(longHash==GpsFileSafety.Hash(longTarget),"Temporary creation failure leaves original unchanged");
        var changeRoot = Root("change-during-preparation");
        string changeSource = MakeFile(changeRoot, "jpg", "SOURCE.JPG"), changeJpg = MakeFile(changeRoot, "jpg", "TARGET.JPG");
        var changeFake = new FakeRunner(); changeFake.Gps[changeSource] = athens;
        var changeAdapter = new ExifToolAdapter(changeFake); var changePlan = new GpsEditPlan(changeRoot, changeSource, changeJpg, changeAdapter);
        string changeHash = GpsFileSafety.Hash(changeJpg);
        changeFake.DuringWrite = p => MakeFile(changeRoot, "arw", "TARGET.ARW");
        Check(new GpsFileTransaction(changeAdapter, parser).Execute(changePlan, changePlan.Targets[0], false).Success, "New RAW appearing does not affect JPG");
        Check(GpsFileSafety.Hash(changeJpg) != changeHash, "Independent JPG committed");
        var externalRoot=Root("external-target-change");string externalSource=MakeFile(externalRoot,"jpg","SOURCE.JPG"),externalTarget=MakeFile(externalRoot,"jpg","TARGET.JPG");
        var externalFake=new FakeRunner();externalFake.Gps[externalSource]=athens;var externalAdapter=new ExifToolAdapter(externalFake);
        var externalPlan=new GpsEditPlan(externalRoot,externalSource,externalTarget,externalAdapter);string externalHash=null;
        externalFake.DuringWrite=delegate(string p){File.AppendAllText(externalTarget,"external modification");externalHash=GpsFileSafety.Hash(externalTarget);};
        Check(!new GpsFileTransaction(externalAdapter,parser).Execute(externalPlan,externalPlan.Targets[0],false).Success,"External target modification blocks commit");
        Check(GpsFileSafety.Hash(externalTarget)==externalHash,"Transaction never overwrites externally modified target");
        var lockRoot = Root("locked-original");
        string lockSource = MakeFile(lockRoot, "jpg", "SOURCE.JPG"), lockJpg = MakeFile(lockRoot, "jpg", "TARGET.JPG");
        var lockFake = new FakeRunner(); lockFake.Gps[lockSource] = athens;
        var lockAdapter = new ExifToolAdapter(lockFake); var lockPlan = new GpsEditPlan(lockRoot, lockSource, lockJpg, lockAdapter);
        string lockHash = GpsFileSafety.Hash(lockJpg); FileStream held = null;
        try
        {
            var locked = new GpsFileTransaction(lockAdapter, p => { held = new FileStream(lockJpg, FileMode.Open, FileAccess.Read, FileShare.Read); });
            Check(!locked.Execute(lockPlan, lockPlan.Targets[0], false).Success, "Locked original prevents atomic replace");
        }
        finally { if (held != null) held.Dispose(); }
        Check(GpsFileSafety.Hash(lockJpg) == lockHash, "Replace failure leaves original unchanged");
        Check(!File.Exists(lockJpg + ".rawmate-gps-transaction"), "Replace failure removes own uncommitted journal");
        foreach (bool throwAfterReplace in new[] { false, true })
        {
            var rollbackRoot = Root("rollback-" + throwAfterReplace);
            string rollbackSource = MakeFile(rollbackRoot, "jpg", "SOURCE.JPG"), rollbackJpg = MakeFile(rollbackRoot, "jpg", "TARGET.JPG");
            var rollbackFake = new FakeRunner(); rollbackFake.Gps[rollbackSource] = athens;
            var rollbackAdapter = new ExifToolAdapter(rollbackFake);
            var rollbackPlan = new GpsEditPlan(rollbackRoot, rollbackSource, rollbackJpg, rollbackAdapter);
            string rollbackHash = GpsFileSafety.Hash(rollbackJpg);
            var rollback = new GpsFileTransaction(rollbackAdapter, parser, delegate(string temp, string original, string backup)
            {
                File.Replace(temp, original, backup, false);
                if (throwAfterReplace) throw new IOException("Injected post-replace failure");
                File.AppendAllText(original, "Injected post-replace corruption");
            });
            Check(!rollback.Execute(rollbackPlan, rollbackPlan.Targets[0], false).Success, "Post-replace failure reported");
            Check(GpsFileSafety.Hash(rollbackJpg) == rollbackHash, "Post-replace failure restored original bytes");
            Check(!File.Exists(rollbackJpg + ".rawmate-gps-backup") && !File.Exists(rollbackJpg + ".rawmate-gps-transaction"), "Rollback cleanup");
        }
        Throws(delegate { Parse(Xml("ARW", null, "")); }, "ARW adapter read rejected");
        var changedGps = Parse(Xml("JPEG", athens, "<GPS:GPSDateStamp>2026:10:04</GPS:GPSDateStamp>"));
        Throws(delegate { ExifToolAdapter.Verify(Parse(Xml("JPEG", null, "")), changedGps, athens); }, "Unrequested GPS time change rejected");
    }
    private static void TestProcess(string executable)
    {
        // Only -ver is sent to the actual user-selected ExifTool. No real photo write.
        var adapter = new ExifToolAdapter(executable);
        Check(Double.Parse(adapter.Version, CultureInfo.InvariantCulture) > 0, "Real configured ExifTool version");
        Console.WriteLine("Configured ExifTool version (read only): " + adapter.Version);
        // Probe actual XML/group/hash output with a generated JPEG, READ ONLY.
        string generated = Path.Combine(fixture, "只读验证 & test.JPG");
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Rgb24, null, new byte[8 * 8 * 3], 8 * 3)));
        using (var stream = File.Create(generated)) encoder.Save(stream);
        string readHash = GpsFileSafety.Hash(generated);
        GpsFileSafety.ValidateParsing(generated);
        Check(GpsFileSafety.Hash(generated) == readHash, "WIC full-frame parsing is read only");
        string broken = Path.Combine(fixture, "broken.JPG"); File.WriteAllText(broken, "not an image");
        Throws(delegate { GpsFileSafety.ValidateParsing(broken); }, "WIC rejects unparsable temporary file");
        var snapshot = adapter.Read(generated);
        Check(snapshot.Gps == null && snapshot.FileType == "JPEG" && snapshot.ImageHash.Length == 64, "Real read-only Unicode JPG metadata probe");
        Check(readHash == GpsFileSafety.Hash(generated), "ExifTool readonly probe did not alter generated image");
        var runner = new ExifToolProcessRunner(System.Reflection.Assembly.GetExecutingAssembly().Location, 3000);
        var result = runner.Run(new[] { "--fake-echo", "中文 路径 & $() .ARW" });
        Check(Encoding.UTF8.GetString(result.Output).Trim() == "中文 路径 & $() .ARW", "UTF8 arguments without shell interpretation");
        Throws(delegate { runner.Run(new[] { "--fake-echo", "bad\n-ver" }); }, "Argument newline injection rejected");
        var timeoutRunner = new ExifToolProcessRunner(System.Reflection.Assembly.GetExecutingAssembly().Location, 150);
        var watch = Stopwatch.StartNew();
        Throws(delegate { timeoutRunner.Run(new[] { "--fake-hang" }); }, "Hung tool terminated");
        Check(watch.ElapsedMilliseconds < 5000, "Timeout bounded");
        result = runner.Run(new[] { "--fake-pipes" });
        Check(result.Output.Length >= 131072 && result.Error.Length >= 131072, "Both pipes drained without deadlock");
        string changedExe = Path.Combine(fixture, "selected-tool.exe");
        File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, changedExe);
        var pinned = new ExifToolProcessRunner(changedExe, 3000);
        File.AppendAllText(changedExe, "modified after selection");
        Throws(delegate { pinned.Run(new[] { "--fake-echo", "must not run" }); }, "Modified executable rejected before start");
    }
    private static void TestCacheIsolation()
    {
        var state=(MainWindow)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainWindow));
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        foreach(var name in new[]{"fullImageCache","fullImageLoadTasks","fullImageCacheLru","imagePathEpochs","thumbnailCache","thumbnailCacheLru","thumbnailStamps","captureDateCache","captureDateStampCache","pickedFiles","rejectedFiles","jpgOnlyFiles","rawOnlyFiles","galleryFiles"})
        {var field=typeof(MainWindow).GetField(name,flags);field.SetValue(state,Activator.CreateInstance(field.FieldType));}
        foreach(var name in new[]{"fullImageCacheLock","thumbnailCacheLock","captureDateCacheLock"})typeof(MainWindow).GetField(name,flags).SetValue(state,new object());
        string root=Root("cache-isolation"),target=MakeFile(root,"jpg","TARGET.JPG"),other=MakeFile(root,"jpg","OTHER.JPG");
        var full=(Dictionary<string,BitmapSource>)typeof(MainWindow).GetField("fullImageCache",flags).GetValue(state);
        var thumb=(Dictionary<string,Tuple<int,BitmapSource>>)typeof(MainWindow).GetField("thumbnailCache",flags).GetValue(state);
        var dates=(Dictionary<string,DateTime?>)typeof(MainWindow).GetField("captureDateCache",flags).GetValue(state);
        var marks=(HashSet<string>)typeof(MainWindow).GetField("pickedFiles",flags).GetValue(state);
        var bitmap=BitmapSource.Create(8,8,96,96,PixelFormats.Rgb24,null,new byte[192],24);bitmap.Freeze();
        full[target]=full[other]=bitmap;thumb[target]=thumb[other]=Tuple.Create(8,bitmap);dates[target]=dates[other]=DateTime.UtcNow;marks.Add(target);
        typeof(MainWindow).GetField("currentSingleFile",flags).SetValue(state,target);
        typeof(MainWindow).GetField("singleZoom",flags).SetValue(state,1.25);
        typeof(MainWindow).GetField("activeFilter",flags).SetValue(state,"picked");
        typeof(MainWindow).GetField("gallerySortMode",flags).SetValue(state,"capture_desc");
        typeof(MainWindow).GetMethod("RemoveImageCachesForPaths",flags).Invoke(state,new object[]{new[]{target}});
        Check(!full.ContainsKey(target)&&full.ContainsKey(other),"Only target full image evicted");
        Check(!thumb.ContainsKey(target)&&thumb.ContainsKey(other),"Only target thumbnail evicted");
        Check(!dates.ContainsKey(target)&&dates.ContainsKey(other),"Only target EXIF evicted");
        Check(marks.Contains(target)&&(double)typeof(MainWindow).GetField("singleZoom",flags).GetValue(state)==1.25,"Cache eviction leaves pick and zoom unchanged");
        Check((string)typeof(MainWindow).GetField("currentSingleFile",flags).GetValue(state)==target&&(string)typeof(MainWindow).GetField("activeFilter",flags).GetValue(state)=="picked"&&(string)typeof(MainWindow).GetField("gallerySortMode",flags).GetValue(state)=="capture_desc","Current/filter/sort unchanged");
        var task=(System.Threading.Tasks.Task<BitmapSource>)typeof(MainWindow).GetMethod("GetFullImageTask",flags).Invoke(state,new object[]{target});
        typeof(MainWindow).GetMethod("RemoveImageCachesForPaths",flags).Invoke(state,new object[]{new[]{target}});
        task.Wait();System.Threading.Thread.Sleep(100);
        Check(!full.ContainsKey(target)&&full.ContainsKey(other),"Eviction epoch prevents stale completion from repopulating target");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-config")
        {
            Console.InputEncoding = new UTF8Encoding(false);
            string input = Console.In.ReadToEnd();
            if (input.Contains("--fake-hang")) { Thread.Sleep(30000); return 0; }
            if (input.Contains("--fake-pipes")) { Console.Error.Write(new string('e', 131072)); Console.Out.Write(new string('o', 131072)); return 0; }
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.WriteLine(input.Split('\n')[1]); return 0;
        }
        try
        {
            fixture = Path.GetFullPath(args[0]); Directory.CreateDirectory(fixture);
            TestCoordinates(); TestXmlAndCommands(); TestPairAndSession(); TestTransactions(); TestCacheIsolation();
            if (args.Length > 1) TestProcess(args[1]);
            Console.WriteLine("PASS " + count + " assertions. All writes use a FAKE adapter; real ExifTool probes are READ ONLY. Fixtures: " + fixture);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
