// RAWMate - a Windows WPF photo culler for JPG/JPEG + Sony ARW pairs.
// Culling is dependency-free; experimental GPS editing uses user-selected ExifTool.
// Build command is in BUILD_RAWMate.cmd. The application uses the Windows Recycle Bin,
// never a permanent delete operation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("RAWMate")]
[assembly: AssemblyDescription("JPG / Sony ARW photo culling and organization tool")]
[assembly: AssemblyCompany("ren8484")]
[assembly: AssemblyProduct("RAWMate")]
[assembly: AssemblyCopyright("Copyright © 2026 ren8484")]
[assembly: AssemblyVersion("1.5.1.0")]
[assembly: AssemblyFileVersion("1.5.1.0")]
[assembly: AssemblyInformationalVersion("1.5.1")]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "error.log"), DateTime.Now.ToString("s") + Environment.NewLine + e.Exception + Environment.NewLine + Environment.NewLine);
                MessageBox.Show("RAWMate 出现错误，详细信息已写入 error.log：\n\n" + e.Exception.Message, "RAWMate 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true;
        };
        app.Run(new MainWindow());
    }
}

// GPS is deliberately separate from culling. No code below calls classification,
// cull-mark persistence, navigation or a JPEG/TIFF encoder.
internal sealed class GpsMetadata
{
    public readonly double Latitude, Longitude;
    public readonly double? Altitude;
    public readonly DateTime? UtcTime;
    public readonly string SourcePath;
    public const double Tolerance = 0.0000001;

    public GpsMetadata(double latitude, double longitude, double? altitude, DateTime? utcTime, string sourcePath)
    {
        if (!Finite(latitude) || Math.Abs(latitude) > 90 || !Finite(longitude) || Math.Abs(longitude) > 180)
            throw new ArgumentOutOfRangeException("coordinate");
        if (altitude.HasValue && !Finite(altitude.Value)) throw new ArgumentOutOfRangeException("altitude");
        if (utcTime.HasValue && utcTime.Value.Kind != DateTimeKind.Utc) throw new ArgumentException("GPS time must be UTC.");
        Latitude = latitude; Longitude = longitude; Altitude = altitude; UtcTime = utcTime; SourcePath = sourcePath;
    }

    private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    public bool SameCoordinates(GpsMetadata other)
    {
        return other != null && Math.Abs(Latitude - other.Latitude) <= Tolerance && Math.Abs(Longitude - other.Longitude) <= Tolerance;
    }
    public string Display { get { return Latitude.ToString("F6", CultureInfo.InvariantCulture) + ", " + Longitude.ToString("F6", CultureInfo.InvariantCulture); } }
    // WIC represents an unsigned EXIF Rational as numerator in the low DWORD.
    internal static ulong[] ToDms(double value)
    {
        if (!Finite(value) || Math.Abs(value) > 180) throw new ArgumentOutOfRangeException("value");
        const uint precision = 1000000;
        long total = (long)Math.Round(Math.Abs(value) * 3600 * precision, MidpointRounding.AwayFromZero);
        uint degrees = (uint)(total / (3600L * precision)); total %= 3600L * precision;
        uint minutes = (uint)(total / (60L * precision));
        uint seconds = (uint)(total % (60L * precision));
        return new[] { (1UL << 32) | degrees, (1UL << 32) | minutes, ((ulong)precision << 32) | seconds };
    }
    internal static double? FromDms(ulong[] values, string reference, bool latitude)
    {
        reference = (reference ?? "").Trim().TrimEnd('\0').ToUpperInvariant();
        if (values == null || values.Length != 3 || !(latitude ? reference == "N" || reference == "S" : reference == "E" || reference == "W")) return null;
        var numbers = new double[3];
        for (int i = 0; i < 3; i++)
        {
            uint denominator = (uint)(values[i] >> 32);
            if (denominator == 0) return null;
            numbers[i] = (double)(uint)values[i] / denominator;
        }
        if (numbers[1] >= 60 || numbers[2] >= 60) return null;
        double result = numbers[0] + numbers[1] / 60 + numbers[2] / 3600;
        if (result > (latitude ? 90 : 180)) return null;
        return reference == "S" || reference == "W" ? -result : result;
    }
    internal static GpsMetadata ReadWic(BitmapMetadata metadata, string path)
    {
        if (metadata == null) return null;
        try
        {
            var lat = FromDms(metadata.GetQuery("/app1/ifd/gps/{ushort=2}") as ulong[], Convert.ToString(metadata.GetQuery("/app1/ifd/gps/{ushort=1}"), CultureInfo.InvariantCulture), true);
            var lon = FromDms(metadata.GetQuery("/app1/ifd/gps/{ushort=4}") as ulong[], Convert.ToString(metadata.GetQuery("/app1/ifd/gps/{ushort=3}"), CultureInfo.InvariantCulture), false);
            if (!lat.HasValue || !lon.HasValue) return null;
            return new GpsMetadata(lat.Value, lon.Value, null, null, path);
        }
        catch { return null; }
    }
}

internal sealed class GpsToolResult
{
    public int ExitCode;
    public byte[] Output;
    public string Error;
}

internal interface IGpsToolRunner
{
    GpsToolResult Run(IList<string> arguments);
}

internal sealed class ExifToolProcessRunner : IGpsToolRunner
{
    private readonly string executable, executableHash;
    private readonly int timeout;
    private static byte[] ReadBounded(Stream source, int maximum)
    {
        using (var memory = new MemoryStream())
        {
            var buffer = new byte[8192]; int count; bool overflow = false;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.Length + count > maximum) overflow = true;
                else if (!overflow) memory.Write(buffer, 0, count);
            }
            if (overflow) throw new IOException("ExifTool 输出过大，停止验证。");
            return memory.ToArray();
        }
    }
    private static void KillOwnedProcessTree(Process process)
    {
        try
        {
            if (process.HasExited) return;
            // Windows launcher packages may spawn a Perl child. Terminate only
            // this invocation's process tree, not other ExifTool instances.
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"),
                "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F");
            start.UseShellExecute = false; start.CreateNoWindow = true;
            start.RedirectStandardOutput = start.RedirectStandardError = true;
            using (var killer = Process.Start(start)) killer.WaitForExit(3000);
        }
        catch { }
        try { if (!process.HasExited) process.Kill(); } catch { }
    }
    internal ExifToolProcessRunner(string selectedExecutable, int timeoutMilliseconds)
    {
        executable = Path.GetFullPath(selectedExecutable);
        if (!String.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            throw new ArgumentException("请选择本机 ExifTool Windows 可执行文件。");
        GpsFileSafety.CheckPath(executable);
        executableHash = GpsFileSafety.Hash(executable);
        timeout = timeoutMilliseconds;
    }
    public GpsToolResult Run(IList<string> arguments)
    {
        foreach (var argument in arguments)
            if (String.IsNullOrEmpty(argument) || argument.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("ExifTool 参数不能包含空行或控制字符。");
        if (GpsFileSafety.Hash(executable) != executableHash) throw new IOException("ExifTool 已改变，请重新选择并验证。");
        using (var process = new Process())
        {
            var start = new ProcessStartInfo(executable, "-config \"\" -charset filename=UTF8 -@ -");
            start.UseShellExecute = false; start.CreateNoWindow = true;
            start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.WorkingDirectory = Path.GetDirectoryName(executable);
            // Override only the child's inherited POSIX locale, never the user's settings.
            start.EnvironmentVariables["LC_ALL"] = "C";
            start.EnvironmentVariables["LC_CTYPE"] = "C";
            start.EnvironmentVariables["LANG"] = "C";
            process.StartInfo = start;
            process.Start();
            // Drain both pipes concurrently; cap output without stopping the drain.
            var output = Task.Factory.StartNew(delegate
            {
                return ReadBounded(process.StandardOutput.BaseStream, 64 * 1024 * 1024);
            });
            var errors = Task.Factory.StartNew(delegate { return Encoding.UTF8.GetString(ReadBounded(process.StandardError.BaseStream, 2 * 1024 * 1024)); });
            var inputTask = Task.Factory.StartNew(delegate
            {
                byte[] input = new UTF8Encoding(false).GetBytes(String.Join("\n", arguments) + "\n");
                process.StandardInput.BaseStream.Write(input, 0, input.Length);
                process.StandardInput.Close();
            });
            try
            {
                if (!process.WaitForExit(timeout)) throw new TimeoutException("ExifTool 调用超时。");
                if (!Task.WaitAll(new Task[] { output, errors, inputTask }, timeout)) throw new TimeoutException("ExifTool 输入输出读取超时。");
                string error = errors.Result;
                // The user may explicitly select exiftool(-k).exe. EOF on redirected
                // stdin ends its pause; discard just its known terminal-only prompt.
                error = String.Join("\n", error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Trim() != "-- press ENTER --" && line.Trim() != "-- press RETURN --"));
                return new GpsToolResult { ExitCode = process.ExitCode, Output = output.Result, Error = error };
            }
            catch
            {
                KillOwnedProcessTree(process);
                try { Task.WaitAll(new Task[] { output, errors, inputTask }, 1000); } catch { }
                throw;
            }
        }
    }
}

// Identity pinned to the complete user-approved 13.59 package, not PATH or EXE alone.
internal sealed class GpsPerformance
{
    [ThreadStatic] internal static GpsPerformance Current;
    private readonly SortedDictionary<string, long> stages = new SortedDictionary<string, long>();
    internal int ManifestPasses, ManifestFiles, ProcessCalls, MainDecodes, EmbeddedDecodes, CacheHits;
    internal long TotalMilliseconds;
    internal sealed class Scope : IDisposable {
        private readonly GpsPerformance previous; internal Scope(GpsPerformance value){previous=Current;Current=value;}
        public void Dispose(){Current=previous;}
    }
    internal sealed class Stage : IDisposable {
        private readonly GpsPerformance owner; private readonly string name; private readonly Stopwatch watch=Stopwatch.StartNew();
        internal Stage(GpsPerformance owner,string name){this.owner=owner;this.name=name;}
        public void Dispose(){if(owner!=null)owner.Add(name,watch.ElapsedMilliseconds);}
    }
    internal IDisposable Activate(){return new Scope(this);}
    internal static IDisposable Measure(string name){return new Stage(Current,name);}
    internal void Add(string name,long ms){long prior;stages.TryGetValue(name,out prior);stages[name]=prior+ms;}
    public override string ToString(){return "GPS performance (ms; nested stages are not additive)\n"+
        String.Join("\n",stages.Select(p=>p.Key+"="+p.Value))+"\nTOTAL_MS="+TotalMilliseconds+
        "\nMANIFEST_PASSES="+ManifestPasses+" MANIFEST_FILES="+ManifestFiles+" PROCESS_CALLS="+ProcessCalls+
        " WIC_MAIN_DECODES="+MainDecodes+" WIC_EMBEDDED_DECODES="+EmbeddedDecodes+" BASELINE_CACHE_HITS="+CacheHits;}
}

internal sealed class ApprovedExifToolRunner : IGpsToolRunner, IDisposable
{
    internal const string ManifestHash = "94C298C2C60460507FFA7F3098F26B69E56490A08269AFB2AFE639F84048E750";
    private readonly string folder;
    private readonly ExifToolProcessRunner process;
    private readonly List<FileStream> locks = new List<FileStream>();
    private readonly Dictionary<string,string> identities = new Dictionary<string,string>(StringComparer.Ordinal);
    private readonly object gate = new object();
    private bool disposed;
    internal readonly GpsPerformance IdentityPerformance = new GpsPerformance();
    internal ApprovedExifToolRunner(string executable)
    {
        var watch=Stopwatch.StartNew();
        folder = Path.GetDirectoryName(Path.GetFullPath(executable));
        try { using(IdentityPerformance.Activate()) using(GpsPerformance.Measure("tool identity validation")) {
        if(Path.GetFileName(executable)!="exiftool(-k).exe")throw new IOException("请选择已冻结的 ExifTool 可执行文件。");
        foreach (string name in new[] { "PERL5LIB", "PERLLIB", "PERL5OPT", "PAR_GLOBAL_TMP", "PAR_TEMP" })
            if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) throw new IOException("ExifTool 运行环境含外部 Perl 覆盖，GPS 写入不可用。");
        foreach(var file in Files()) {var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);locks.Add(stream);identities.Add(file,GpsFileSafety.HandleIdentity(stream));}
        CheckIdentity(); CheckSession(); process = new ExifToolProcessRunner(executable, 30000);
        }} catch {Dispose();throw;} finally {IdentityPerformance.TotalMilliseconds=watch.ElapsedMilliseconds;}
    }
    private string[] Files() { GpsFileSafety.CheckPath(folder); return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToArray(); }
    private void CheckIdentity()
    {
        if(GpsPerformance.Current!=null)GpsPerformance.Current.ManifestPasses++;
        var lines = new List<string> { "RelativePath\tBytes\tSHA256" };
        foreach (var path in Files())
        {
            if(GpsPerformance.Current!=null)GpsPerformance.Current.ManifestFiles++;
            GpsFileSafety.CheckPath(path);
            string relative = path.Substring(folder.Length + 1).Replace('\\', '/');
            if (relative.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0) throw new IOException("工具 manifest 路径无效。");
            lines.Add(relative + "\t" + new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture) + "\t" + GpsFileSafety.Hash(path).Replace("-", ""));
        }
        using (var hash = SHA256.Create())
            if (BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false).GetBytes(String.Join("\n", lines) + "\n"))).Replace("-", "") != ManifestHash)
                throw new IOException("ExifTool 完整工具身份不符合已验证的 13.59 manifest；GPS 写入不可用。");
    }
    public GpsToolResult Run(IList<string> arguments)
    {
        lock(gate) {
            using(GpsPerformance.Measure("tool identity validation"))CheckSession();
            if(GpsPerformance.Current!=null)GpsPerformance.Current.ProcessCalls++;
            var result=process.Run(arguments);
            using(GpsPerformance.Measure("tool identity validation"))CheckSession();
            return result;
        }
    }
    internal void CheckSession() {
        if(disposed||locks.Count!=identities.Count||locks.Count==0)throw new IOException("ExifTool session/只读锁已失效，请重新选择工具。");
        foreach(string name in new[]{"PERL5LIB","PERLLIB","PERL5OPT","PAR_GLOBAL_TMP","PAR_TEMP"})
            if(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))throw new IOException("ExifTool 运行环境已改变。");
        var files=Files();if(!files.SequenceEqual(identities.Keys.OrderBy(p=>p,StringComparer.Ordinal)))throw new IOException("ExifTool 工具目录成员已改变，请重新选择并完整验证。");
        for(int i=0;i<files.Length;i++) {
            if(locks[i].SafeFileHandle.IsClosed||locks[i].SafeFileHandle.IsInvalid||GpsFileSafety.HandleIdentity(locks[i])!=identities[files[i]])throw new IOException("ExifTool 依赖 identity/锁状态异常。");
            using(var current=new FileStream(files[i],FileMode.Open,FileAccess.Read,FileShare.Read))
                if(GpsFileSafety.HandleIdentity(current)!=identities[files[i]])throw new IOException("ExifTool 依赖路径 identity 已改变。");
        }
    }
    public void Dispose(){lock(gate){if(disposed)return;disposed=true;foreach(var stream in locks)stream.Dispose();locks.Clear();}}
    ~ApprovedExifToolRunner(){Dispose();}
}

internal sealed class GpsSnapshot
{
    public GpsMetadata Gps;
    public bool HasCoordinateFields;
    public string ImageHash, FileType, Status, FileIdentity;
    public JpgGpsIntegrity.Snapshot Jpeg;
    public readonly SortedDictionary<string, string> OtherMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public readonly Dictionary<string, string> BinaryHashes = new Dictionary<string, string>(StringComparer.Ordinal);
}

internal sealed class ExifToolAdapter : IDisposable
{
    private readonly IGpsToolRunner runner;
    private sealed class Baseline { internal string Identity; internal GpsSnapshot Snapshot; }
    private readonly Dictionary<string,Baseline> baselines = new Dictionary<string,Baseline>(StringComparer.OrdinalIgnoreCase);
    private readonly object baselineGate = new object();
    private bool disposed;
    public readonly string Version;
    internal ExifToolAdapter(string selectedExecutable) : this(new ApprovedExifToolRunner(selectedExecutable)) { }
    internal ExifToolAdapter(IGpsToolRunner selectedRunner)
    {
        runner = selectedRunner;
        Version = Encoding.UTF8.GetString(Run(new[] { "-ver" })).Trim();
        double version;
        if (!Double.TryParse(Version, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out version) || Version != "13.59")
            throw new IOException("所选工具没有返回有效 ExifTool 版本。");
    }
    private byte[] Run(IList<string> arguments)
    {
        if(disposed)throw new IOException("ExifTool session 已关闭。");
        var result = runner.Run(arguments);
        if (result.ExitCode != 0 || !String.IsNullOrWhiteSpace(result.Error))
            throw new IOException("ExifTool 失败或报告警告：" + result.Error);
        return result.Output ?? new byte[0];
    }
    internal static IList<string> WriteArguments(string temporaryFile, GpsMetadata gps)
    {
        // No -tagsFromFile, GPS:all, -m, -F, image encoder or shell interpolation.
        return new[] { "-overwrite_original",
            "-EXIF:GPSLatitude=" + Math.Abs(gps.Latitude).ToString("R", CultureInfo.InvariantCulture),
            "-EXIF:GPSLatitudeRef=" + (gps.Latitude < 0 ? "S" : "N"),
            "-EXIF:GPSLongitude=" + Math.Abs(gps.Longitude).ToString("R", CultureInfo.InvariantCulture),
            "-EXIF:GPSLongitudeRef=" + (gps.Longitude < 0 ? "W" : "E"), "-EXIF:GPSStatus#=A", "--", Path.GetFullPath(temporaryFile) };
    }
    internal void WriteTemporary(string temporaryFile, GpsMetadata gps)
    {
        Guid scratchId;
        if (!Path.GetFileName(temporaryFile).StartsWith(".rawmate-gps-", StringComparison.Ordinal) ||
            !GpsFileSafety.IsJpg(temporaryFile) || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(temporaryFile).Substring(13), "N", out scratchId))
            throw new InvalidOperationException("只允许对 RAWMate GPS 临时副本调用写入。");
        Run(WriteArguments(temporaryFile, gps));
    }
    internal GpsSnapshot Read(string file)
    {
        return ReadCore(file,false);
    }
    internal GpsSnapshot ReadBaseline(string file) {return ReadCore(file,true);}
    private GpsSnapshot ReadCore(string file,bool cache) {
        lock(baselineGate) {
            if(disposed)throw new IOException("ExifTool session 已关闭。");
            file=Path.GetFullPath(file);GpsFileSafety.CheckPath(file);
            using(GpsPerformance.Measure("tool identity validation"))ValidateSession();
            using(var guard=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                string identity=GpsFileSafety.ContentIdentity(guard);Baseline existing;
                if(cache&&baselines.TryGetValue(file,out existing)&&existing.Identity==identity) {
                    if(GpsPerformance.Current!=null)GpsPerformance.Current.CacheHits++;return existing.Snapshot;
                }
                baselines.Remove(file);
                byte[] xml;using(GpsPerformance.Measure("metadata read (includes ExifTool Validate)"))
                    xml=Run(new[]{"-X","-G1","-a","-n","-struct","-u","-b","-api","ImageHashType=SHA256","-all",
                        "-MakerNotes","-ThumbnailImage","-PreviewImage","-JpgFromRaw","-ICC_Profile","-ImageDataHash","-validate","-warning","-error","--",file});
                GpsSnapshot snapshot;using(GpsPerformance.Measure("Validate / metadata parse"))snapshot=ParseXml(xml,file);
                snapshot.Jpeg=JpgGpsIntegrity.Capture(file);
                if(GpsFileSafety.ContentIdentity(guard)!=identity)throw new IOException("完整 baseline 读取期间文件 identity 已改变。");
                snapshot.FileIdentity=identity;
                if(cache){if(baselines.Count>=16)baselines.Remove(baselines.Keys.First());baselines[file]=new Baseline{Identity=identity,Snapshot=snapshot};}
                return snapshot;
            }
        }
    }
    public void Dispose(){lock(baselineGate){if(disposed)return;disposed=true;baselines.Clear();var owned=runner as IDisposable;if(owned!=null)owned.Dispose();}}
    internal void ValidateSession(){if(disposed)throw new IOException("ExifTool session 已关闭。");var approved=runner as ApprovedExifToolRunner;if(approved!=null)approved.CheckSession();}
    internal static GpsSnapshot ParseXml(byte[] xml, string file)
    {
        var result = new GpsSnapshot();
        foreach(var tag in new[]{"MakerNotes","ThumbnailImage","PreviewImage","JpgFromRaw","ICC_Profile"})result.BinaryHashes[tag]="absent";
        var binarySeen=new Dictionary<string,string>();
        var document = new XmlDocument { XmlResolver = null };
        using (var stream = new MemoryStream(xml))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
        var descriptions = document.GetElementsByTagName("Description", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        if (descriptions.Count != 1) throw new IOException("ExifTool 必须返回一个目标文件。");
        string latitude = null, longitude = null, latRef = null, lonRef = null;
        bool validated = false;
        var values = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (XmlNode node in descriptions[0].ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            string tag = node.LocalName, group = node.Prefix, key = group + ":" + tag;
            string binaryTag=tag.StartsWith("MakerNote",StringComparison.Ordinal)?"MakerNotes":tag;
            if(node.Attributes!=null&&node.Attributes["datatype","http://www.w3.org/1999/02/22-rdf-syntax-ns#"]!=null) {
                if(node.Attributes["datatype","http://www.w3.org/1999/02/22-rdf-syntax-ns#"].Value!="http://www.w3.org/2001/XMLSchema#base64Binary")throw new IOException("未知 binary XML 类型。");
                byte[] payload=Convert.FromBase64String(node.InnerText);string hash;
                using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(payload));
                if(result.BinaryHashes.ContainsKey(binaryTag)) {
                    string prior;if(binarySeen.TryGetValue(binaryTag,out prior)&&prior!=hash)throw new IOException("重复 binary 块不一致："+binaryTag);
                    binarySeen[binaryTag]=hash;result.BinaryHashes[binaryTag]=payload.Length==0?"absent":hash;
                    continue; // All bytes are compared through BinaryHashes, including optional blocks.
                }
            }
            if (group == "ExifTool" && tag == "Validate") validated = true;
            if (group == "ExifTool" && (tag == "Error" || tag == "Warning" || tag == "Validate" && node.InnerText != "0 0 0" && node.InnerText != "0" && node.InnerText != "OK"))
                throw new IOException("文件结构验证未通过：" + node.InnerText);
            if (group == "GPS" && new[] { "GPSLatitude", "GPSLongitude", "GPSLatitudeRef", "GPSLongitudeRef" }.Contains(tag))
            {
                result.HasCoordinateFields = true;
                if (tag == "GPSLatitude") { if (latitude != null) throw new IOException("重复 GPSLatitude。"); latitude = node.InnerText; }
                if (tag == "GPSLongitude") { if (longitude != null) throw new IOException("重复 GPSLongitude。"); longitude = node.InnerText; }
                if (tag == "GPSLatitudeRef") { if (latRef != null) throw new IOException("重复 GPSLatitudeRef。"); latRef = node.InnerText; }
                if (tag == "GPSLongitudeRef") { if (lonRef != null) throw new IOException("重复 GPSLongitudeRef。"); lonRef = node.InnerText; }
                continue;
            }
            if (group == "GPS" && tag == "GPSStatus")
            {
                if (result.Status != null) throw new IOException("重复 GPSStatus。");
                result.Status = node.InnerText; continue;
            }
            if (tag == "ImageDataHash" && group == "File") result.ImageHash = node.InnerText;
            if (tag == "FileType" && group == "File") result.FileType = node.InnerText;
            if (group == "System" || group == "ExifTool" || group == "Composite") continue;
            List<string> list;
            if (!values.TryGetValue(key, out list)) values[key] = list = new List<string>();
            list.Add(node.InnerXml);
        }
        foreach (var pair in values) result.OtherMetadata[pair.Key] = String.Join("\u001f", pair.Value.OrderBy(x => x, StringComparer.Ordinal));
        double lat, lon;
        if (Double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
            Double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out lon) &&
            (latRef == "N" || latRef == "S") && (lonRef == "E" || lonRef == "W"))
        {
            // -n may already apply the reference sign. Both representations are valid.
            try { result.Gps = new GpsMetadata(Math.Abs(lat) * (latRef == "S" ? -1 : 1), Math.Abs(lon) * (lonRef == "W" ? -1 : 1), null, null, file); }
            catch (ArgumentException) { }
        }
        if (!validated) throw new IOException("ExifTool 没有返回结构验证结果。");
        if (String.IsNullOrEmpty(result.ImageHash) || result.ImageHash.Length != 64 || result.ImageHash.Any(c => !Uri.IsHexDigit(c)))
            throw new IOException("缺少 SHA-256 图像数据哈希；此文件不能安全写入。");
        if (result.FileType != "JPEG") throw new IOException("GPS 编辑本轮只支持 JPG/JPEG，不处理 ARW / XMP。");
        return result;
    }
    internal static void Verify(GpsSnapshot before, GpsSnapshot after, GpsMetadata expected)
    {
        if (!expected.SameCoordinates(after.Gps) || after.Status != "A") throw new IOException("GPS 五字段回读与来源不一致，或 GPSStatus 不是 A。");
        if (before.FileType != "JPEG" || after.FileType != "JPEG" || before.ImageHash != after.ImageHash) throw new IOException("JPEG 图像数据发生变化。");
        var proved = JpgGpsIntegrity.Verify(before.Jpeg, after.Jpeg, expected);
        foreach (var key in before.OtherMetadata.Keys.Union(after.OtherMetadata.Keys))
        {
            string old, value;
            bool existed = before.OtherMetadata.TryGetValue(key, out old), exists = after.OtherMetadata.TryGetValue(key, out value);
            if (existed && exists && old == value) continue;
            if (key == "GPS:GPSVersionID" && !existed && exists && value == "2 3 0 0" && proved.Contains("NEW_GPS_IFD")) continue;
            if (proved.Contains(key)) continue;
            throw new IOException("未授权的非目标元数据变化：" + key);
        }
        foreach (var key in before.BinaryHashes.Keys.Union(after.BinaryHashes.Keys))
        {
            string old, value;
            if (!before.BinaryHashes.TryGetValue(key, out old) || !after.BinaryHashes.TryGetValue(key, out value)) throw new IOException("缺少块证据：" + key);
            if (old != value && !(key == "MakerNotes" && proved.Contains("BLOCK:Raw/MakerNotes"))) throw new IOException("原始块变化：" + key);
        }
    }
}

internal sealed class ClassificationEntry
{
    internal string Source,Destination,Identity,Disposition,Reason;
}

internal sealed class ClassificationPlan
{
    internal string Root;internal bool Undo;
    internal readonly List<ClassificationEntry> Entries=new List<ClassificationEntry>();
    internal readonly List<string> Diagnostics=new List<string>();
    internal int ReadyCount {get{return Entries.Count(e=>e.Disposition=="ready");}}
    internal int ConflictCount {get{return Entries.Count(e=>e.Disposition=="conflict");}}
    internal static string Destination(PhotoItem item,bool undo) {
        string parent=Path.GetDirectoryName(item.AbsolutePath);
        return undo?Path.Combine(Path.GetDirectoryName(parent),Path.GetFileName(item.AbsolutePath)):
            Path.Combine(parent,item.IsJpg?"jpg":"arw",Path.GetFileName(item.AbsolutePath));
    }
    internal static bool Exists(string path) {
        try {File.GetAttributes(path);return true;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
    }
    internal static void ValidatePaths(string root,string source,string destination) {
        if(!PhotoCollection.ContainsPath(root,source)||!PhotoCollection.ContainsPath(root,destination))throw new IOException("分类路径不在当前集合内。");
        GpsFileSafety.CheckPath(root);GpsFileSafety.CheckPath(source);GpsFileSafety.CheckPath(destination);
    }
    internal static ClassificationPlan Build(string root,bool undo,CancellationToken token) {
        var plan=new ClassificationPlan{Root=PhotoCollection.Normalize(root),Undo=undo};
        GpsFileSafety.CheckPath(plan.Root);
        var snapshot=PhotoCollection.Scan(plan.Root,token);plan.Diagnostics.AddRange(snapshot.Diagnostics);
        foreach(var photo in snapshot.Photos) {
            token.ThrowIfCancellationRequested();if(undo?photo.IsUnclassified:!photo.IsUnclassified)continue;
            var e=new ClassificationEntry{Source=photo.AbsolutePath,Disposition="ready"};plan.Entries.Add(e);
            try {
                e.Destination=Destination(photo,undo);ValidatePaths(plan.Root,e.Source,e.Destination);
                if(undo&&PhotoCollection.IsTypeDirectory(Path.GetFileName(plan.Root))&&GpsFileSafety.Same(Path.GetDirectoryName(e.Source),plan.Root))throw new IOException("取消分类不能移出当前集合根目录。");
                if(Exists(e.Destination)){e.Disposition="conflict";e.Reason="目标已存在，未覆盖";continue;}
                using(var stream=new FileStream(e.Source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))e.Identity=GpsFileSafety.HandleIdentity(stream);
            }catch(Exception error){e.Disposition="skipped";e.Reason=error.Message;}
        }
        foreach(var group in plan.Entries.Where(e=>e.Destination!=null).GroupBy(e=>e.Destination,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            foreach(var e in group){e.Disposition="conflict";e.Reason="多个源文件指向同一目标，全部保留原位";}
        return plan;
    }
}

// Keep every ancestor directory stable through the final checks and rename.
// No delete-sharing: a junction/rename swap cannot replace a checked ancestor.
internal sealed class ClassificationDirectoryLocks : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]private struct Info {internal uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,out Info info);
    private readonly List<Microsoft.Win32.SafeHandles.SafeFileHandle> handles=new List<Microsoft.Win32.SafeHandles.SafeFileHandle>();
    internal ClassificationDirectoryLocks(string source,string destination) {
        try {
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string start in new[]{Path.GetDirectoryName(source),Path.GetDirectoryName(destination)})
                for(string p=start;!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if(Directory.Exists(p))paths.Add(p);
            foreach(string path in paths.OrderBy(p=>p.Length))Lock(path);
            string parent=Path.GetDirectoryName(destination);Directory.CreateDirectory(parent);Lock(parent);
        }catch{Dispose();throw;}
    }
    private void Lock(string path) {
        var handle=CreateFile(path,0x80,3,IntPtr.Zero,3,0x02000000|0x00200000,IntPtr.Zero);
        Info info;
        if(handle.IsInvalid||!GetFileInformationByHandle(handle,out info)||(info.Attributes&(uint)FileAttributes.ReparsePoint)!=0||(info.Attributes&(uint)FileAttributes.Directory)==0){handle.Dispose();throw new IOException("分类目录无法锁定或包含链接："+path);}
        handles.Add(handle);
    }
    public void Dispose(){foreach(var h in handles)h.Dispose();handles.Clear();}
}

internal sealed class ClassificationResult
{
    internal readonly Dictionary<string,string> Moved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    internal readonly List<string> Conflicts=new List<string>(),Skipped=new List<string>(),Failed=new List<string>();
    internal bool Cancelled;
}

internal static class ClassificationExecutor
{
    internal static ClassificationResult Execute(ClassificationPlan plan,CancellationToken token,Func<string,string,bool> commit,Action<int,int,string> progress=null,Action<ClassificationEntry> beforeMove=null) {
        var result=new ClassificationResult();int index=0;bool stop=false;
        foreach(var e in plan.Entries) {
            index++;if(progress!=null)progress(index,plan.Entries.Count,e.Source);
            if(e.Disposition=="conflict"){result.Conflicts.Add(e.Source+"："+e.Reason);continue;}
            if(e.Disposition!="ready"){result.Skipped.Add(e.Source+"："+e.Reason);continue;}
            if(token.IsCancellationRequested||stop){result.Cancelled=token.IsCancellationRequested;result.Skipped.Add(e.Source+"：未开始移动");continue;}
            try {
                ClassificationPlan.ValidatePaths(plan.Root,e.Source,e.Destination);
                if(ClassificationPlan.Exists(e.Destination)){result.Conflicts.Add(e.Source+"：确认后目标已存在，未覆盖");continue;}
                using(var dirs=new ClassificationDirectoryLocks(e.Source,e.Destination))
                using(var file=new FileStream(e.Source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
                    if(GpsFileSafety.HandleIdentity(file)!=e.Identity)throw new IOException("确认后源文件 identity 已改变");
                    if(beforeMove!=null)beforeMove(e);
                    ClassificationPlan.ValidatePaths(plan.Root,e.Source,e.Destination);
                    using(var current=new FileStream(e.Source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                        if(GpsFileSafety.HandleIdentity(current)!=e.Identity)throw new IOException("移动前源路径 identity 已改变");
                    if(ClassificationPlan.Exists(e.Destination)){result.Conflicts.Add(e.Source+"：目标已出现，未覆盖");continue;}
                    File.Move(e.Source,e.Destination);bool committed=false;
                    try {
                        using(var moved=new FileStream(e.Destination,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                            if(GpsFileSafety.HandleIdentity(moved)!=e.Identity)throw new IOException("移动后 identity 不符");
                        committed=commit(e.Source,e.Destination);
                        if(!committed)throw new IOException("状态未能持久化");
                        result.Moved.Add(e.Source,e.Destination);
                    }catch(Exception stateError) {
                        stop=true;
                        if(!committed&&!ClassificationPlan.Exists(e.Source)) {
                            using(var moved=new FileStream(e.Destination,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                                if(GpsFileSafety.HandleIdentity(moved)!=e.Identity)throw new IOException("状态保存失败且目标改变，请保留恢复文件："+e.Destination,stateError);
                            File.Move(e.Destination,e.Source);
                            throw new IOException("状态保存失败，已还原原路径；停止后续移动",stateError);
                        }
                        throw new IOException("状态保存失败，无法安全还原；请保留源/目标核查："+e.Source+" → "+e.Destination,stateError);
                    }
                }
            }catch(Exception error){result.Failed.Add(e.Source+"："+error.Message);}
        }
        return result;
    }
}

internal sealed class PhotoItem
{
    // Pair scopes remove exactly one immediate type-directory layer, never a
    // date/collection ancestor. Flat gallery layout does not erase identity.
    public readonly string AbsolutePath, RelativePath, RelativeDirectory, PairKey;
    public readonly bool IsJpg,IsUnclassified;
    internal PhotoItem(string root, string file) {
        AbsolutePath=Path.GetFullPath(file);RelativePath=PhotoCollection.Relative(root,AbsolutePath);
        RelativeDirectory=Path.GetDirectoryName(RelativePath)??String.Empty;
        string scope=RelativeDirectory;
        if(PhotoCollection.IsTypeDirectory(Path.GetFileName(scope)))scope=Path.GetDirectoryName(scope)??String.Empty;
        PairKey=Path.Combine(scope,Path.GetFileNameWithoutExtension(file));
        IsJpg=GpsFileSafety.IsJpg(file);
        IsUnclassified=!PhotoCollection.IsTypeDirectory(Path.GetFileName(Path.GetDirectoryName(AbsolutePath)));
    }
}

internal sealed class PhotoCollection
{
    public readonly string Root;
    public readonly System.Collections.ObjectModel.ReadOnlyCollection<PhotoItem> Photos;
    public readonly System.Collections.ObjectModel.ReadOnlyCollection<string> Diagnostics;
    public readonly Dictionary<string,List<PhotoItem>> JpgPairs,RawPairs;
    public readonly int PendingCount;
    internal PhotoCollection(string root,IEnumerable<PhotoItem> photos,IEnumerable<string> diagnostics) {
        Root=Normalize(root);Photos=photos.OrderBy(p=>p.RelativePath,StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
        Diagnostics=diagnostics.ToList().AsReadOnly();
        JpgPairs=Photos.Where(p=>p.IsJpg).GroupBy(p=>p.PairKey,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
        RawPairs=Photos.Where(p=>!p.IsJpg).GroupBy(p=>p.PairKey,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
        PendingCount=Photos.Count(p=>p.IsUnclassified);
    }
    internal static string Normalize(string root) {string p=Path.GetFullPath(root);return p.Length>3?p.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar):p;}
    internal static bool IsTypeDirectory(string name) {return new[]{"jpg","jpeg","raw","arw"}.Contains(name,StringComparer.OrdinalIgnoreCase);}
    internal static bool IsArtifact(string name) {return name.StartsWith(".rawmate-gps-",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".rawmate-gps-backup",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".rawmate-gps-transaction",StringComparison.OrdinalIgnoreCase);}
    internal static bool IsExcludedDirectory(string name) {return String.Equals(name,".git",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"test-output",StringComparison.OrdinalIgnoreCase);}
    internal static string Relative(string root,string file) {
        string prefix=Normalize(root).TrimEnd('\\')+"\\",full=Path.GetFullPath(file);
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("照片不在当前集合根目录内。");
        return full.Substring(prefix.Length);
    }
    internal static bool ContainsPath(string root,string file) {
        try {string relative=Relative(root,file);string[] parts=relative.Split('\\');return !parts.Take(parts.Length-1).Any(IsExcludedDirectory)&&!IsArtifact(Path.GetFileName(file));}catch{return false;}
    }
    internal static bool SafePath(string file) {
        try {for(string p=Path.GetFullPath(file);!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
            if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return false;
            return true;}catch{return false;}
    }
    internal static string AbsolutePairKey(string file) {
        string parent=Path.GetDirectoryName(Path.GetFullPath(file));
        if(IsTypeDirectory(Path.GetFileName(parent)))parent=Path.GetDirectoryName(parent);
        return Path.Combine(parent,Path.GetFileNameWithoutExtension(file));
    }
    private static void ReadDirectory(string root,string directory,bool recurse,Stack<string> pending,List<PhotoItem> photos,List<string> diagnostics,CancellationToken cancel) {
        try {
            foreach(string path in Directory.EnumerateFileSystemEntries(directory)) {
                cancel.ThrowIfCancellationRequested();
                try {
                    var attributes=File.GetAttributes(path);
                    if((attributes&FileAttributes.ReparsePoint)!=0){if(diagnostics.Count<100)diagnostics.Add("跳过链接："+path);continue;}
                    if((attributes&FileAttributes.Directory)!=0) {
                        if(recurse&&!IsExcludedDirectory(Path.GetFileName(path)))pending.Push(path);
                    }else if(ContainsPath(root,path)&&(GpsFileSafety.IsJpg(path)||String.Equals(Path.GetExtension(path),".arw",StringComparison.OrdinalIgnoreCase)))photos.Add(new PhotoItem(root,path));
                }catch(OperationCanceledException){throw;}
                catch(Exception e){if(e is IOException||e is UnauthorizedAccessException||e is System.Security.SecurityException){if(diagnostics.Count<100)diagnostics.Add("跳过不可访问项："+path+" ("+e.GetType().Name+")");}else throw;}
            }
        }catch(OperationCanceledException){throw;}
        catch(Exception e){if(e is IOException||e is UnauthorizedAccessException||e is System.Security.SecurityException){if(diagnostics.Count<100)diagnostics.Add("跳过不可访问目录："+directory+" ("+e.GetType().Name+")");}else throw;}
    }
    internal static PhotoCollection Scan(string root,CancellationToken cancel) {
        root=Normalize(root);var photos=new List<PhotoItem>();var diagnostics=new List<string>();var pending=new Stack<string>();
        cancel.ThrowIfCancellationRequested();
        if(!SafePath(root)){diagnostics.Add("集合根目录包含链接，已停止扫描。");return new PhotoCollection(root,photos,diagnostics);}
        pending.Push(root);
        while(pending.Count>0){cancel.ThrowIfCancellationRequested();string directory=pending.Pop();if(!SafePath(directory)){if(diagnostics.Count<100)diagnostics.Add("跳过链接目录："+directory);continue;}ReadDirectory(root,directory,true,pending,photos,diagnostics,cancel);}
        return new PhotoCollection(root,photos,diagnostics);
    }
    // Recheck only the confirmed pair's physical scope, including ambiguous
    // unclassified/type-folder duplicates. Never globally match a filename.
    internal static PhotoCollection ReadPairScope(string root,string file) {
        var item=new PhotoItem(root,file);string relativeScope=Path.GetDirectoryName(item.PairKey)??String.Empty;
        string scope=Path.Combine(Normalize(root),relativeScope);var photos=new List<PhotoItem>();var diagnostics=new List<string>();
        foreach(string directory in new[]{scope}.Concat(new[]{"jpg","jpeg","raw","arw"}.Select(n=>Path.Combine(scope,n)))) {
            if(!Directory.Exists(directory))continue;
            if(!SafePath(directory)){diagnostics.Add("配对范围含链接："+directory);continue;}
            ReadDirectory(root,directory,false,null,photos,diagnostics,CancellationToken.None);
        }
        return new PhotoCollection(root,photos,diagnostics);
    }
}

internal static class GpsFileSafety
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
            WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out HandleInformation information);
    internal static string HandleIdentity(FileStream stream) {
        HandleInformation i;
        if(stream.SafeFileHandle.IsClosed||!GetFileInformationByHandle(stream.SafeFileHandle,out i)||i.Links!=1)throw new IOException("文件 identity / 单链接状态不可验证。");
        return String.Join(":",new[]{i.Volume,i.IndexHigh,i.IndexLow,i.CreationHigh,i.CreationLow,i.WriteHigh,i.WriteLow,i.SizeHigh,i.SizeLow,i.Attributes,i.Links}.Select(n=>n.ToString(CultureInfo.InvariantCulture)));
    }
    internal static string ContentIdentity(FileStream stream) {
        string before=HandleIdentity(stream);stream.Position=0;string hash;
        using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream));
        if(HandleIdentity(stream)!=before)throw new IOException("计算 fingerprint 时文件改变。");return before+"|"+hash;
    }
    internal static string ContentIdentity(string file) {CheckPath(file);using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))return ContentIdentity(stream);}
    // WIC parsing is an additional gate, not proof of full sensor-RAW decoding.
    // Some RAW codecs expose only the preview. Real ARW acceptance also needs the
    // independent data/MakerNotes checks and a copied-sample workflow validation.
    internal static void ValidateParsing(string file)
    {
        CheckPath(file);
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new IOException("WIC 无法解析图像帧。");
            foreach (var frame in decoder.Frames)
            {
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.PixelWidth > 100000 || frame.PixelHeight > 100000 || frame.Format.BitsPerPixel <= 0)
                    throw new IOException("图像尺寸或像素格式无效。");
                int stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
                byte[] pixels = new byte[checked(stride * Math.Min(frame.PixelHeight, 16))];
                for (int y = 0; y < frame.PixelHeight; y += 16)
                    frame.CopyPixels(new Int32Rect(0, y, frame.PixelWidth, Math.Min(16, frame.PixelHeight - y)), pixels, stride, 0);
            }
        }
    }
    internal static string Hash(string file)
    {
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream));
    }
    internal static void CheckPath(string path)
    {
        path = Path.GetFullPath(path);
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 || path.Substring(2).Contains(":"))
            throw new IOException("GPS 写入只支持本地普通文件路径。");
        for (string cursor = path; !String.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("GPS 不允许处理链接文件或链接目录。");
        }
        if (File.Exists(path))
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                HandleInformation information;
                if (!GetFileInformationByHandle(stream.SafeFileHandle, out information) || information.Links != 1)
                    throw new IOException("GPS 不允许处理硬链接或无法验证链接状态的文件。");
            }
        }
    }
    internal static bool Same(string a, string b) { return String.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
    internal static bool IsJpg(string path) { return new[] { ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
    internal static void ValidateJpg(string root, string jpg)
    {
        CheckPath(root); CheckPath(jpg);
        if (!File.Exists(jpg) || !PhotoCollection.ContainsPath(root, jpg) ||
            !(String.Equals(Path.GetExtension(jpg), ".jpg", StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetExtension(jpg), ".jpeg", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("GPS JPG 必须是当前集合根目录内的正式照片。");
    }
}

internal sealed class GpsEditTarget
{
    public readonly string Path, OriginalHash, OriginalIdentity;
    public readonly GpsSnapshot Before;
    internal GpsEditTarget(string path, ExifToolAdapter adapter)
    {
        Path = path; GpsFileSafety.CheckPath(path);
        OriginalIdentity=GpsFileSafety.ContentIdentity(path);OriginalHash = GpsFileSafety.Hash(path); Before = adapter.ReadBaseline(path);
        if (GpsFileSafety.ContentIdentity(path) != OriginalIdentity || GpsFileSafety.Hash(path)!=OriginalHash) throw new IOException("读取期间目标文件已改变。");
    }
}

// Memory-only source; a new root invalidates it before any photo can be edited.
internal sealed class GpsSourceSession
{
    public string Root { get; private set; }
    public GpsMetadata Source { get; private set; }
    public string SourceFileIdentity { get; private set; }
    private string sourceHash;
    internal void SelectRoot(string root)
    {
        string normalized = String.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
        if (normalized != null && normalized.Length > 3) normalized = normalized.TrimEnd('\\');
        if (!String.Equals(Root, normalized, StringComparison.OrdinalIgnoreCase)) Clear();
        Root = normalized;
    }
    internal void Clear() { Source = null; sourceHash = null; SourceFileIdentity=null; }
    internal void SetSource(string jpg, ExifToolAdapter adapter = null)
    {
        if (Root == null) throw new IOException("未选择照片根目录。");
        GpsFileSafety.ValidateJpg(Root, jpg);
        string hash = GpsFileSafety.Hash(jpg);
        string identity=GpsFileSafety.ContentIdentity(jpg);
        GpsMetadata gps;
        if (adapter != null) gps = adapter.Read(jpg).Gps;
        else using (var stream = new FileStream(jpg, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            gps = GpsMetadata.ReadWic(decoder.Frames[0].Metadata as BitmapMetadata, jpg);
        }
        if (gps == null) throw new IOException("当前照片没有有效 GPS 信息，无法设为来源");
        if (hash != GpsFileSafety.Hash(jpg)||identity!=GpsFileSafety.ContentIdentity(jpg)) throw new IOException("来源读取期间已改变。");
        Source = gps; sourceHash = hash;SourceFileIdentity=identity;
    }
    internal bool IsValid()
    {
        if (Source == null) return false;
        try { GpsFileSafety.ValidateJpg(Root, Source.SourcePath); if (sourceHash == GpsFileSafety.Hash(Source.SourcePath)&&SourceFileIdentity==GpsFileSafety.ContentIdentity(Source.SourcePath)) return true; }
        catch { }
        Clear(); return false;
    }
    internal GpsEditPlan CreatePlan(string target, ExifToolAdapter adapter)
    {
        if (!IsValid()) throw new IOException("未设置有效 GPS 来源。");
        return new GpsEditPlan(Root, Source.SourcePath, target, adapter);
    }
}

internal enum GpsSelectionKind { Missing, Existing, Source, Invalid }
internal sealed class GpsSelectionEntry {
    internal string Path,Reason; internal GpsSelectionKind Kind; internal GpsEditPlan Plan;
}
internal sealed class GpsBatchPlan {
    internal readonly string Root,SourcePath,SourceIdentity;
    internal readonly GpsMetadata Source;
    internal readonly System.Collections.ObjectModel.ReadOnlyCollection<GpsSelectionEntry> Entries;
    internal GpsBatchPlan(string root,string source,IEnumerable<string> selected,ExifToolAdapter adapter,Action<int,int> progress=null) {
        Root=Path.GetFullPath(root);SourcePath=Path.GetFullPath(source);GpsFileSafety.ValidateJpg(Root,SourcePath);
        SourceIdentity=GpsFileSafety.ContentIdentity(SourcePath);Source=adapter.ReadBaseline(SourcePath).Gps;
        if(Source==null)throw new IOException("GPS 来源无效。");
        var paths=selected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var entries=new List<GpsSelectionEntry>();
        for(int i=0;i<paths.Length;i++) {
            var entry=new GpsSelectionEntry{Path=paths[i]};entries.Add(entry);
            try {
                GpsFileSafety.ValidateJpg(Root,entry.Path);
                if(GpsFileSafety.Same(entry.Path,SourcePath)){entry.Kind=GpsSelectionKind.Source;entry.Reason="来源本身，永远跳过";}
                else {
                    if((File.GetAttributes(entry.Path)&FileAttributes.ReadOnly)!=0)throw new IOException("JPG 为只读文件。");
                    if(File.Exists(entry.Path+".rawmate-gps-backup")||File.Exists(entry.Path+".rawmate-gps-transaction"))throw new IOException("存在未处理恢复材料。");
                    entry.Plan=new GpsEditPlan(Root,SourcePath,entry.Path,adapter);
                    if(entry.Plan.SourceIdentity!=SourceIdentity)throw new IOException("规划期间来源改变。");
                    entry.Kind=entry.Plan.Targets[0].Before.HasCoordinateFields?GpsSelectionKind.Existing:GpsSelectionKind.Missing;
                }
            } catch(Exception error){entry.Kind=GpsSelectionKind.Invalid;entry.Reason=error.Message;}
            if(GpsFileSafety.ContentIdentity(SourcePath)!=SourceIdentity)throw new IOException("规划期间来源改变，整批取消。");
            if(progress!=null)progress(i+1,paths.Length);
        }
        Entries=entries.AsReadOnly();
    }
    internal int Count(GpsSelectionKind kind){return Entries.Count(e=>e.Kind==kind);}
    internal bool Includes(GpsSelectionEntry entry,bool overwrite){return entry.Kind==GpsSelectionKind.Missing||(overwrite&&entry.Kind==GpsSelectionKind.Existing);}
}
internal sealed class GpsBatchProgress {internal int Index,Total;internal string Path;internal GpsFileResult Result;}
internal sealed class GpsBatchResult {
    internal readonly List<GpsFileResult> Success=new List<GpsFileResult>(),Failed=new List<GpsFileResult>(),Skipped=new List<GpsFileResult>();
}
internal static class GpsBatchExecutor {
    internal static GpsBatchResult Execute(GpsBatchPlan batch,bool overwrite,Func<bool> cancel,Action<GpsBatchProgress> progress,
        Func<GpsSelectionEntry,GpsFileResult> execute) {
        var result=new GpsBatchResult();int index=0;
        foreach(var entry in batch.Entries) {
            index++;var state=new GpsBatchProgress{Index=index,Total=batch.Entries.Count,Path=entry.Path};if(progress!=null)progress(state);
            GpsFileResult item;
            if(entry.Kind==GpsSelectionKind.Invalid){item=new GpsFileResult{Path=entry.Path,Message=entry.Reason};result.Failed.Add(item);}
            else if(!batch.Includes(entry,overwrite)||(cancel!=null&&cancel())) {
                item=new GpsFileResult{Path=entry.Path,Message=entry.Kind==GpsSelectionKind.Source?"GPS 来源自身，已跳过":entry.Kind==GpsSelectionKind.Existing&&!overwrite?"已有 GPS，未授权覆盖":"已取消剩余，未开始写入"};result.Skipped.Add(item);
            } else {
                try {item=execute(entry);if(item==null)throw new IOException("事务没有返回结果。");}
                catch(Exception e){item=new GpsFileResult{Path=entry.Path,Message=e.Message};}
                if(item.Success)result.Success.Add(item);else result.Failed.Add(item);
            }
            state.Result=item;if(progress!=null)progress(state);
        }
        return result;
    }
}
internal static class GpsUxState {
    internal static string Badge(GpsMetadata gps,bool current){return current?"GPS 来源":gps==null?"GPS —":"GPS ✓";}
    internal static bool CanSetSource(GpsMetadata gps,bool current,bool busy){return gps!=null&&!current&&!busy;}
    internal static bool CanApply(bool source,bool current,bool busy){return source&&!current&&!busy;}
    internal static string ToolStatus(bool ready,bool busy,string path){return ready?"可用":busy?"校验中":String.IsNullOrEmpty(path)?"未配置":"校验失败";}
}

internal sealed class GpsEditPlan
{
    public readonly string Root, SourceHash, JpgPath, SourceIdentity;
    internal readonly GpsPerformance Performance = new GpsPerformance();
    public readonly GpsMetadata Source;

    public readonly System.Collections.ObjectModel.ReadOnlyCollection<GpsEditTarget> Targets;
    public readonly string Notice;
    internal GpsEditPlan(string root, string sourceJpg, string targetJpg, ExifToolAdapter adapter)
    {
        var watch=Stopwatch.StartNew();using(Performance.Activate())using(GpsPerformance.Measure("baseline acquisition")) {
        Root = Path.GetFullPath(root); JpgPath = Path.GetFullPath(targetJpg);
        GpsFileSafety.ValidateJpg(Root, sourceJpg); GpsFileSafety.ValidateJpg(Root, JpgPath);
        if (GpsFileSafety.Same(sourceJpg, JpgPath)) throw new IOException("不能把来源 GPS 应用到来源自身。");
        SourceIdentity=GpsFileSafety.ContentIdentity(sourceJpg);SourceHash = GpsFileSafety.Hash(sourceJpg); Source = adapter.ReadBaseline(sourceJpg).Gps;
        if (Source == null) throw new IOException("来源照片没有有效 GPS。");
        if (GpsFileSafety.ContentIdentity(sourceJpg) != SourceIdentity || GpsFileSafety.Hash(sourceJpg)!=SourceHash) throw new IOException("读取期间来源照片已改变。");
        Notice = "仅修改当前 JPG，RAW / XMP 不处理。";
        var targets = new List<GpsEditTarget> { new GpsEditTarget(JpgPath, adapter) };
        Targets = targets.AsReadOnly();
        } Performance.TotalMilliseconds=watch.ElapsedMilliseconds;
    }
    internal void ValidateUnchanged(GpsEditTarget target)
    {
        GpsFileSafety.ValidateJpg(Root, Source.SourcePath); GpsFileSafety.ValidateJpg(Root, JpgPath);
        if (GpsFileSafety.ContentIdentity(Source.SourcePath) != SourceIdentity) throw new IOException("确认后 GPS 来源已改变。");

        if (!Targets.Contains(target)) throw new IOException("目标不属于当前计划。");
        GpsFileSafety.CheckPath(target.Path);
        if (GpsFileSafety.ContentIdentity(target.Path) != target.OriginalIdentity) throw new IOException("确认后目标文件已改变。");
    }
}

internal sealed class GpsFileResult
{
    public string Path, Message, RecoveryPath, Diagnostics;
    public bool Success;
}

internal sealed class GpsFileTransaction
{
    private readonly ExifToolAdapter adapter;
    private readonly Action<string> validateParsing;
    private readonly Action<string, string, string> replaceFile;
    private readonly Action beforeCommit;
    // Real-file validation is intentionally mandatory, never a silent no-op.
    internal GpsFileTransaction(ExifToolAdapter adapter, Action<string> validateParsing, Action<string, string, string> replaceFile = null, Action beforeCommit = null)
    {
        if (validateParsing == null) throw new ArgumentNullException("validateParsing");
        this.adapter = adapter; this.validateParsing = validateParsing;
        this.beforeCommit = beforeCommit;
        this.replaceFile = replaceFile ?? delegate(string temporary, string original, string backup) { File.Replace(temporary, original, backup, false); };
    }
    internal GpsFileResult Execute(GpsEditPlan plan, GpsEditTarget target, bool overwriteConfirmed)
    {
        var watch=Stopwatch.StartNew();GpsFileResult result;
        using(plan.Performance.Activate())result=ExecuteCore(plan,target,overwriteConfirmed);
        plan.Performance.TotalMilliseconds+=watch.ElapsedMilliseconds;result.Diagnostics=plan.Performance.ToString();return result;
    }
    private GpsFileResult ExecuteCore(GpsEditPlan plan, GpsEditTarget target, bool overwriteConfirmed)
    {
        var result = new GpsFileResult { Path = target.Path };
        string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target.Path), ".rawmate-gps-" + Guid.NewGuid().ToString("N") + ".JPG");
        string backup = target.Path + ".rawmate-gps-backup";
        string journal = target.Path + ".rawmate-gps-transaction";
        bool committed = false, ownJournal = false;
        string preparedHash = null;
        try
        {
            if (!plan.Targets.Contains(target) || !GpsFileSafety.IsJpg(target.Path)) throw new IOException("只允许当前计划中的 JPG 事务。");
            if (target.Before.HasCoordinateFields && !overwriteConfirmed) throw new IOException("已有 GPS：需要单独确认覆盖。");
            plan.ValidateUnchanged(target);
            if (File.Exists(backup) || File.Exists(journal)) throw new IOException("发现上次 GPS 事务恢复文件；请先核查恢复，不自动覆盖或删除。");
            using(GpsPerformance.Measure("temp copy")) {
                File.Copy(target.Path, temporary, false);
                if (GpsFileSafety.Hash(temporary) != target.OriginalHash) throw new IOException("临时副本与原件不一致。");
            }
            using(GpsPerformance.Measure("ExifTool write"))adapter.WriteTemporary(temporary, plan.Source);
            GpsSnapshot after;using(GpsPerformance.Measure("post-write metadata read (includes structural/decode gates)"))after=adapter.Read(temporary);
            using(GpsPerformance.Measure("MakerNotes/private validation"))ExifToolAdapter.Verify(target.Before, after, plan.Source);
            // Capture already performed the identical full WIC decode of these
            // exact locked bytes. Keep custom/injected verifier callbacks intact.
            if(validateParsing!=(Action<string>)GpsFileSafety.ValidateParsing)validateParsing(temporary);
            else if(after.Jpeg==null||String.IsNullOrEmpty(after.Jpeg.Decode))throw new IOException("缺少写后 WIC 完整解码证据。");
            preparedHash = GpsFileSafety.Hash(temporary);
            using(GpsPerformance.Measure("pre-commit recheck")) {
                plan.ValidateUnchanged(target);
                if (beforeCommit != null) beforeCommit();
                adapter.ValidateSession();
                // A concurrent change after read/decode cannot use stale proof.
                if(GpsFileSafety.ContentIdentity(temporary)!=after.FileIdentity)throw new IOException("写后验证完成后临时文件改变。");
            }
            // A durable intent record plus File.Replace backup makes interrupted
            // transactions visible. Never delete/move the destination as fallback.
            using (var stream = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownJournal = true;
                byte[] bytes = Encoding.UTF8.GetBytes("RAWMate-GPS|1\n" + target.OriginalHash + "\n" + preparedHash + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(temporary)) + "\n");
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            GpsFileSafety.CheckPath(target.Path);
            // Excludes ordinary concurrent writers while allowing Windows Replace.
            using (var original = new FileStream(target.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var sha = SHA256.Create())
            {
                using(GpsPerformance.Measure("pre-commit recheck"))
                    if (GpsFileSafety.ContentIdentity(original) != target.OriginalIdentity) throw new IOException("提交前原件已改变。");
                using(GpsPerformance.Measure("File.Replace"))replaceFile(temporary, target.Path, backup);
                committed = true;
            }
            using(GpsPerformance.Measure("post-commit verify"))
                if (GpsFileSafety.Hash(target.Path) != preparedHash || GpsFileSafety.Hash(backup) != target.OriginalHash)
                    throw new IOException("替换后内容指纹验证失败。");
            result.Success = true; result.Message = "GPS 写入并验证成功。";
            // Delete only our disposable recovery copies after verified success.
            // Cleanup errors are warnings, not a falsely reported write failure.
            try { File.Delete(backup); File.Delete(journal); ownJournal = false; }
            catch { result.RecoveryPath = File.Exists(backup) ? backup : journal; result.Message += " 恢复记录清理失败，已保留：" + result.RecoveryPath; }
        }
        catch (Exception error)
        {
            result.Message = error.Message;
            // A failed filesystem call may still leave a backup. Recognize only
            // our exact prepared result before attempting recovery; never guess.
            if (!committed && ownJournal && File.Exists(backup) && File.Exists(target.Path))
                try { committed = GpsFileSafety.Hash(backup) == target.OriginalHash && GpsFileSafety.Hash(target.Path) == preparedHash; } catch { }
            if (committed)
            {
                try
                {
                    if (!File.Exists(backup) || GpsFileSafety.Hash(backup) != target.OriginalHash)
                        throw new IOException("原件备份验证失败。");
                    File.Replace(backup, target.Path, null, false);
                    if (GpsFileSafety.Hash(target.Path) != target.OriginalHash) throw new IOException("恢复验证失败。");
                    committed = false;
                    result.Message += " 已恢复原件。";
                }
                catch (Exception recoveryError) { result.RecoveryPath = backup; result.Message += " 恢复未完成：" + recoveryError.Message + "；保留恢复记录：" + journal; }
            }
            if (ownJournal && !committed && !File.Exists(backup))
                try { File.Delete(journal); ownJournal = false; } catch { result.RecoveryPath = journal; }
            if (File.Exists(backup)) result.RecoveryPath = backup;
            else if (ownJournal && File.Exists(journal)) result.RecoveryPath = journal;
        }
        finally
        {
            // Failure evidence is never silently discarded; no automatic recovery.
            if (!result.Success && File.Exists(temporary))
            { result.RecoveryPath = temporary; result.Message += "；失败临时副本保留：" + temporary; }
        }
        return result;
    }
}

internal static class JpgGpsIntegrity
{
    internal sealed class Snapshot {
        public string File,Decode; public byte[] MakerRaw=new byte[0],MPFRaw;
        // Exact EXIF bytes from the identity-bound Capture, not a later path read.
        public byte[] ExifData; public long ExifBase,ExifEnd,FileLength;public int ExifApp1Count;
        public CreateGpsProof Relocation;
        public long MakerOffset; public int HiddenRelative=-1,MPFRelative=-1; public string MPFBlockKey;
        public RefPointer Hidden,MPImage;
        public SortedDictionary<string,Dir> Dirs=new SortedDictionary<string,Dir>(StringComparer.Ordinal);
        public SortedDictionary<string,Span> Blocks=new SortedDictionary<string,Span>(StringComparer.Ordinal);
    }
    internal sealed class Span {
        public string Hash;public long Offset,Length;
        public Span(string key,long offset,long length,byte[] bytes){Offset=offset;Length=length;Hash=H(bytes);}
    }
    static string H(byte[] b) {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(b)).Replace("-","");}
    internal class Entry {
        public int Id,Type,Index; public long Count,Field,Position,Bytes,EntryPosition;public byte[] Data;
        public string Key {get{return "0x"+Id.ToString("x4");}}
    }
    internal class Dir {
        public string Name; public long Position,Next,Base; public List<Entry> Entries=new List<Entry>();
        public Entry Get(int id) {var a=Entries.Where(e=>e.Id==id).ToArray();if(a.Length!=1)throw new IOException(Name+" must have one tag "+id.ToString("x4"));return a[0];}
    }
    class Tiff {
        public byte[] Data; public long Base,End;
        static readonly int[] Sizes={0,1,1,2,4,8,1,1,2,4,8,4,8,4};
        public Tiff(byte[] data,long basis,long end) {Data=data;Base=basis;End=end;Bounds(basis,8);if(U16(basis)!=0x4949||U16(basis+2)!=42)throw new IOException("Only parsed little endian classic TIFF accepted");}
        public void Bounds(long p,long n) {if(p<Base||n<0||p>End||n>End-p)throw new IOException("TIFF data range invalid: "+p+"+"+n+" / "+End);}
        public int U16(long p) {Bounds(p,2);return BitConverter.ToUInt16(Data,checked((int)p));}
        public long U32(long p) {Bounds(p,4);return BitConverter.ToUInt32(Data,checked((int)p));}
        public Dir Read(string name,long relative,bool sony=false) {
            long p=checked(Base+relative);int count=U16(p);Bounds(p,checked(2+12L*count+(sony?0:4)));var d=new Dir{Name=name,Position=p,Base=Base,Next=sony?0:U32(p+2+12L*count)};
            for(int i=0;i<count;i++) {
                long q=p+2+12L*i;int type=U16(q+2);if(type<1||type>=Sizes.Length)throw new IOException("Unknown TIFF type");
                long n=U32(q+4),bytes=checked(n*Sizes[type]),field=U32(q+8),loc=bytes<=4?q+8:checked(Base+field);Bounds(loc,bytes);
                d.Entries.Add(new Entry{Id=U16(q),Type=type,Count=n,Field=field,Position=loc,Bytes=bytes,EntryPosition=q,Index=i,Data=Slice(Data,loc,bytes)});
            }
            if(d.Entries.GroupBy(e=>e.Id).Any(g=>g.Count()!=1))throw new IOException("Duplicate tag in "+name);
            return d;
        }
    }
    internal class RefPointer {
        public string Name,Hash;public long FieldPosition,Stored,Base,Actual,Length;
        public RefPointer(string name,long pos,long stored,long basis,long actual,long length,byte[] data) {
            Range(data,actual,length);if(length<=0||checked(stored+basis)!=actual)throw new IOException(name+" does not point exactly to independently located payload");
            Name=name;FieldPosition=pos;Stored=stored;Base=basis;Actual=actual;Length=length;Hash=H(Slice(data,actual,length));
        }
    }
    static void Range(byte[] b,long p,long n) {if(p<0||n<0||p>b.LongLength||n>b.LongLength-p)throw new IOException("File data range invalid: "+p+"+"+n);}
    static byte[] Slice(byte[] b,long p,long n) {Range(b,p,n);var a=new byte[checked((int)n)];Array.Copy(b,p,a,0,n);return a;}
    static void Add(Snapshot s,string name,long p,long n,byte[] data) {Range(data,p,n);s.Blocks.Add(name,new Span(name,p,n,Slice(data,p,n)));}
    static long[] Numbers(Entry e) {
        if(e.Type!=3&&e.Type!=4&&e.Type!=13)throw new IOException("Expected SHORT/LONG pointer array");
        return Enumerable.Range(0,checked((int)e.Count)).Select(i=>e.Type==3?(long)BitConverter.ToUInt16(e.Data,i*2):(long)BitConverter.ToUInt32(e.Data,i*4)).ToArray();
    }
    static void AddDir(Snapshot s,Dir d) {if(s.Dirs.ContainsKey(d.Name))throw new IOException("Directory cycle/duplicate");s.Dirs.Add(d.Name,d);}
    static void MainDirs(Snapshot s,Tiff t) {
        var d=t.Read("IFD0",t.U32(t.Base+4));AddDir(s,d);
        var exPointer=d.Entries.FirstOrDefault(e=>e.Id==0x8769);
        Dir ex=null;
        if(exPointer!=null) {ex=t.Read("ExifIFD",exPointer.Field);AddDir(s,ex);}
        var gps=d.Entries.FirstOrDefault(e=>e.Id==0x8825);if(gps!=null)AddDir(s,t.Read("GPS",gps.Field));
        if(ex!=null) {
            var inter=ex.Entries.FirstOrDefault(e=>e.Id==0xa005);if(inter!=null)AddDir(s,t.Read("InteropIFD",inter.Field));
            var maker=ex.Entries.FirstOrDefault(e=>e.Id==0x927c);
            if(maker!=null) {
                s.MakerOffset=maker.Position;s.MakerRaw=Slice(t.Data,maker.Position,maker.Bytes);Add(s,"Raw/MakerNotes",maker.Position,maker.Bytes,t.Data);
                if(s.MakerRaw.Length>=12&&Encoding.ASCII.GetString(s.MakerRaw,0,12)=="SONY DSC \0\0\0")
                    AddDir(s,t.Read("SonyMaker",maker.Position+12-t.Base));
            }
        }
        long next=d.Next;int ordinal=1;var seen=new HashSet<long>();
        while(next!=0) {if(!seen.Add(next)||ordinal>4)throw new IOException("IFD cycle");var f=t.Read("IFD"+ordinal++,next);AddDir(s,f);next=f.Next;}
        foreach(var f in s.Dirs.Values.Where(f=>f.Name.StartsWith("IFD",StringComparison.Ordinal))) {
            var offset=f.Entries.FirstOrDefault(e=>e.Id==0x0201);var length=f.Entries.FirstOrDefault(e=>e.Id==0x0202);
            if(offset!=null||length!=null) {
                if(offset==null||length==null||offset.Type!=4||length.Type!=4||offset.Count!=1||length.Count!=1)throw new IOException("Unsupported JPEG payload pointer pair");
                string key=f.Name=="IFD0"?"Raw/PreviewImage":f.Name=="IFD1"?"Raw/ThumbnailImage":"Raw/JpgFromRaw";
                Add(s,key,t.Base+offset.Field,length.Field,t.Data);Decode(Slice(t.Data,t.Base+offset.Field,length.Field));
            }
        }
    }
    static long Locate(byte[] b,long first,byte[] magic,long length,bool jpeg) {
        var candidates=new List<long>();for(long p=first;p+length<=b.LongLength;p++) {
            if(b[p]!=magic[0])continue;bool match=true;for(int j=1;j<magic.Length;j++)if(b[p+j]!=magic[j]){match=false;break;}if(!match)continue;
            if(jpeg) {if(b[p+length-2]!=255||b[p+length-1]!=217)continue;try{Decode(Slice(b,p,length));}catch{continue;}}
            candidates.Add(p);
        }
        if(candidates.Count!=1)throw new IOException("Independent payload locator ambiguous/missing: "+candidates.Count);return candidates[0];
    }
    static void JpgStructure(Snapshot s,byte[] data) {
        long basis=-1,end=-1;int p=2;while(p<data.Length) {if(data[p++]!=255)throw new IOException("JPEG boundary");while(data[p]==255)p++;int m=data[p++];if(m==218||m==217)break;int n=data[p]*256+data[p+1];Range(data,p,n);if(m==225&&n>=8&&Encoding.ASCII.GetString(data,p+2,6)=="Exif\0\0"){if(basis>=0)throw new IOException("Multiple EXIF");basis=p+8;end=p+n;}p+=n;}
        if(basis<0)return;s.ExifBase=basis;s.ExifEnd=end;s.ExifData=Slice(data,0,end);var t=new Tiff(data,basis,end);MainDirs(s,t);
        var hidden=s.Dirs.ContainsKey("SonyMaker")?s.Dirs["SonyMaker"].Entries.FirstOrDefault(e=>e.Id==0x2044):null;
        long trailer=s.Blocks["JPEG/EOI+tail"].Offset+2;
        if(hidden!=null) {
            if(hidden.Type!=4||hidden.Count!=2||hidden.Bytes!=8)throw new IOException("HiddenInfo is not two LONGs");
            long offset=BitConverter.ToUInt32(hidden.Data,0),length=BitConverter.ToUInt32(hidden.Data,4);
            long actual=Locate(data,trailer,new byte[]{0x55,0x26,0x11,0x05,0x00},length,false);
            s.Hidden=new RefPointer("Sony:HiddenDataOffset",hidden.Position,offset,basis,actual,length,data);
            s.HiddenRelative=checked((int)(hidden.Position-s.MakerOffset));Add(s,"Raw/HiddenData",actual,length,data);
        }
        var mpf=s.Blocks.Where(x=>x.Key.StartsWith("JPEG/0xe2/")&&x.Value.Length>8&&Encoding.ASCII.GetString(data,checked((int)x.Value.Offset)+4,4)=="MPF\0").ToArray();if(mpf.Length==0)return;if(mpf.Length!=1)throw new IOException("Expected one MPF APP2");
        s.MPFBlockKey=mpf[0].Key;var span=mpf[0].Value;long mpbase=span.Offset+8;var mt=new Tiff(data,mpbase,span.Offset+span.Length);var md=mt.Read("MPF",mt.U32(mpbase+4));AddDir(s,md);
        var me=md.Get(0xb002);if(me.Type!=7||me.Count!=32)throw new IOException("Expected two MP Entry records");long field=me.Position+16+8,ml=BitConverter.ToUInt32(data,checked((int)(me.Position+16+4))),mo=BitConverter.ToUInt32(data,checked((int)field));
        long ma=Locate(data,trailer,new byte[]{255,216,255},ml,true);s.MPImage=new RefPointer("MPImage2:MPImageStart",field,mo,mpbase,ma,ml,data);s.MPFRaw=Slice(data,span.Offset,span.Length);s.MPFRelative=checked((int)(field-span.Offset));Add(s,"Raw/MPImage2",ma,ml,data);
    }
    static bool EqualExcept(byte[] a,byte[] b,int field) {
        if(a==null||b==null||a.Length!=b.Length||field<0||field>a.Length-4)return false;
        for(int i=0;i<a.Length;i++)if((i<field||i>=field+4)&&a[i]!=b[i])return false;return true;
    }
    static bool Prove(RefPointer b,RefPointer a) {
        return b!=null&&a!=null&&b.Name==a.Name&&b.Length==a.Length&&b.Hash==a.Hash&&b.Stored+b.Base==b.Actual&&a.Stored+a.Base==a.Actual&&
            (a.Stored+a.Base)-(b.Stored+b.Base)==a.Actual-b.Actual;
    }
    internal static Snapshot Capture(string file) {
        if(!GpsFileSafety.IsJpg(file))throw new IOException("Integrity module only accepts JPG");
        byte[] data=File.ReadAllBytes(file);var s=new Snapshot{File=file};
        using(GpsPerformance.Measure("JPEG structural validation"))Jpeg(s,data);
        using(GpsPerformance.Measure("MakerNotes/private structural capture"))JpgStructure(s,data);
        using(GpsPerformance.Measure("WIC full decode"))s.Decode=Decode(data,true);return s;
    }
    internal static HashSet<string> Verify(Snapshot b,Snapshot a,GpsMetadata expected=null) {
        if(b==null||a==null)throw new IOException("Missing actual JPEG structural evidence");
        var proved=new HashSet<string>(StringComparer.Ordinal);var failures=new List<string>();
        bool create=!b.Dirs.ContainsKey("GPS");
        if(create) {
            using(GpsPerformance.Measure("CreateGpsIfd relocation verification"))a.Relocation=CreateGpsIfdPath(b,a,expected,proved);
        } else ExistingGpsIfdPath(b,a,failures,proved);
        if(b.MPImage!=null||a.MPImage!=null) {
            if(!Prove(b.MPImage,a.MPImage)||b.MPFRelative!=a.MPFRelative||b.MPFBlockKey!=a.MPFBlockKey||!EqualExcept(b.MPFRaw,a.MPFRaw,b.MPFRelative))failures.Add("MPImage2/MPF unproved byte or reference change");
            else {proved.Add("MPImage2:MPImageStart");proved.Add("BLOCK:"+b.MPFBlockKey);}
        }
        CompareDirs(b,a,failures,proved,create);
        foreach(var key in b.Blocks.Keys.Union(a.Blocks.Keys)) {
            Span old,now;
            if(!b.Blocks.TryGetValue(key,out old)||!a.Blocks.TryGetValue(key,out now))throw new IOException("Protected JPEG block added/removed: "+key);
            if(old.Length!=now.Length||old.Hash!=now.Hash)if(!proved.Contains("BLOCK:"+key))throw new IOException("JPEG/private payload changed: "+key);
        }
        if(b.Decode!=a.Decode)failures.Add("JPEG decoder/dimensions changed");
        if(failures.Count!=0)throw new IOException(String.Join("; ",failures));return proved;
    }
    // Existing-GPS proof is unchanged: only the independently proved HiddenData
    // field can differ inside MakerNotes, followed by the original common gates.
    static void ExistingGpsIfdPath(Snapshot b,Snapshot a,List<string> failures,HashSet<string> proved) {
        bool rawSame=b.MakerRaw.SequenceEqual(a.MakerRaw);
        if(b.Hidden!=null||a.Hidden!=null) {
            bool valid=Prove(b.Hidden,a.Hidden)&&b.Hidden.Base==a.Hidden.Base&&a.Hidden.Stored-b.Hidden.Stored==a.Hidden.Actual-b.Hidden.Actual;
            if(!valid)failures.Add("HiddenData reference or payload changed");
            else {proved.Add("Sony:HiddenDataOffset");if(b.HiddenRelative==a.HiddenRelative&&EqualExcept(b.MakerRaw,a.MakerRaw,b.HiddenRelative))proved.Add("BLOCK:Raw/MakerNotes");}
        }
        if(!rawSame&&!proved.Contains("BLOCK:Raw/MakerNotes"))failures.Add("Additional/unproved MakerNotes bytes changed");
    }
    internal sealed class RelocationFailure : IOException {
        internal readonly string Category;
        internal RelocationFailure(string category,string message):base("CreateGpsIfd/"+category+": "+message){Category=category;}
    }
    internal sealed class RelocationEntryProof {
        public int Tag,Type,Index;public long Count,Length,BeforeStored,AfterStored,BeforeActual,AfterActual;
        public string Classification,BeforeHash,AfterHash;
    }
    internal sealed class CreateGpsProof {
        public int Ordinary,Nested,Inline,UnexplainedBytes,ChangedBytes,OffsetCells;
        public long MakerDelta,HiddenDelta;public double MakerProofMilliseconds;
        public readonly List<RelocationEntryProof> Entries=new List<RelocationEntryProof>();
    }
    static void Need(bool ok,string category,string why){if(!ok)throw new RelocationFailure(category,why);}
    // Reparse the identity-bound EXIF prefix. The normal Capture's Sony reader
    // predates this path; here Sony's table explicitly has NO TIFF next word.
    sealed class CreateLayout {
        internal Snapshot S;internal Tiff T;internal Dir Sony;internal Entry Maker;
        internal readonly SortedDictionary<string,Dir> Dirs=new SortedDictionary<string,Dir>(StringComparer.Ordinal);
        internal CreateLayout(Snapshot s) {
            S=s;Need(s.ExifApp1Count==1&&s.ExifData!=null,"ENTRY","single EXIF APP1 required");
            try {
                T=new Tiff(s.ExifData,s.ExifBase,s.ExifEnd);Dir root=Read("IFD0",T.U32(T.Base+4));
                Entry ep=root.Get(0x8769);Pointer(ep);Dir ex=Read("ExifIFD",ep.Field);
                Maker=ex.Get(0x927c);Need(Maker.Type==7&&Maker.Bytes>14,"MAKER_SCHEMA","MakerNotes envelope must be UNDEFINED");
                Need(Maker.Position==s.MakerOffset&&Maker.Data.SequenceEqual(s.MakerRaw),"MAKER_BYTES","capture envelope mismatch");
                Need(Maker.Data.Length>=12&&Encoding.ASCII.GetString(Maker.Data,0,12)=="SONY DSC \0\0\0","SONY_HEADER","unparsed Sony header");
                Sony=Read("SonyMaker",Maker.Position+12-T.Base,true);
                Entry gp=root.Entries.FirstOrDefault(e=>e.Id==0x8825);if(gp!=null){Pointer(gp);Read("GPS",gp.Field);}
                Entry inter=ex.Entries.FirstOrDefault(e=>e.Id==0xa005);if(inter!=null){Pointer(inter);Read("InteropIFD",inter.Field);}
                long next=root.Next;int ordinal=1;var seen=new HashSet<long>();
                while(next!=0){Need(ordinal<=4&&seen.Add(next),"DIRECTORY_REFERENCE","IFD cycle/unsupported chain");Dir d=Read("IFD"+ordinal++,next);next=d.Next;}
            } catch(RelocationFailure){throw;} catch(IOException e){throw new RelocationFailure("TIFF_STRUCTURE",e.Message);}
        }
        internal static void Pointer(Entry e){Need(e.Type==4&&e.Count==1,"DIRECTORY_REFERENCE","directory pointer must be LONG[1]");}
        Dir Read(string name,long offset,bool sony=false) {
            Need(!Dirs.ContainsKey(name),"DIRECTORY_REFERENCE","directory duplicate");Dir d=T.Read(name,offset,sony);
            Need(d.Entries.Count>0&&d.Entries.Count<=2048&&d.Entries.All(e=>e.Count>0),"DIRECTORY_SCHEMA","invalid entry/count");Dirs.Add(name,d);return d;
        }
        internal byte[] Cell(Entry e){return Slice(T.Data,e.EntryPosition+8,4);}
        internal long End(Dir d){return d.Position+2+12L*d.Entries.Count+(d.Name=="SonyMaker"?0:4);}
        internal string NextName(Dir d) {
            if(d.Next==0)return "END";var found=Dirs.Values.Where(x=>x.Name!="SonyMaker"&&x.Position==T.Base+d.Next).ToArray();
            Need(found.Length==1,"DIRECTORY_REFERENCE","dangling/ambiguous next IFD");return found[0].Name;
        }
        internal Dictionary<int,long> Pool() {
            long cursor=End(Sony),end=Maker.Position+Maker.Bytes;var result=new Dictionary<int,long>();
            Need(cursor<=end,"POOL_LAYOUT","Sony table outside envelope");
            foreach(Entry e in Sony.Entries) {
                if(e.Bytes<=4)continue;long actual=cursor;cursor=checked(cursor+e.Bytes);
                Need(cursor<=end&&actual==e.Position&&T.Base+e.Field==actual,"POOL_LAYOUT","gap/overlap/wrong typed offset");result.Add(e.Id,actual);
            }
            Need(cursor==end,"POOL_LAYOUT","pool must consume envelope without gaps");return result;
        }
    }
    sealed class OwnedRange {
        internal long Start,End;internal string Owner;
        internal OwnedRange(long start,long end,string owner){Start=start;End=end;Owner=owner;}
    }
    static string ExifRangeLedger(CreateLayout l) {
        var ranges=new List<OwnedRange>{new OwnedRange(l.T.Base,l.T.Base+8,"TIFF/header")};
        foreach(Dir d in l.Dirs.Values) {
            if(d.Name=="SonyMaker")continue;ranges.Add(new OwnedRange(d.Position,l.End(d),d.Name+"/directory"));
            foreach(Entry e in d.Entries)if(e.Bytes>4)ranges.Add(new OwnedRange(e.Position,e.Position+e.Bytes,d.Name+"/"+e.Key));
            Entry offset=d.Entries.FirstOrDefault(e=>e.Id==0x0201),length=d.Entries.FirstOrDefault(e=>e.Id==0x0202);
            if(offset!=null||length!=null) {
                Need(offset!=null&&length!=null,"THUMBNAIL_REFERENCE","missing offset/length pair");CreateLayout.Pointer(offset);CreateLayout.Pointer(length);
                long lo=l.T.Base+offset.Field;l.T.Bounds(lo,length.Field);ranges.Add(new OwnedRange(lo,lo+length.Field,d.Name+"/JPEG-payload"));
            }
        }
        ranges=ranges.OrderBy(x=>x.Start).ThenBy(x=>x.End).ToList();long cursor=l.T.Base;string owner="START";var gaps=new List<string>();
        foreach(var r in ranges) {
            Need(r.Start>=cursor&&r.End>=r.Start&&r.End<=l.T.End,"EXIF_RANGE","overlap/out-of-range ownership");
            if(r.Start>cursor) {
                Need(r.Start-cursor==1&&cursor%2==1&&r.Start%2==0&&l.T.Data[checked((int)cursor)]==0,"EXIF_PADDING","unexplained gap/padding");
                gaps.Add(owner+">"+r.Owner+":1:00");
            }
            cursor=r.End;owner=r.Owner;
        }
        Need(cursor==l.T.End||cursor+1==l.T.End&&cursor%2==1&&l.T.End%2==0&&l.T.Data[checked((int)cursor)]==0,"EXIF_PADDING","unexplained terminal alignment");
        return String.Join("|",gaps);
    }
    static void SameSchema(Dir b,Dir a) {
        Need(b.Entries.Count==a.Entries.Count&&b.Entries.Zip(a.Entries,(x,y)=>x.Id==y.Id&&x.Type==y.Type&&x.Count==y.Count).All(x=>x),
            b.Name=="SonyMaker"?"SONY_SCHEMA":"PUBLIC_SCHEMA",b.Name+" ID/type/count/order changed");
    }
    static void NewGpsSchema(CreateLayout a,GpsMetadata expected) {
        Need(a.Dirs.ContainsKey("GPS"),"GPS_SCHEMA","missing new GPS IFD");Dir d=a.Dirs["GPS"];
        Need(d.Next==0,"GPS_REFERENCE","GPS next IFD must be zero");
        Need(d.Entries.Select(e=>e.Id).OrderBy(x=>x).SequenceEqual(new[]{0,1,2,3,4,9}),"GPS_SCHEMA","new GPS tags must be exactly approved six");
        Entry ver=d.Get(0);Need(ver.Type==1&&ver.Count==4&&ver.Data.SequenceEqual(new byte[]{2,3,0,0}),"GPS_SCHEMA","GPSVersionID");
        foreach(int id in new[]{1,3,9}) {Entry e=d.Get(id);Need(e.Type==2&&e.Count==2&&e.Data[1]==0&&a.Cell(e).SequenceEqual(new[]{e.Data[0],(byte)0,(byte)0,(byte)0}),"GPS_SCHEMA","ASCII schema/padding");}
        string lat=Encoding.ASCII.GetString(d.Get(1).Data,0,1),lon=Encoding.ASCII.GetString(d.Get(3).Data,0,1);
        Need((lat=="N"||lat=="S")&&(lon=="E"||lon=="W")&&d.Get(9).Data[0]==(byte)'A',"GPS_VALUE","refs/status");
        Func<int,string,bool,double> coordinate=(id,reference,latitude)=> {
            Entry e=d.Get(id);Need(e.Type==5&&e.Count==3,"GPS_SCHEMA","coordinate must be RATIONAL[3]");
            var values=new ulong[3];for(int i=0;i<3;i++)values[i]=(ulong)BitConverter.ToUInt32(e.Data,i*8)|((ulong)BitConverter.ToUInt32(e.Data,i*8+4)<<32);
            double? value=GpsMetadata.FromDms(values,reference,latitude);Need(value.HasValue,"GPS_VALUE","invalid DMS");return value.Value;
        };
        var actual=new GpsMetadata(coordinate(2,lat,true),coordinate(4,lon,false),null,null,"");
        Need(expected==null||expected.SameCoordinates(actual),"GPS_VALUE","raw coordinate differs from plan");
        Need(expected==null||lat==(expected.Latitude<0?"S":"N")&&lon==(expected.Longitude<0?"W":"E"),"GPS_VALUE","reference differs from plan");
    }
    static void PublicExifProof(CreateLayout b,CreateLayout a,GpsMetadata expected,HashSet<string> proved) {
        Need(!b.Dirs.ContainsKey("GPS")&&!b.Dirs["IFD0"].Entries.Any(e=>e.Id==0x8825),"ENTRY","before must have no GPS IFD/reference");
        Need(a.Dirs.Keys.Where(k=>k!="GPS").SequenceEqual(b.Dirs.Keys),"PUBLIC_SCHEMA","only GPS directory may be added");NewGpsSchema(a,expected);
        foreach(Dir old in b.Dirs.Values) {
            if(old.Name=="SonyMaker")continue;Dir now=a.Dirs[old.Name];var filtered=new Dir{Name=now.Name,Entries=now.Entries};
            if(old.Name=="IFD0") {
                Entry p=now.Get(0x8825);CreateLayout.Pointer(p);Need(a.T.Base+p.Field==a.Dirs["GPS"].Position,"GPS_REFERENCE","GPSInfo exact reference");
                filtered=new Dir{Name=now.Name,Entries=now.Entries.Where(e=>e.Id!=0x8825).ToList()};
            }
            SameSchema(old,filtered);Need(b.NextName(old)==a.NextName(now),"DIRECTORY_REFERENCE","next target changed");
            for(int i=0;i<old.Entries.Count;i++) {
                Entry x=old.Entries[i],y=filtered.Entries[i];if(old.Name=="ExifIFD"&&x.Id==0x927c)continue;
                string target=old.Name=="IFD0"&&x.Id==0x8769?"ExifIFD":old.Name=="ExifIFD"&&x.Id==0xa005?"InteropIFD":null;
                if(target!=null){Need(b.T.Base+x.Field==b.Dirs[target].Position&&a.T.Base+y.Field==a.Dirs[target].Position,"DIRECTORY_REFERENCE","exact directory reference");continue;}
                if(old.Name.StartsWith("IFD",StringComparison.Ordinal)&&x.Id==0x0201) {
                    Entry xl=old.Get(0x0202),yl=now.Get(0x0202);Need(xl.Field==yl.Field,"THUMBNAIL_REFERENCE","JPEG length changed");
                    Need(Slice(b.T.Data,b.T.Base+x.Field,xl.Field).SequenceEqual(Slice(a.T.Data,a.T.Base+y.Field,yl.Field)),"THUMBNAIL_PAYLOAD","JPEG payload changed");
                    proved.Add(old.Name=="IFD0"?"IFD0:PreviewImageStart":old.Name=="IFD1"?"IFD1:ThumbnailOffset":old.Name+":JpgFromRawStart");continue;
                }
                Need(x.Data.SequenceEqual(y.Data),"PUBLIC_VALUE",old.Name+"/"+x.Key+" unauthorized value");
                if(x.Bytes<=4)Need(b.Cell(x).SequenceEqual(a.Cell(y)),"PUBLIC_PADDING","unused inline bytes changed");
            }
        }
        Need(ExifRangeLedger(b)==ExifRangeLedger(a),"EXIF_PADDING","alignment ownership changed");proved.Add("NEW_GPS_IFD");
    }
    static CreateGpsProof CreateGpsIfdPath(Snapshot before,Snapshot after,GpsMetadata expected,HashSet<string> proved) {
        Need(before.ExifBase==after.ExifBase,"ENTRY","TIFF base changed");var b=new CreateLayout(before);var a=new CreateLayout(after);SameSchema(b.Sony,a.Sony);
        var proof=new CreateGpsProof();var timer=Stopwatch.StartNew();
        using(GpsPerformance.Measure("MakerNotes byte proof")) {
            Need(b.Maker.Bytes==a.Maker.Bytes,"MAKER_SCHEMA","envelope length changed");var bp=b.Pool();var ap=a.Pool();var mask=new bool[before.MakerRaw.Length];
            Action<long> own=p=>{Need(p>=0&&p<=mask.Length-4,"MAKER_BYTES","offset cell outside envelope");for(int j=0;j<4;j++)mask[checked((int)p)+j]=true;proof.OffsetCells++;};
            Need(bp.ContainsKey(0x2044)&&ap.ContainsKey(0x2044),"HIDDEN_SCHEMA","candidate requires parsed HiddenInfo");
            Entry hb=b.Sony.Get(0x2044),ha=a.Sony.Get(0x2044);
            Need(hb.Type==4&&hb.Count==2&&hb.Bytes==8&&ha.Type==4&&ha.Count==2&&ha.Bytes==8,"HIDDEN_SCHEMA","HiddenInfo must be LONG[2]");
            long bs=BitConverter.ToUInt32(hb.Data,0),ns=BitConverter.ToUInt32(ha.Data,0),bl=BitConverter.ToUInt32(hb.Data,4),nl=BitConverter.ToUInt32(ha.Data,4);
            Need(bl==nl&&bl>0&&hb.Data.Skip(4).SequenceEqual(ha.Data.Skip(4)),"HIDDEN_PAYLOAD","HiddenInfo length LONG changed");
            RefPointer bh=before.Hidden,ah=after.Hidden;
            Need(bh!=null&&ah!=null&&bh.Stored==bs&&ah.Stored==ns&&bh.Base==b.T.Base&&ah.Base==a.T.Base&&bh.Actual==bs+b.T.Base&&ah.Actual==ns+a.T.Base&&
                bh.FieldPosition==bp[0x2044]&&ah.FieldPosition==ap[0x2044]&&bh.Length==bl&&ah.Length==nl&&
                bh.Actual>=before.Blocks["JPEG/EOI+tail"].Offset+2&&ah.Actual>=after.Blocks["JPEG/EOI+tail"].Offset+2&&
                bl<=before.FileLength-bh.Actual&&nl<=after.FileLength-ah.Actual,"HIDDEN_REFERENCE","exact independent trailer reference/range");
            Need(bh.Hash==ah.Hash,"HIDDEN_PAYLOAD","complete HiddenData raw SHA256 changed");
            Need(ns-bs==ah.Actual-bh.Actual,"HIDDEN_REFERENCE","nested delta mismatch");proof.HiddenDelta=ah.Actual-bh.Actual;
            for(int i=0;i<b.Sony.Entries.Count;i++) {
                Entry x=b.Sony.Entries[i],y=a.Sony.Entries[i];Need(x.EntryPosition-b.Maker.Position==y.EntryPosition-a.Maker.Position,"SONY_SCHEMA","relative entry position changed");
                if(x.Bytes<=4){Need(b.Cell(x).SequenceEqual(a.Cell(y)),"MAKER_BYTES","inline cell/padding changed");proof.Inline++;continue;}
                long bo=bp[x.Id],ao=ap[y.Id];Need(x.Bytes==y.Bytes&&ao-a.Maker.Position==bo-b.Maker.Position&&y.Field-x.Field==ao-bo,"POOL_LAYOUT","typed payload relocation delta/relative location");
                var entry=new RelocationEntryProof{Tag=x.Id,Type=x.Type,Index=x.Index,Count=x.Count,Length=x.Bytes,BeforeStored=x.Field,AfterStored=y.Field,BeforeActual=bo,AfterActual=ao,BeforeHash=H(x.Data),AfterHash=H(y.Data)};
                own(x.EntryPosition+8-b.Maker.Position);
                if(x.Id==0x2044){own(bo-b.Maker.Position);entry.Classification="NESTED_PROVEN_RELOCATION";proof.Nested++;}
                else {Need(x.Data.SequenceEqual(y.Data)&&entry.BeforeHash==entry.AfterHash,"PRIVATE_PAYLOAD",x.Key+" raw payload changed");entry.Classification="PROVEN_RELOCATION";proof.Ordinary++;}
                proof.Entries.Add(entry);
            }
            for(int i=0;i<mask.Length;i++)if(before.MakerRaw[i]!=after.MakerRaw[i]){proof.ChangedBytes++;if(!mask[i])proof.UnexplainedBytes++;}
            Need(proof.UnexplainedBytes==0,"MAKER_BYTES","unexplained byte count "+proof.UnexplainedBytes);proof.MakerDelta=a.Maker.Position-b.Maker.Position;
        }
        proof.MakerProofMilliseconds=timer.Elapsed.TotalMilliseconds;PublicExifProof(b,a,expected,proved);
        proved.Add("Sony:HiddenDataOffset");proved.Add("BLOCK:Raw/MakerNotes");return proof;
    }
    static void CompareDirs(Snapshot b,Snapshot a,List<string> failures,HashSet<string> proved,bool create=false) {
        foreach(string k in b.Dirs.Keys.Union(a.Dirs.Keys)) {
            // New path accounts for every EXIF byte/schema itself. MPF is outside
            // EXIF and retains its original independent/common proof.
            if(create&&k!="MPF")continue;
            if(!b.Dirs.ContainsKey(k)||!a.Dirs.ContainsKey(k)){
                if(k=="GPS"&&!b.Dirs.ContainsKey(k)&&a.Dirs[k].Entries.All(e=>new[]{0,1,2,3,4,9}.Contains(e.Id))){proved.Add("NEW_GPS_IFD");continue;}
                if(k=="IFD0"&&b.Dirs.Count==0&&a.Dirs[k].Entries.All(e=>e.Id==0x8825))continue;
                failures.Add("Directory added/removed "+k);continue;}var x=b.Dirs[k];var y=a.Dirs[k];
            foreach(int id in x.Entries.Select(e=>e.Id).Union(y.Entries.Select(e=>e.Id))) {
                bool gps=k=="GPS"&&new[]{1,2,3,4,9}.Contains(id);var e=x.Entries.FirstOrDefault(z=>z.Id==id);var f=y.Entries.FirstOrDefault(z=>z.Id==id);if(gps)continue;
                if(k=="IFD0"&&id==0x8825&&e==null&&f!=null&&a.Dirs.ContainsKey("GPS"))continue;
                if(e==null||f==null||e.Type!=f.Type||e.Count!=f.Count){failures.Add("Unauthorized directory schema change "+k+"/"+id.ToString("x4"));continue;}
                if(e.Data.SequenceEqual(f.Data))continue;
                bool allow=false;
                // Actual typed directory-reference tags, never an arbitrary offset-looking value.
                string destination=null;if(k=="IFD0"&&id==0x8769)destination="ExifIFD";if(k=="IFD0"&&id==0x8825)destination="GPS";if(k=="ExifIFD"&&id==0xa005)destination="InteropIFD";
                if(destination!=null)allow=e.Field+x.Base==b.Dirs[destination].Position&&f.Field+y.Base==a.Dirs[destination].Position;
                if(k=="ExifIFD"&&id==0x927c)allow=proved.Contains("BLOCK:Raw/MakerNotes");
                if(k=="SonyMaker"&&id==0x2044)allow=proved.Contains("BLOCK:Raw/MakerNotes");
                if(k=="MPF"&&id==0xb002)allow=proved.Contains("BLOCK:"+b.MPFBlockKey);
                if(new[]{"IFD0","IFD1","IFD2"}.Contains(k)&&id==0x0201) {
                    string block=k=="IFD0"?"Raw/PreviewImage":k=="IFD1"?"Raw/ThumbnailImage":"Raw/JpgFromRaw";var old=b.Blocks[block];var now=a.Blocks[block];allow=old.Hash==now.Hash&&old.Length==now.Length&&e.Field+x.Base==old.Offset&&f.Field+y.Base==now.Offset;
                    if(allow)proved.Add(k=="IFD0"?"IFD0:PreviewImageStart":k=="IFD1"?"IFD1:ThumbnailOffset":"IFD2:JpgFromRawStart");
                }
                if(!allow){failures.Add("Unauthorized raw tag data change "+k+"/"+e.Key);}
            }
            if(k=="SonyMaker"&&!x.Entries.Select(e=>e.Id).SequenceEqual(y.Entries.Select(e=>e.Id))){failures.Add("SonyMaker directory reordered");}
            // Followed next-pointer chains must preserve their directory identities, not numeric offsets.
            Func<Snapshot,Dir,string> next=(s,d)=>d.Next==0?"END":String.Join("|",s.Dirs.Values.Where(v=>v.Base==d.Base&&v.Position==d.Base+d.Next).Select(v=>v.Name));
            if(next(b,x)!=next(a,y)||x.Next!=0&&next(b,x)==""){failures.Add("Next IFD target changed/dangling "+k);}
        }
    }
    static string Decode(byte[] bytes,bool main=false) {
        if(GpsPerformance.Current!=null){if(main)GpsPerformance.Current.MainDecodes++;else GpsPerformance.Current.EmbeddedDecodes++;}
        using(var s=new MemoryStream(bytes)) {var d=BitmapDecoder.Create(s,BitmapCreateOptions.PreservePixelFormat|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad);if(d.Frames.Count==0)throw new IOException("No image frame");foreach(var f in d.Frames){
            if(f.PixelWidth<=0||f.PixelHeight<=0||f.PixelWidth>100000||f.PixelHeight>100000||f.Format.BitsPerPixel<=0)throw new IOException("WIC dimensions/format invalid");
            int stride=checked((f.PixelWidth*f.Format.BitsPerPixel+7)/8);byte[] pixels=new byte[checked(stride*16)];for(int y=0;y<f.PixelHeight;y+=16)f.CopyPixels(new Int32Rect(0,y,f.PixelWidth,Math.Min(16,f.PixelHeight-y)),pixels,stride,0);}return d.CodecInfo.FriendlyName+"; "+d.Frames[0].PixelWidth+"x"+d.Frames[0].PixelHeight+"; FULL CopyPixels";}
    }
    static void Jpeg(Snapshot snap,byte[] data) {
        snap.FileLength=data.LongLength;
        if(data.Length<4||data[0]!=255||data[1]!=216)throw new IOException("Not JPEG");
        int p=2,scan=0;var occurrence=new Dictionary<int,int>();snap.Blocks.Add("JPEG/SOI",new Span("JPEG/SOI",0,2,data.Take(2).ToArray()));
        while(p<data.Length) {
            int start=p;if(data[p++]!=255)throw new IOException("Invalid JPEG marker boundary");while(p<data.Length&&data[p]==255)p++;if(p>=data.Length)throw new IOException("Truncated JPEG");int marker=data[p++];
            if(marker==217) {string key="JPEG/EOI+tail";snap.Blocks.Add(key,new Span(key,start,data.Length-start,data.Skip(start).ToArray()));return;}
            if(marker==0||marker==216||marker>=208&&marker<=215)throw new IOException("Unexpected standalone JPEG marker");
            if(p+2>data.Length)throw new IOException("Truncated JPEG length");int length=data[p]*256+data[p+1],end=checked(p+length);if(length<2||end>data.Length)throw new IOException("JPEG segment out of bounds");
            int count=occurrence.ContainsKey(marker)?++occurrence[marker]:occurrence[marker]=1;string id="JPEG/0x"+marker.ToString("x2")+"/"+count;
            bool exif=marker==225&&length>=8&&Encoding.ASCII.GetString(data,p+2,6)=="Exif\0\0";
            if(exif)snap.ExifApp1Count++;
            if(!exif)snap.Blocks.Add(id,new Span(id,start,end-start,data.Skip(start).Take(end-start).ToArray()));
            p=end;
            if(marker==218) {
                int entropy=p;while(p<data.Length){if(data[p++]!=255)continue;int ff=p-1;while(p<data.Length&&data[p]==255)p++;if(p>=data.Length)throw new IOException("JPEG scan missing terminator");int m=data[p];if(m==0||m>=208&&m<=215){p++;continue;}p=ff;break;}
                string k="JPEG/entropy/"+(++scan);snap.Blocks.Add(k,new Span(k,entropy,p-entropy,data.Skip(entropy).Take(p-entropy).ToArray()));
            }
        }
        throw new IOException("JPEG EOI not found");
    }
}

internal sealed class MainWindow : Window
{
    private sealed class RecycleResult
    {
        public readonly List<string> SuccessPaths = new List<string>();
        public readonly List<string> FailedPaths = new List<string>();
        public readonly List<string> Notes = new List<string>();
    }

    private TextBox folderBox = new TextBox();
    private Button folderHistoryButton = new Button();
    private WrapPanel gallery = new WrapPanel();
    private ScrollViewer galleryScrollViewer = new ScrollViewer();
    private TextBlock status = new TextBlock();
    private TextBlock folderHint = new TextBlock();
    private TextBlock jpgCount = new TextBlock();
    private TextBlock rawCount = new TextBlock();
    private TextBlock pendingCount = new TextBlock();
    private TextBlock cullProgressCount = new TextBlock();
    private TextBlock cullProgressPercent = new TextBlock();
    private TextBlock pickedProgressCount = new TextBlock();
    private TextBlock rejectedProgressCount = new TextBlock();
    private ColumnDefinition cullProgressFillColumn = new ColumnDefinition();
    private ColumnDefinition cullProgressRemainingColumn = new ColumnDefinition();
    private TextBlock galleryCaption = new TextBlock();
    private TextBlock photoInfoTitle = new TextBlock();
    private TextBlock photoInfo = new TextBlock();
    private TextBlock gpsInfo = new TextBlock();
    private StackPanel gpsSourceBar=new StackPanel();
    private Grid gpsGridActions=new Grid();
    private TextBlock gpsSourceText=new TextBlock();
    private TextBlock gpsSingleSourceCoordinates=new TextBlock(),gpsSingleCurrentSource=new TextBlock();
    private Border gpsSingleSourceDivider=new Border();
    private Button gpsClearButton=new Button(),gpsSetButton=new Button(),gpsApplyButton=new Button(),gpsGridSetButton=new Button(),gpsGridApplyButton=new Button();
    private TextBlock gpsSettingsStatus;
    private Button gpsSettingsChoose;
    private string gpsActivity;
    private FileSystemWatcher gpsSourceWatcher;
    private DispatcherTimer gpsSourcePoll;
    private sealed class GpsDisplay {internal string Stamp;internal GpsMetadata Gps;internal bool ImageIdentityChanged;}
    private readonly Dictionary<string,GpsDisplay> gpsDisplayCache=new Dictionary<string,GpsDisplay>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Border,string> gpsBadges=new Dictionary<Border,string>();
    private readonly HashSet<string> gpsDisplayPending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private GpsSourceSession gpsSession = new GpsSourceSession();
    private int gpsDisplayGeneration;
    private bool gpsDirectoryRefreshing;
    // A watcher may invalidate coordinates before Refresh is clicked. Keep only
    // the explicitly chosen path for re-reading, never the invalid coordinates.
    private string gpsRefreshSourcePath;
    private ExifToolAdapter gpsAdapter;
    private string gpsToolPath, gpsToolNotice = "未配置 ExifTool，GPS 写入不可用";
    private bool gpsBusy;
    private int gpsContextEpoch;
    private readonly Dictionary<string,int> imagePathEpochs = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> thumbnailStamps = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    private Button organizeButton = new Button();
    private Button unorganizeButton = new Button();
    private Button recycleSelectedButton = new Button();
    private Button recycleRejectedButton = new Button();
    private Button filterButton = new Button();
    private Image singleImage = new Image();
    private ScrollViewer singleViewer = new ScrollViewer();
    private TextBlock singleCaption = new TextBlock();
    private Border singleCullBadge = new Border();
    private TextBlock singleCullBadgeText = new TextBlock();
    private Button advanceTargetButton = new Button();
    private TextBox zoomBox = new TextBox();
    private Canvas navigatorCanvas = new Canvas();
    private Image navigatorImage = new Image();
    private Border navigatorViewport = new Border();
    private bool navigatorDragging;
    private bool navigatorUpdateQueued;
    private StackPanel filmStrip = new StackPanel();
    private ScrollViewer filmStripScrollViewer = new ScrollViewer();
    private readonly Dictionary<Border, string> tileFiles = new Dictionary<Border, string>();
    private readonly Dictionary<Border, string> filmTiles = new Dictionary<Border, string>();
    private readonly Dictionary<Border, string> lazyFilmThumbnailFiles = new Dictionary<Border, string>();
    private readonly HashSet<Border> loadingFilmThumbnailTiles = new HashSet<Border>();
    private readonly Dictionary<Border, string> lazyThumbnailFiles = new Dictionary<Border, string>();
    private readonly HashSet<Border> loadingThumbnailTiles = new HashSet<Border>();
    private readonly HashSet<string> selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> galleryFiles = new List<string>();
    private PhotoCollection collection;
    private bool collectionUiEnabled,collectionScanning,restoringCollectionSession;
    private int collectionGeneration;
    private CancellationTokenSource collectionCancellation;
    private bool classificationBusy;
    private readonly HashSet<string> pickedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rejectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> jpgOnlyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rawOnlyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // Completed RAW-only decisions are anchored to the surviving RAW, not a deleted JPG.
    private readonly HashSet<string> retainedRawFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool cullMarksReadFailed;
    private Border selectionAnchor;
    private string currentSingleFile;
    private bool singleViewMode;
    private bool advanceToUndecided = true;
    private string navigationRoot;
    private bool buildingInitialShell;
    private bool fitSingleImage = true;
    private double singleZoom = 1.0;
    private Point singlePanStart;
    private double singlePanHorizontal;
    private double singlePanVertical;
    private bool singlePanning;
    private int singleLoadVersion;
    private int galleryThumbnailLoadVersion;
    private bool thumbnailLoadQueued;
    private int filmThumbnailLoadVersion;
    private bool filmThumbnailLoadQueued;
    private readonly object fullImageCacheLock = new object();
    private readonly Dictionary<string, BitmapSource> fullImageCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<BitmapSource>> fullImageLoadTasks = new Dictionary<string, Task<BitmapSource>>(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> fullImageCacheLru = new LinkedList<string>();
    private readonly object thumbnailCacheLock = new object();
    private readonly Dictionary<string, Tuple<int, BitmapSource>> thumbnailCache = new Dictionary<string, Tuple<int, BitmapSource>>(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> thumbnailCacheLru = new LinkedList<string>();
    private readonly object captureDateCacheLock = new object();
    private readonly Dictionary<string, DateTime?> captureDateCache = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> captureDateStampCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private int fullImageCacheGeneration;
    private readonly List<string> recentFolders = new List<string>();
    private bool forgetFolderHistoryUntilNextScan;
    private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location).EndsWith("-Test", StringComparison.OrdinalIgnoreCase) ? "RAWMate-Test-1.4" : "RAWMate");
    // Thumbnails are derived, disposable data. Sharing this cache between the formal and
    // test executables avoids decoding the same camera files again when comparing builds.
    private static readonly string ThumbnailCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate", "thumbnail-cache-v1");
    private static readonly string CaptureDateCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate", "capture-date-cache-v1.txt");
    private readonly string settingsPath = Path.Combine(StateDirectory, "settings.txt");
    private readonly string cullMarksPath = Path.Combine(StateDirectory, "cull-marks-v2.txt");
    private string activeFilter = "all";
    private string lastFolder;
    private string gallerySortMode = "name_asc";
    private double gridTileWidth = 196;
    private static readonly bool darkMode = true;
    private const double DefaultWindowWidth = 1380;
    private const double DefaultWindowHeight = 860;
    private const double PreferredMinimumWindowWidth = 1000;
    private const double PreferredMinimumWindowHeight = 640;
    private const double SidebarWidth = 320;
    private const int MaxThumbnailCacheItems = 512;
    private const int PersistentThumbnailWidth = 384;
    // The sidebar column also contains outer margins, border and padding; 280 DIPs
    // matches the original usable content width so large screens are not rescaled.
    private const double SidebarContentWidth = 280;
    private const double HeaderControlHeight = 28;
    private const double HeaderControlGap = 8;
    private const string ButtonBlue = "#436794";
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private static readonly string[] JpgExtensions = { ".jpg", ".jpeg" };
    private static readonly string[] RawExtensions = { ".arw" };

    public MainWindow()
    {
        Title = "RAWMate";
        ApplyInitialWindowSize();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        SourceInitialized += delegate { ApplyDarkTitleBar(); };
        LoadSettings();
        LoadCullMarks();
        LoadCaptureDateCache();
        collectionUiEnabled=true;
        if (!String.IsNullOrWhiteSpace(lastFolder)) folderBox.Text = lastFolder;
        Background = Brush("#171B21");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        try { Icon = BitmapFrame.Create(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RAWMate.ico"), UriKind.Absolute)); } catch { }
        // Always paint a lightweight shell first. Restoring a saved single-photo
        // session can require directory sorting and EXIF reads, so defer that work
        // until after the window has become visible.
        buildingInitialShell = true;
        Content = BuildUi();
        buildingInitialShell = false;
        SetStatus("选择一个相机照片目录，然后扫描或创建分类。", false);
        PreviewKeyDown += MainWindowPreviewKeyDown;
        Loaded += delegate
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RestoreSession));
            if (!String.IsNullOrEmpty(gpsToolPath)) ConfigureGpsTool(gpsToolPath);
        };
        Closing += delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(gpsBusy||classificationBusy){e.Cancel=true;SetStatus("文件操作正在安全处理，请先取消剩余并等待当前文件完成。",true);return;} Interlocked.Increment(ref gpsContextEpoch); SaveSettings(); SaveCullMarks(); SaveCaptureDateCache(); };
        Closed += delegate { Interlocked.Increment(ref collectionGeneration);if(collectionCancellation!=null)collectionCancellation.Cancel();if(gpsAdapter!=null){gpsAdapter.Dispose();gpsAdapter=null;} };
        Closed += delegate { if(gpsSourceWatcher!=null)gpsSourceWatcher.Dispose();if(gpsSourcePoll!=null)gpsSourcePoll.Stop(); };
    }

    private void ApplyInitialWindowSize()
    {
        // WPF reports the work area in device-independent units, so this also accounts
        // for Windows display scaling (for example 1920x1080 at 150% is about 1280x720).
        var workArea = SystemParameters.WorkArea;
        var availableWidth = workArea.Width > 0 ? workArea.Width : DefaultWindowWidth;
        var availableHeight = workArea.Height > 0 ? workArea.Height : DefaultWindowHeight;
        const double edgeAllowance = 8;
        availableWidth = Math.Max(1, availableWidth - edgeAllowance);
        availableHeight = Math.Max(1, availableHeight - edgeAllowance);

        MinWidth = Math.Min(PreferredMinimumWindowWidth, availableWidth);
        MinHeight = Math.Min(PreferredMinimumWindowHeight, availableHeight);
        Width = Math.Min(DefaultWindowWidth, availableWidth);
        Height = Math.Min(DefaultWindowHeight, availableHeight);
    }

    private void ApplyDarkTitleBar()
    {
        // Keep the native Windows caption and its resize/system-menu behavior, but
        // make it visually continuous with RAWMate's fixed-dark workspace.
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));

            // DWM COLORREF uses 0x00BBGGRR. Unsupported attributes are safely ignored
            // on older Windows builds, while immersive dark mode still supplies light glyphs.
            var captionColor = ColorRef(0x20, 0x26, 0x2E);
            var borderColor = ColorRef(0x36, 0x40, 0x4B);
            var textColor = ColorRef(0xFF, 0xFF, 0xFF);
            DwmSetWindowAttribute(handle, DwmCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmBorderColor, ref borderColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmTextColor, ref textColor, sizeof(int));
        }
        catch
        {
            // Title-bar theming is cosmetic; never prevent the app from opening.
        }
    }

    private static int ColorRef(byte red, byte green, byte blue)
    {
        return red | (green << 8) | (blue << 16);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private UIElement BuildUi()
    {
        var root = new Grid { Background = Brush("#171B21") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The sidebar is a flat workspace column. A single hairline divider replaces
        // the former rounded outer card and its surrounding gutter.
        var sidebar = new Border
        {
            Background = Brush("#1B2027"),
            BorderBrush = Brush("#36404B"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(14, 14, 14, 12),
            ClipToBounds = true
        };
        var sidebarLayout = new Grid();
        sidebarLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebarLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sidebarContent = new Grid { Width = SidebarContentWidth, HorizontalAlignment = HorizontalAlignment.Left };
        sidebarContent.Children.Add(BuildSidebar());
        var sidebarViewbox = new Viewbox
        {
            Child = sidebarContent,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // Keep the removed brand area's space from reappearing on tall/4K windows.
            // The Viewbox may still scale the sidebar down on short/high-DPI work areas,
            // but any unused vertical space must remain below the controls.
            VerticalAlignment = VerticalAlignment.Top
        };
        sidebarLayout.Children.Add(sidebarViewbox);

        var versionText = new TextBlock
        {
            Text = GetDisplayVersion(),
            Foreground = Brush("#8F9CAA"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 8, 0, 0)
        };
        var appFooter=new DockPanel();appFooter.Children.Add(versionText);
        var settings=GpsSmallButton("设置");settings.HorizontalAlignment=HorizontalAlignment.Right;settings.Click+=delegate{ShowGpsSettings();};appFooter.Children.Add(settings);
        Grid.SetRow(appFooter, 1);
        sidebarLayout.Children.Add(appFooter);
        sidebar.Child = sidebarLayout;
        root.Children.Add(sidebar);

        var main = new Grid { Background = Brush("#171B21") };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var workspace = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 0),
            Child = BuildGalleryCard()
        };
        main.Children.Add(workspace);
        var infoFooter = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brush("#36404B"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = BuildPhotoInfoCard()
        };
        Grid.SetRow(infoFooter, 1);
        main.Children.Add(infoFooter);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        return root;
    }

    private static string GetDisplayVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var attribute = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();
        var raw = attribute == null ? null : attribute.InformationalVersion;
        if (String.IsNullOrWhiteSpace(raw)) raw = assembly.GetName().Version.ToString(3);

        var metadataIndex = raw.IndexOf('+');
        if (metadataIndex >= 0) raw = raw.Substring(0, metadataIndex);
        Version version;
        if (Version.TryParse(raw, out version) && version.Build == 0 && version.Revision == 0)
            raw = version.Major.ToString(CultureInfo.InvariantCulture) + "." + version.Minor.ToString(CultureInfo.InvariantCulture);
        return "v" + raw;
    }

    private UIElement BuildSidebar()
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildPathCard());
        panel.Children.Add(Divider());
        panel.Children.Add(BuildStatCard());
        panel.Children.Add(Divider());
        panel.Children.Add(BuildFooter());
        return panel;
    }

    private UIElement BuildPathCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("照片目录"));
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        folderBox.Height = 40;
        folderBox.FontSize = 15;
        folderBox.FontWeight = FontWeights.Normal;
        folderBox.Padding = new Thickness(10, 0, 10, 0);
        folderBox.VerticalContentAlignment = VerticalAlignment.Center;
        folderBox.BorderThickness = new Thickness(0);
        folderBox.Background = darkMode ? Brush("#171D24") : Brush("#FFFFFF");
        folderBox.Foreground = darkMode ? Brush("#F0F4F8") : Brush("#1F2937");
        folderBox.CaretBrush = folderBox.Foreground;
        StyleRoundedTextBox(folderBox, new CornerRadius(6, 0, 0, 6));
        folderBox.TextChanged -= FolderBoxTextChanged;
        folderBox.TextChanged += FolderBoxTextChanged;
        folderRow.Children.Add(folderBox);
        folderHistoryButton.Content = "▼";
        StyleFolderHistoryButton(folderHistoryButton);
        folderHistoryButton.Click -= OpenFolderHistory;
        folderHistoryButton.Click += OpenFolderHistory;
        Grid.SetColumn(folderHistoryButton, 1);
        folderRow.Children.Add(folderHistoryButton);
        panel.Children.Add(new Border
        {
            Child = folderRow,
            Margin = new Thickness(0, 10, 0, 0),
            BorderBrush = darkMode ? Brush("#596675") : Brush("#B7C3D0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true
        });
        folderHint.Margin = new Thickness(2, 6, 2, 0);
        folderHint.FontSize = 11;
        folderHint.FontWeight = FontWeights.Normal;
        folderHint.Foreground = Brush("#8F9CAA");
        folderHint.Text = "选择照片主目录";
        folderHint.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(folderHint);
        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var browse = SecondaryButton("选择目录", 0);
        browse.Height = 34;
        browse.HorizontalAlignment = HorizontalAlignment.Stretch;
        browse.Click += ChooseFolder;
        actions.Children.Add(browse);
        var scan = PrimaryButton("刷新目录", 0);
        scan.Height = 34;
        scan.HorizontalAlignment = HorizontalAlignment.Stretch;
        scan.Click += delegate { RefreshCurrentFolder(); };
        Grid.SetColumn(scan, 2);
        actions.Children.Add(scan);
        panel.Children.Add(actions);
        return panel;
    }

    private UIElement BuildStatCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("目录概览"));

        var stats = new Grid { Margin = new Thickness(2, 12, 2, 0) };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var statA = Stat("JPG", jpgCount, "#2678D4");
        var statB = Stat("RAW", rawCount, "#8167C8");
        var statC = Stat("待分类", pendingCount, "#E28825");
        stats.Children.Add(statA);
        Grid.SetColumn(statB, 1);
        stats.Children.Add(statB);
        Grid.SetColumn(statC, 2);
        stats.Children.Add(statC);
        panel.Children.Add(stats);
        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        organizeButton.Content = "创建分类";
        StyleButton(organizeButton, true, 0);
        organizeButton.Height = 34;
        organizeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        organizeButton.Click -= OrganizeFiles;
        organizeButton.Click += OrganizeFiles;
        actions.Children.Add(organizeButton);
        unorganizeButton.Content = "取消分类";
        StyleButton(unorganizeButton, true, 0);
        unorganizeButton.Height = 34;
        unorganizeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        unorganizeButton.Click -= UnorganizeFiles;
        unorganizeButton.Click += UnorganizeFiles;
        Grid.SetColumn(unorganizeButton, 2);
        actions.Children.Add(unorganizeButton);
        panel.Children.Add(actions);
        panel.Children.Add(BuildCullProgress());
        return panel;
    }

    private UIElement BuildCullProgress()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "挑片进度",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1F2937")
        });
        cullProgressCount.FontSize = 13;
        cullProgressCount.FontWeight = FontWeights.SemiBold;
        cullProgressCount.Foreground = Brush("#DCEBFA");
        cullProgressCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(cullProgressCount, 1);
        header.Children.Add(cullProgressCount);
        panel.Children.Add(header);

        var progress = new Grid
        {
            Background = Brushes.Transparent
        };
        cullProgressFillColumn = new ColumnDefinition();
        cullProgressRemainingColumn = new ColumnDefinition();
        progress.ColumnDefinitions.Add(cullProgressFillColumn);
        progress.ColumnDefinitions.Add(cullProgressRemainingColumn);
        var fill = new Border { Background = Brush(ButtonBlue), CornerRadius = new CornerRadius(5) };
        progress.Children.Add(fill);
        cullProgressPercent.FontSize = 12;
        cullProgressPercent.FontWeight = FontWeights.SemiBold;
        cullProgressPercent.Foreground = Brushes.White;
        cullProgressPercent.HorizontalAlignment = HorizontalAlignment.Stretch;
        cullProgressPercent.VerticalAlignment = VerticalAlignment.Center;
        cullProgressPercent.TextAlignment = TextAlignment.Center;
        Grid.SetColumnSpan(cullProgressPercent, 2);
        Panel.SetZIndex(cullProgressPercent, 1);
        progress.Children.Add(cullProgressPercent);
        panel.Children.Add(new Border
        {
            Child = progress,
            Height = 22,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(1),
            Background = Brush("#171D24"),
            BorderBrush = Brush("#596675"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true
        });

        var counts = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        for (var index = 0; index < 2; index++)
            counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddProgressOutcome(counts, pickedProgressCount, "P", "保留", "#58D26E", 0);
        AddProgressOutcome(counts, rejectedProgressCount, "X", "废片", "#F0667A", 1);
        panel.Children.Add(counts);
        UpdateCullProgress();
        return panel;
    }

    private static void AddProgressOutcome(Grid host, TextBlock block, string shortcut, string label, string color, int column)
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = column == 0 ? new Thickness(12, 0, 0, 0) : new Thickness(0, 0, 12, 0)
        };
        group.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Background = Brush("#20" + color.Substring(1)),
            BorderBrush = Brush("#70" + color.Substring(1)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = shortcut,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        });
        block.FontSize = 12;
        block.FontWeight = FontWeights.Normal;
        block.VerticalAlignment = VerticalAlignment.Center;
        block.Margin = new Thickness(7, 0, 0, 0);
        SetProgressOutcomeText(block, label, color, 0);
        group.Children.Add(block);
        Grid.SetColumn(group, column);
        host.Children.Add(group);
    }

    private static void SetProgressOutcomeText(TextBlock block, string label, string color, int count)
    {
        block.Inlines.Clear();
        block.Inlines.Add(new Run(label + "  ") { Foreground = Brush("#B5C0CD"), FontWeight = FontWeights.Normal });
        block.Inlines.Add(new Run(count.ToString("N0")) { Foreground = Brush(color), FontSize = 12, FontWeight = FontWeights.SemiBold });
    }

    private void UpdateCullProgress()
    {
        var currentFiles = new List<string>();
        var root = RootFolder(false);
        if (root != null) currentFiles = CollectionJpgs(root);
        var completedRaw = root == null ? 0 : CompletedRawOnlyInRoot(root).Count;
        var total = currentFiles.Count + completedRaw;
        var picked = currentFiles.Count(IsKept) + completedRaw;
        var rejected = currentFiles.Count(rejectedFiles.Contains);
        var decided = picked + rejected;
        var percent = total > 0 ? (int)Math.Round(decided * 100.0 / total) : 0;

        cullProgressCount.Text = "已决定 " + decided.ToString("N0") + " / " + total.ToString("N0");
        cullProgressPercent.Text = percent + "%";
        SetProgressOutcomeText(pickedProgressCount, "保留", "#58D26E", picked);
        SetProgressOutcomeText(rejectedProgressCount, "废片", "#F0667A", rejected);
        cullProgressFillColumn.Width = new GridLength(total > 0 ? decided : 0, GridUnitType.Star);
        cullProgressRemainingColumn.Width = new GridLength(total > 0 ? total - decided : 1, GridUnitType.Star);
        UpdateRejectedButtonState();
    }

    private void UpdateRejectedButtonState()
    {
        if (recycleRejectedButton == null) return;
        var root = RootFolder(false);
        var enabled = !cullMarksReadFailed && !collectionScanning && root != null && BuildCleanupFromCollection(CollectionState(root)).Items.Count > 0;
        recycleRejectedButton.IsHitTestVisible = enabled;
        recycleRejectedButton.Opacity = enabled ? 1.0 : 0.52;
    }

    private UIElement BuildGalleryCard()
    {
        return singleViewMode && !buildingInitialShell ? BuildSingleView() : BuildGridView();
    }

    private UIElement BuildGridView()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(SectionTitle("图库"));
        var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        galleryCaption.Text = tileFiles.Count > 0 ? tileFiles.Count + " 张 JPG · 单击选择，双击进入单张视图" : "选择集合目录后，递归显示 JPG。";
        galleryCaption.Foreground = Brush("#66778B");
        galleryCaption.VerticalAlignment = VerticalAlignment.Center;
        topRight.Children.Add(galleryCaption);
        topRight.Children.Add(BuildFilterButton());
        topRight.Children.Add(BuildSortButton());
        topRight.Children.Add(BuildThumbnailSizeButton());
        topRight.Children.Add(BuildViewButton(true));
        topRight.Children.Add(BuildViewButton(false));
        Grid.SetColumn(topRight, 1);
        title.Children.Add(topRight);
        var header=new StackPanel();header.Children.Add(title);panel.Children.Add(header);
        galleryScrollViewer = new ScrollViewer { Margin = new Thickness(0, 12, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 8, 18, 8) };
        var miniScrollBar = CreateMiniVerticalScrollBar();
        var syncingMiniScrollBar = false;
        galleryScrollViewer.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
        {
            syncingMiniScrollBar = true;
            miniScrollBar.Maximum = Math.Max(0, e.ExtentHeight - e.ViewportHeight);
            miniScrollBar.ViewportSize = Math.Max(0, e.ViewportHeight);
            miniScrollBar.Value = Math.Min(miniScrollBar.Maximum, Math.Max(0, e.VerticalOffset));
            miniScrollBar.Visibility = miniScrollBar.Maximum > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            syncingMiniScrollBar = false;
            QueueVisibleThumbnailLoads();
        };
        miniScrollBar.ValueChanged += delegate
        {
            if (!syncingMiniScrollBar) galleryScrollViewer.ScrollToVerticalOffset(miniScrollBar.Value);
        };
        galleryScrollViewer.SizeChanged += delegate { QueueVisibleThumbnailLoads(); };
        galleryScrollViewer.Loaded += delegate { QueueVisibleThumbnailLoads(); };
        gallery.HorizontalAlignment = HorizontalAlignment.Stretch;
        galleryScrollViewer.Content = gallery;
        Grid.SetRow(galleryScrollViewer, 1);
        panel.Children.Add(galleryScrollViewer);
        Grid.SetRow(miniScrollBar, 1);
        panel.Children.Add(miniScrollBar);
        EnableGpsMarquee(galleryScrollViewer,panel);
        UpdateGpsLine();
        return panel;
    }

    private UIElement BuildSingleView()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { ClipToBounds = true };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(SectionTitle("单张视图"));
        singleCaption.HorizontalAlignment = HorizontalAlignment.Stretch;
        singleCaption.VerticalAlignment = VerticalAlignment.Center;
        singleCaption.TextAlignment = TextAlignment.Center;
        singleCaption.TextWrapping = TextWrapping.NoWrap;
        singleCaption.TextTrimming = TextTrimming.CharacterEllipsis;
        singleCaption.Margin = new Thickness(12, 0, 12, 0);
        singleCaption.Foreground = Brush("#66778B");
        singleCaption.FontSize = 13;
        Grid.SetColumn(singleCaption, 1);
        header.Children.Add(singleCaption);
        var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        topRight.Children.Add(BuildSingleCullBadge());
        topRight.Children.Add(BuildAdvanceTargetButton());
        topRight.Children.Add(BuildViewButton(true));
        topRight.Children.Add(BuildViewButton(false));
        Grid.SetColumn(topRight, 2);
        header.Children.Add(topRight);
        panel.Children.Add(header);

        var frame = new Border { Margin = new Thickness(0, 8, 0, 0), Background = Brush("#151A20"), BorderThickness = new Thickness(0), ClipToBounds = true };
        var stage = new Grid();
        singleViewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Background = Brushes.Transparent, Focusable = true, Cursor = Cursors.Hand };
        singleImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        singleViewer.Content = singleImage;
        singleViewer.PreviewMouseWheel += SingleViewerMouseWheel;
        singleViewer.PreviewMouseLeftButtonDown += SingleViewerMouseLeftButtonDown;
        singleViewer.PreviewMouseMove += SingleViewerMouseMove;
        singleViewer.PreviewMouseLeftButtonUp += SingleViewerMouseLeftButtonUp;
        singleViewer.ScrollChanged += delegate { QueueNavigatorUpdate(); };
        singleViewer.SizeChanged += delegate { if (fitSingleImage) ApplySingleZoom(); };
        stage.Children.Add(singleViewer);
        stage.Children.Add(BuildNavigator());
        var stripFrame = BuildFilmStrip();
        // Keep navigation and zoom as separate layers: zoom above, filmstrip below.
        stripFrame.Margin = new Thickness(0, 0, 0, 12);
        stripFrame.HorizontalAlignment = HorizontalAlignment.Center;
        stripFrame.VerticalAlignment = VerticalAlignment.Bottom;
        stage.Children.Add(stripFrame);
        var controlsFrame = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 80), Padding = new Thickness(5, 4, 5, 4), Background = Brush("#E6202833"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var previous = SingleToolButton("‹", 30, "上一张（由挑片 / 浏览模式决定）");
        previous.Margin = new Thickness(0, 0, 4, 0);
        previous.Click += delegate { NavigateSingle(-1); };
        controls.Children.Add(previous);
        var zoomOut = SingleToolButton("−", 28, "缩小");
        zoomOut.Margin = new Thickness(0, 0, 4, 0);
        zoomOut.Click += delegate { ChangeSingleZoom(1.0 / 1.25); };
        controls.Children.Add(zoomOut);
        zoomBox.Width = 52;
        zoomBox.Height = 26;
        zoomBox.Padding = new Thickness(2, 3, 2, 2);
        zoomBox.TextAlignment = TextAlignment.Center;
        zoomBox.VerticalContentAlignment = VerticalAlignment.Center;
        zoomBox.FontSize = 11;
        zoomBox.Background = Brush("#171D24");
        zoomBox.Foreground = Brushes.White;
        zoomBox.BorderBrush = Brush("#4B5A6B");
        zoomBox.BorderThickness = new Thickness(1);
        zoomBox.Margin = new Thickness(0, 0, 4, 0);
        StyleZoomBox(zoomBox);
        zoomBox.KeyDown += ZoomBoxKeyDown;
        zoomBox.LostFocus += delegate { ApplyZoomText(); };
        controls.Children.Add(zoomBox);
        var fit = SingleToolButton("适应", 42, "适应窗口");
        fit.Margin = new Thickness(0, 0, 4, 0);
        fit.Click += delegate { fitSingleImage = true; ApplySingleZoom(); };
        controls.Children.Add(fit);
        var zoomIn = SingleToolButton("+", 28, "放大");
        zoomIn.Margin = new Thickness(0, 0, 4, 0);
        zoomIn.Click += delegate { ChangeSingleZoom(1.25); };
        controls.Children.Add(zoomIn);
        var next = SingleToolButton("›", 30, "下一张（由挑片 / 浏览模式决定）");
        next.Click += delegate { NavigateSingle(1); };
        controls.Children.Add(next);
        controlsFrame.Child = controls;
        stage.Children.Add(controlsFrame);
        frame.Child = stage;
        Grid.SetRow(frame, 1);
        panel.Children.Add(frame);
        LoadSinglePhoto();
        return panel;
    }

    private Border BuildSingleCullBadge()
    {
        singleCullBadge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Height = HeaderControlHeight,
            MinWidth = 58,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
        singleCullBadgeText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        singleCullBadge.Child = singleCullBadgeText;
        UpdateSingleCullStatus();
        return singleCullBadge;
    }

    private Button BuildAdvanceTargetButton()
    {
        advanceTargetButton = SecondaryButton(String.Empty, 56);
        advanceTargetButton.Height = HeaderControlHeight;
        advanceTargetButton.Margin = new Thickness(HeaderControlGap, 0, 0, 0);
        advanceTargetButton.Padding = new Thickness(0);
        advanceTargetButton.FontSize = 12;
        UpdateAdvanceTargetButton();
        advanceTargetButton.Click += delegate
        {
            advanceToUndecided = !advanceToUndecided;
            UpdateAdvanceTargetButton();
            SetStatus(advanceToUndecided
                ? "已切换到“挑片”：前后导航只访问未决定照片。"
                : "已切换到“浏览”：前后导航按当前排序逐张查看。", false);
        };
        return advanceTargetButton;
    }

    private void UpdateAdvanceTargetButton()
    {
        if (advanceTargetButton == null) return;
        advanceTargetButton.Content = advanceToUndecided ? "挑片" : "浏览";
        advanceTargetButton.ToolTip = advanceToUndecided
            ? "挑片：前后导航只访问未决定照片，标记后跳到下一张未决定照片"
            : "浏览：前后导航和标记后前进均按当前排序逐张进行";
    }

    private void UpdateSingleCullStatus()
    {
        if (singleCullBadge == null || singleCullBadgeText == null) return;
        if (!String.IsNullOrWhiteSpace(currentSingleFile) && pickedFiles.Contains(currentSingleFile))
        {
            singleCullBadgeText.Text = "双格式";
            singleCullBadgeText.Foreground = Brush("#65E27A");
            singleCullBadge.Background = Brush("#E6213328");
            singleCullBadge.BorderBrush = Brush("#7258D26E");
            singleCullBadge.ToolTip = "保留 JPG + RAW";
        }
        else if (DecisionOf(currentSingleFile) == "J" || DecisionOf(currentSingleFile) == "R")
        {
            var jpg = DecisionOf(currentSingleFile) == "J";
            singleCullBadgeText.Text = jpg ? "仅 JPG" : "仅 RAW";
            singleCullBadgeText.Foreground = Brush(jpg ? "#76B8FF" : "#C7A0FF");
            singleCullBadge.Background = Brush("#E6212C38");
            singleCullBadge.BorderBrush = Brush(jpg ? "#7276B8FF" : "#72C7A0FF");
            singleCullBadge.ToolTip = jpg ? "仅保留 JPG；执行清理时回收 RAW" : "仅保留 RAW；执行清理时回收 JPG";
        }
        else if (!String.IsNullOrWhiteSpace(currentSingleFile) && rejectedFiles.Contains(currentSingleFile))
        {
            singleCullBadgeText.Text = "废片";
            singleCullBadgeText.Foreground = Brush("#FF7084");
            singleCullBadge.Background = Brush("#E63B252C");
            singleCullBadge.BorderBrush = Brush("#72F0667A");
            singleCullBadge.ToolTip = "当前照片状态：废片";
        }
        else
        {
            singleCullBadgeText.Text = "未决定";
            singleCullBadgeText.Foreground = Brush("#B7C4D2");
            singleCullBadge.Background = Brush("#E6252D36");
            singleCullBadge.BorderBrush = Brush("#725B6979");
            singleCullBadge.ToolTip = "当前照片状态：未决定";
        }
    }

    private UIElement BuildNavigator()
    {
        var frame = new Border
        {
            Width = 208,
            Height = 154,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
            Padding = new Thickness(8, 6, 8, 8),
            Background = darkMode ? Brush("#D91B222B") : Brush("#F2FFFFFF"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
        frame.ToolTip = "点击或拖动取景框以定位；适应窗口时会按当前适应比例切换到 100% 或 200%";

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(new TextBlock
        {
            Text = "导航器",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = darkMode ? Brush("#D8E2EE") : Brush("#34475A"),
            Margin = new Thickness(1, 0, 0, 5)
        });

        navigatorCanvas = new Canvas
        {
            Width = 190,
            Height = 116,
            Background = Brushes.Black,
            ClipToBounds = true,
            Cursor = Cursors.Cross
        };
        navigatorImage = new Image
        {
            Width = 190,
            Height = 116,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(navigatorImage, BitmapScalingMode.HighQuality);
        navigatorViewport = new Border
        {
            BorderBrush = Brush("#66AEFF"),
            BorderThickness = new Thickness(1.5),
            Background = Brush("#1A4F9BFF"),
            IsHitTestVisible = false
        };
        navigatorCanvas.Children.Add(navigatorImage);
        navigatorCanvas.Children.Add(navigatorViewport);
        navigatorCanvas.PreviewMouseLeftButtonDown += NavigatorMouseLeftButtonDown;
        navigatorCanvas.PreviewMouseMove += NavigatorMouseMove;
        navigatorCanvas.PreviewMouseLeftButtonUp += NavigatorMouseLeftButtonUp;
        navigatorCanvas.LostMouseCapture += delegate { navigatorDragging = false; };
        Grid.SetRow(navigatorCanvas, 1);
        panel.Children.Add(navigatorCanvas);
        frame.Child = panel;
        return frame;
    }

    private Rect NavigatorImageRect()
    {
        var source = navigatorImage.Source as BitmapSource;
        var width = navigatorCanvas.ActualWidth > 0 ? navigatorCanvas.ActualWidth : navigatorCanvas.Width;
        var height = navigatorCanvas.ActualHeight > 0 ? navigatorCanvas.ActualHeight : navigatorCanvas.Height;
        if (source == null || width <= 0 || height <= 0 || source.Width <= 0 || source.Height <= 0)
            return Rect.Empty;
        var scale = Math.Min(width / source.Width, height / source.Height);
        var displayWidth = source.Width * scale;
        var displayHeight = source.Height * scale;
        return new Rect((width - displayWidth) / 2.0, (height - displayHeight) / 2.0, displayWidth, displayHeight);
    }

    private void QueueNavigatorUpdate()
    {
        if (navigatorUpdateQueued || Dispatcher.HasShutdownStarted) return;
        navigatorUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
        {
            navigatorUpdateQueued = false;
            UpdateNavigatorViewport();
        }));
    }

    private void UpdateNavigatorViewport()
    {
        var source = singleImage.Source as BitmapSource;
        var imageRect = NavigatorImageRect();
        if (source == null || imageRect.IsEmpty)
        {
            navigatorViewport.Visibility = Visibility.Collapsed;
            return;
        }

        navigatorViewport.Visibility = Visibility.Visible;
        var scaledWidth = source.Width * singleZoom;
        var scaledHeight = source.Height * singleZoom;
        var viewportWidth = Math.Max(1, singleViewer.ViewportWidth);
        var viewportHeight = Math.Max(1, singleViewer.ViewportHeight);
        var rectWidth = scaledWidth <= viewportWidth ? imageRect.Width : imageRect.Width * viewportWidth / scaledWidth;
        var rectHeight = scaledHeight <= viewportHeight ? imageRect.Height : imageRect.Height * viewportHeight / scaledHeight;
        rectWidth = Math.Max(8, Math.Min(imageRect.Width, rectWidth));
        rectHeight = Math.Max(8, Math.Min(imageRect.Height, rectHeight));

        var x = imageRect.Left;
        var y = imageRect.Top;
        if (singleViewer.ScrollableWidth > 0.5)
            x += (imageRect.Width - rectWidth) * singleViewer.HorizontalOffset / singleViewer.ScrollableWidth;
        if (singleViewer.ScrollableHeight > 0.5)
            y += (imageRect.Height - rectHeight) * singleViewer.VerticalOffset / singleViewer.ScrollableHeight;

        navigatorViewport.Width = rectWidth;
        navigatorViewport.Height = rectHeight;
        Canvas.SetLeft(navigatorViewport, x);
        Canvas.SetTop(navigatorViewport, y);
    }

    private void NavigatorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (navigatorImage.Source == null) return;
        navigatorDragging = true;
        navigatorCanvas.CaptureMouse();
        CenterSingleViewFromNavigator(e.GetPosition(navigatorCanvas));
        e.Handled = true;
    }

    private void NavigatorMouseMove(object sender, MouseEventArgs e)
    {
        if (!navigatorDragging || !navigatorCanvas.IsMouseCaptured) return;
        CenterSingleViewFromNavigator(e.GetPosition(navigatorCanvas));
        e.Handled = true;
    }

    private void NavigatorMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!navigatorDragging) return;
        CenterSingleViewFromNavigator(e.GetPosition(navigatorCanvas));
        navigatorDragging = false;
        navigatorCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void CenterSingleViewFromNavigator(Point point)
    {
        var source = singleImage.Source as BitmapSource;
        var imageRect = NavigatorImageRect();
        if (source == null || imageRect.IsEmpty) return;

        var normalizedX = Math.Max(0, Math.Min(1, (point.X - imageRect.Left) / imageRect.Width));
        var normalizedY = Math.Max(0, Math.Min(1, (point.Y - imageRect.Top) / imageRect.Height));
        if (fitSingleImage || (singleViewer.ScrollableWidth < 0.5 && singleViewer.ScrollableHeight < 0.5))
        {
            fitSingleImage = false;
            singleZoom = GetSingleInspectionZoom();
            ApplySingleZoom();
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
        {
            var scaledWidth = source.Width * singleZoom;
            var scaledHeight = source.Height * singleZoom;
            var horizontal = normalizedX * scaledWidth - singleViewer.ViewportWidth / 2.0;
            var vertical = normalizedY * scaledHeight - singleViewer.ViewportHeight / 2.0;
            singleViewer.ScrollToHorizontalOffset(Math.Max(0, Math.Min(singleViewer.ScrollableWidth, horizontal)));
            singleViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(singleViewer.ScrollableHeight, vertical)));
            QueueNavigatorUpdate();
        }));
    }

    private Button BuildFilterButton()
    {
        filterButton = SecondaryButton(FilterLabel(), 0);
        filterButton.Width = 94;
        filterButton.Height = 28;
        filterButton.FontSize = 11;
        filterButton.Margin = new Thickness(10, 0, 0, 0);
        filterButton.ToolTip = "筛选挑片标记";
        filterButton.Click += OpenFilterMenu;
        return filterButton;
    }

    private Button BuildSortButton()
    {
        var button = SecondaryButton(SortLabel(), 88);
        button.Height = 28;
        button.FontSize = 11;
        button.Margin = new Thickness(8, 0, 0, 0);
        button.ToolTip = "选择图库排序方式";
        button.Click += delegate
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
            StyleFolderHistoryMenu(menu);
            AddSortMenuItem(menu, "名称升序", "name_asc");
            AddSortMenuItem(menu, "名称降序", "name_desc");
            AddSortMenuItem(menu, "拍摄时间升序", "capture_asc");
            AddSortMenuItem(menu, "拍摄时间降序", "capture_desc");
            menu.IsOpen = true;
        };
        return button;
    }

    private void AddSortMenuItem(ContextMenu menu, string text, string value)
    {
        var item = new MenuItem { Header = text };
        StyleContextMenuItem(item);
        item.Click += delegate
        {
            gallerySortMode = value;
            SaveSettings();
            RebuildUi();
        };
        menu.Items.Add(item);
    }

    private string SortLabel()
    {
        switch (gallerySortMode)
        {
            case "name_desc": return "名称 ↓";
            case "capture_asc": return "拍摄时间 ↑";
            case "capture_desc": return "拍摄时间 ↓";
            default: return "名称 ↑";
        }
    }

    private Button BuildThumbnailSizeButton()
    {
        var button = SecondaryButton(ThumbnailSizeLabel(), 76);
        button.Height = 28;
        button.FontSize = 11;
        button.Margin = new Thickness(8, 0, 0, 0);
        button.ToolTip = "选择网格缩略图大小";
        button.Click += delegate
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
            StyleFolderHistoryMenu(menu);
            AddThumbnailSizeMenuItem(menu, "小", 160);
            AddThumbnailSizeMenuItem(menu, "中", 196);
            AddThumbnailSizeMenuItem(menu, "大", 232);
            AddThumbnailSizeMenuItem(menu, "特大", 280);
            menu.IsOpen = true;
        };
        return button;
    }

    private void AddThumbnailSizeMenuItem(ContextMenu menu, string text, double width)
    {
        var item = new MenuItem { Header = "缩略图：" + text };
        StyleContextMenuItem(item);
        item.Click += delegate
        {
            gridTileWidth = width;
            SaveSettings();
            RebuildUi();
        };
        menu.Items.Add(item);
    }

    private string ThumbnailSizeLabel()
    {
        if (gridTileWidth <= 170) return "缩略图 小";
        if (gridTileWidth <= 210) return "缩略图 中";
        if (gridTileWidth <= 250) return "缩略图 大";
        return "缩略图 特大";
    }

    private string FilterLabel()
    {
        switch (activeFilter)
        {
            case "picked": return "保留";
            case "rejected": return "废片";
            case "undecided": return "未决定";
            default: return "全部照片";
        }
    }

    private void OpenFilterMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = filterButton, Placement = PlacementMode.Bottom, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
        StyleFolderHistoryMenu(menu);
        AddFilterMenuItem(menu, "全部照片", "all");
        AddFilterMenuItem(menu, "旗标：保留", "picked");
        AddFilterMenuItem(menu, "旗标：废片", "rejected");
        AddFilterMenuItem(menu, "未决定", "undecided");
        menu.IsOpen = true;
    }

    private void AddFilterMenuItem(ContextMenu menu, string text, string value)
    {
        var item = new MenuItem { Header = text };
        StyleContextMenuItem(item);
        item.Click += delegate { activeFilter = value; RebuildUi(); };
        menu.Items.Add(item);
    }

    private Border BuildFilmStrip()
    {
        EnsureGalleryFiles();
        filmStrip = new StackPanel { Orientation = Orientation.Horizontal };
        filmTiles.Clear();
        lazyFilmThumbnailFiles.Clear();
        loadingFilmThumbnailTiles.Clear();
        filmThumbnailLoadVersion++;
        foreach (var file in galleryFiles)
        {
            var tile = new Border { Width = 62, Height = 48, Margin = new Thickness(2, 0, 2, 0), Background = Brush("#151A20"), BorderBrush = Brush("#3F4955"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Cursor = Cursors.Hand, ToolTip = Path.GetFileName(file) };
            BitmapSource cachedThumbnail;
            int cachedWidth;
            if (TryGetAnyCachedThumbnail(file, out cachedThumbnail, out cachedWidth))
                tile.Child = CreateThumbnailImage(cachedThumbnail);
            else
            {
                tile.Child = new TextBlock { Text = "…", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                lazyFilmThumbnailFiles[tile] = file;
            }
            tile.MouseLeftButtonDown += delegate { currentSingleFile = file; ShowPhotoInfo(file); LoadSinglePhoto(); UpdateFilmStripSelection(); };
            filmTiles[tile] = file;
            filmStrip.Children.Add(tile);
        }
        filmStripScrollViewer = new ScrollViewer { Content = filmStrip, Width = 450, Height = 52, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent };
        filmStripScrollViewer.ScrollChanged += delegate { QueueVisibleFilmThumbnailLoads(); };
        filmStripScrollViewer.SizeChanged += delegate { QueueVisibleFilmThumbnailLoads(); };
        filmStripScrollViewer.Loaded += delegate
        {
            UpdateFilmStripSelection();
            QueueVisibleFilmThumbnailLoads();
        };
        return new Border { Child = filmStripScrollViewer, Padding = new Thickness(3, 2, 3, 2), Background = Brush("#D9202833"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4), MaxWidth = 460 };
    }

    private void UpdateFilmStripSelection()
    {
        Border activeTile = null;
        foreach (var item in filmTiles)
        {
            var active = String.Equals(item.Value, currentSingleFile, StringComparison.OrdinalIgnoreCase);
            item.Key.BorderBrush = active ? Brush("#2678D4") : Brush("#3F4955");
            item.Key.BorderThickness = new Thickness(active ? 2 : 1);
            item.Key.Opacity = active ? 1.0 : 0.65;
            if (active) activeTile = item.Key;
        }
        if (activeTile != null && activeTile.IsLoaded) activeTile.BringIntoView();
        QueueVisibleFilmThumbnailLoads();
    }

    private void QueueVisibleFilmThumbnailLoads()
    {
        if (!singleViewMode || filmThumbnailLoadQueued || Dispatcher.HasShutdownStarted) return;
        filmThumbnailLoadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
        {
            filmThumbnailLoadQueued = false;
            LoadVisibleFilmThumbnails();
        }));
    }

    private void LoadVisibleFilmThumbnails()
    {
        if (!singleViewMode || filmStripScrollViewer == null || !filmStripScrollViewer.IsLoaded) return;
        var version = filmThumbnailLoadVersion;
        foreach (var item in lazyFilmThumbnailFiles.ToList())
        {
            var tile = item.Key;
            if (tile.Child is Image || loadingFilmThumbnailTiles.Contains(tile) || !IsFilmThumbnailNearViewport(tile)) continue;
            loadingFilmThumbnailTiles.Add(tile);
            var file = item.Value;
            Task.Factory.StartNew(delegate { return LoadCachedThumbnail(file, 110); }).ContinueWith(delegate(Task<BitmapSource> task)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    loadingFilmThumbnailTiles.Remove(tile);
                    string expected;
                    if (version != filmThumbnailLoadVersion || !lazyFilmThumbnailFiles.TryGetValue(tile, out expected) ||
                        !String.Equals(expected, file, StringComparison.OrdinalIgnoreCase)) return;
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        tile.Child = CreateThumbnailImage(task.Result);
                    }
                    else
                    {
                        tile.Child = new TextBlock { Text = "×", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    }
                }));
            });
        }
    }

    private bool IsFilmThumbnailNearViewport(Border tile)
    {
        if (!tile.IsLoaded || filmStripScrollViewer.ViewportWidth <= 0) return false;
        try
        {
            var bounds = tile.TransformToAncestor(filmStripScrollViewer).TransformBounds(
                new Rect(0, 0, Math.Max(1, tile.ActualWidth), Math.Max(1, tile.ActualHeight)));
            var buffer = filmStripScrollViewer.ViewportWidth;
            return bounds.Right >= -buffer && bounds.Left <= filmStripScrollViewer.ViewportWidth + buffer;
        }
        catch { return false; }
    }

    private Button BuildViewButton(bool gridView)
    {
        var active = singleViewMode != gridView;
        var button = new Button { Content = gridView ? "▦" : "▣", ToolTip = gridView ? "网格视图" : "单张视图", Margin = new Thickness(HeaderControlGap, 0, 0, 0) };
        button.Width = 32;
        button.Height = HeaderControlHeight;
        button.FontSize = 18;
        button.Padding = new Thickness(0, -2, 0, 0);
        button.Cursor = Cursors.Hand;
        button.BorderThickness = new Thickness(1);
        button.Background = Brush(ButtonBlue);
        button.BorderBrush = active ? Brush("#A9C7E8") : Brush(ButtonBlue);
        button.Foreground = Brushes.White;
        button.Opacity = active ? 1.0 : 0.82;
        StyleViewButton(button);
        button.Click += delegate { SetViewMode(!gridView); };
        return button;
    }

    private static Button SingleToolButton(string text, double width, string toolTip)
    {
        var button = new Button { Content = text, Width = width, Height = 26, ToolTip = toolTip, Cursor = Cursors.Hand, FontSize = 13, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), Background = Brush(ButtonBlue), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, button.Background);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
        return button;
    }

    private static void StyleZoomBox(TextBox box)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, box.Background);
        border.SetValue(Border.BorderBrushProperty, box.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, box.BorderThickness);
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(ScrollViewer.BackgroundProperty, Brushes.Transparent);
        host.SetValue(ScrollViewer.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox));
        template.VisualTree = border;
        box.Template = template;
    }

    private static void StyleRoundedTextBox(TextBox box, CornerRadius radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(Border.BackgroundProperty, box.Background);
        border.SetValue(Border.BorderBrushProperty, box.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, box.BorderThickness);
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(ScrollViewer.BackgroundProperty, Brushes.Transparent);
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        host.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox));
        template.VisualTree = border;
        box.Template = template;
    }

    private UIElement BuildPhotoInfoCard()
    {
        var panel = new StackPanel();
        if (String.IsNullOrWhiteSpace(photoInfo.Text)) photoInfo.Text = "点击缩略图查看拍摄信息";
        photoInfo.FontSize = 13;
        photoInfo.Foreground = Brush("#66778B");
        photoInfo.TextWrapping = TextWrapping.NoWrap;
        photoInfo.TextTrimming = TextTrimming.CharacterEllipsis;
        photoInfo.VerticalAlignment = VerticalAlignment.Center;
        photoInfo.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(photoInfo);
        gpsInfo.FontSize=12; gpsInfo.Foreground=Brush("#899BB1");
        gpsInfo.TextTrimming=TextTrimming.CharacterEllipsis;gpsInfo.Margin=new Thickness(0);
        gpsInfo.Visibility=Visibility.Visible;gpsInfo.VerticalAlignment=VerticalAlignment.Center;
        gpsSetButton=GpsSmallButton("设为来源");gpsSetButton.Click+=delegate {SetGpsSource(currentSingleFile);};
        gpsApplyButton=GpsSmallButton("应用来源");gpsApplyButton.Click+=delegate {ApplyGps(currentSingleFile);};
        gpsSourceText=new TextBlock{FontSize=12,Foreground=Brush("#899BB1"),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
        gpsClearButton=GpsSmallButton("清除");gpsClearButton.Click+=delegate {ClearGpsSource();};
        gpsGridActions=new Grid();
        gpsGridSetButton=GpsSmallButton("设为来源");gpsGridSetButton.Click+=delegate {var files=SelectedGpsFiles(null);if(files.Length==1)SetGpsSource(files[0]);};
        gpsGridApplyButton=GpsSmallButton("应用来源");gpsGridApplyButton.Click+=delegate {ApplyGpsSelection(SelectedGpsFiles(null));};
            // Both views share this footer layout; selection and click handlers remain separate.
            var singleGpsRow=new Grid{Margin=new Thickness(0,4,0,0),ClipToBounds=true};
            singleGpsRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            singleGpsRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            singleGpsRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            singleGpsRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            singleGpsRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            singleGpsRow.Children.Add(gpsInfo);
            gpsSingleSourceDivider=new Border{Width=1,Height=14,Background=Brush("#36404B"),Margin=new Thickness(12,0,12,0),VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(gpsSingleSourceDivider,1);singleGpsRow.Children.Add(gpsSingleSourceDivider);
            gpsSourceBar=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            var sourceContent=new Grid{HorizontalAlignment=HorizontalAlignment.Left,ClipToBounds=true};
            sourceContent.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            sourceContent.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            sourceContent.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            sourceContent.Children.Add(gpsSourceText);
            gpsSingleSourceCoordinates=new TextBlock{FontSize=12,Foreground=Brush("#899BB1"),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
            Grid.SetColumn(gpsSingleSourceCoordinates,1);sourceContent.Children.Add(gpsSingleSourceCoordinates);
            gpsSingleCurrentSource=new TextBlock{Text="当前来源",FontSize=12,Foreground=Brush("#899BB1"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
            Grid.SetColumn(gpsSingleCurrentSource,2);sourceContent.Children.Add(gpsSingleCurrentSource);
            gpsSourceBar.Children.Add(sourceContent);Grid.SetColumn(gpsSourceBar,2);singleGpsRow.Children.Add(gpsSourceBar);
            var actions=singleViewMode?new Grid():gpsGridActions;
            actions.Margin=new Thickness(12,0,0,0);actions.VerticalAlignment=VerticalAlignment.Center;
            actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var setButton=singleViewMode?gpsSetButton:gpsGridSetButton;
            var applyButton=singleViewMode?gpsApplyButton:gpsGridApplyButton;
            gpsClearButton.Margin=new Thickness(0);setButton.Margin=new Thickness(8,0,0,0);applyButton.Margin=new Thickness(8,0,0,0);
            actions.Children.Add(gpsClearButton);Grid.SetColumn(setButton,1);actions.Children.Add(setButton);Grid.SetColumn(applyButton,2);actions.Children.Add(applyButton);
            Grid.SetColumn(actions,4);singleGpsRow.Children.Add(actions);
            Action<Button,bool> mutedButton=delegate(Button button,bool tertiary)
            {
                button.Background=tertiary?Brush("#293D54"):Brushes.Transparent;button.Foreground=Brush("#A9B8CA");button.FontWeight=FontWeights.Normal;
                var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));border.SetValue(Border.BackgroundProperty,button.Background);
                border.SetValue(Border.BorderBrushProperty,Brush("#465464"));border.SetValue(Border.BorderThicknessProperty,new Thickness(tertiary?0:1));border.SetValue(Border.PaddingProperty,new Thickness(10,2,10,2));
                var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);
                var template=new ControlTemplate(typeof(Button)){VisualTree=border};var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.5));template.Triggers.Add(disabled);button.Template=template;
            };
            mutedButton(setButton,false);mutedButton(gpsClearButton,true);
            // The star source region includes flexible trailing space. Its left-aligned
            // content measures within the finite space left after the Auto actions.
            sourceContent.SizeChanged+=delegate{gpsSingleSourceCoordinates.MaxWidth=Math.Max(0,sourceContent.ActualWidth-gpsSingleCurrentSource.DesiredSize.Width-44);};
            panel.Children.Add(singleGpsRow);
        UpdateGpsLine();
        return panel;
    }

    private UIElement BuildFooter()
    {
        var footer = new StackPanel();
        footer.Children.Add(SectionTitle("操作"));
        var checkPairs = SecondaryButton("文件对应检查", 0);
        checkPairs.HorizontalAlignment = HorizontalAlignment.Stretch;
        checkPairs.Margin = new Thickness(0, 12, 0, 0);
        checkPairs.Click += CheckMissingPairs;
        footer.Children.Add(checkPairs);
        recycleSelectedButton.Content = "选中项标记废片";
        StyleButton(recycleSelectedButton, true, 0);
        recycleSelectedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        recycleSelectedButton.Margin = new Thickness(0, 8, 0, 0);
        recycleSelectedButton.IsHitTestVisible = false;
        recycleSelectedButton.Opacity = 0.52;
        recycleSelectedButton.Click -= RecycleSelectedPairs;
        recycleSelectedButton.Click += RecycleSelectedPairs;
        footer.Children.Add(recycleSelectedButton);
        recycleRejectedButton.Content = "执行清理";
        recycleRejectedButton.ToolTip = "根据 J / R / X 决定清理当前目录；确认后移入 Windows 回收站";
        StyleDestructiveButton(recycleRejectedButton, 0);
        recycleRejectedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        recycleRejectedButton.Margin = new Thickness(0, 8, 0, 0);
        UpdateRejectedButtonState();
        recycleRejectedButton.Click -= RecycleRejectedPairs;
        recycleRejectedButton.Click += RecycleRejectedPairs;
        footer.Children.Add(recycleRejectedButton);
        return footer;
    }

    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        using (var dialog = new Forms.FolderBrowserDialog { Description = "选择相机照片所在的目录", ShowNewFolderButton = false })
        {
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                folderBox.Text = dialog.SelectedPath;
                RefreshCurrentFolder();
            }
        }
    }

    private void FolderBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        SelectGpsRoot((folderBox.Text??String.Empty).Trim());
        ClearFullImageCache();
        ClearGallery();
        SetStatus("目录已变更，请扫描或创建分类。", false);
    }

    private void OpenFolderHistory(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Background = darkMode ? Brush("#242A31") : Brush("#FFFFFF"), Foreground = darkMode ? Brush("#F0F4F8") : Brush("#1F2937"), PlacementTarget = folderHistoryButton, Placement = PlacementMode.Bottom };
        StyleFolderHistoryMenu(menu);
        if (recentFolders.Count == 0)
        {
            var empty = new MenuItem { Header = "暂无最近目录", IsEnabled = false };
            StyleContextMenuItem(empty);
            menu.Items.Add(empty);
        }
        else
        {
            foreach (var folder in recentFolders)
            {
                var item = new MenuItem { Header = folder, Background = menu.Background, Foreground = menu.Foreground };
                StyleContextMenuItem(item);
                item.Click += delegate { folderBox.Text = folder; RefreshCurrentFolder(); };
                menu.Items.Add(item);
            }

            var clearHistory = new MenuItem
            {
                Header = "清除路径记录",
                Height = 30,
                Margin = new Thickness(8, 6, 8, 8),
                Padding = new Thickness(12, 0, 12, 0),
                Background = Brush("#8A4650"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "清除最近目录；当前目录保持打开"
            };
            StyleRoundedContextMenuAction(clearHistory);
            clearHistory.Click += delegate
            {
                menu.IsOpen = false;
                ClearFolderHistory();
            };
            menu.Items.Add(clearHistory);
        }
        menu.IsOpen = true;
    }

    private void ClearFolderHistory()
    {
        recentFolders.Clear();
        lastFolder = null;
        forgetFolderHistoryUntilNextScan = true;
        SaveSettings();
        SetStatus("路径记录已清除；当前目录仍可继续使用。重新选择或刷新后会再次记忆。", false);
    }

    private void ResetUiControls(string currentFolder)
    {
        folderBox = new TextBox { Text = currentFolder };
        folderHistoryButton = new Button();
        gallery = new WrapPanel();
        galleryScrollViewer = new ScrollViewer();
        status = new TextBlock();
        folderHint = new TextBlock();
        jpgCount = new TextBlock();
        rawCount = new TextBlock();
        pendingCount = new TextBlock();
        cullProgressCount = new TextBlock();
        cullProgressPercent = new TextBlock();
        pickedProgressCount = new TextBlock();
        rejectedProgressCount = new TextBlock();
        cullProgressFillColumn = new ColumnDefinition();
        cullProgressRemainingColumn = new ColumnDefinition();
        galleryCaption = new TextBlock();
        photoInfoTitle = new TextBlock();
        photoInfo = new TextBlock();
        gpsInfo = new TextBlock();
        gpsBadges.Clear();gpsSourceBar=new StackPanel();gpsSourceText=new TextBlock();
        gpsSetButton=new Button();gpsApplyButton=new Button();gpsClearButton=new Button();gpsGridSetButton=new Button();gpsGridApplyButton=new Button();
        singleImage = new Image();
        singleViewer = new ScrollViewer();
        singleCaption = new TextBlock();
        singleCullBadge = new Border();
        singleCullBadgeText = new TextBlock();
        advanceTargetButton = new Button();
        zoomBox = new TextBox();
        navigatorCanvas = new Canvas();
        navigatorImage = new Image();
        navigatorViewport = new Border();
        navigatorDragging = false;
        navigatorUpdateQueued = false;
        organizeButton = new Button();
        unorganizeButton = new Button();
        recycleSelectedButton = new Button();
        recycleRejectedButton = new Button();
        filterButton = new Button();
        filmStrip = new StackPanel();
        filmStripScrollViewer = new ScrollViewer();
        galleryThumbnailLoadVersion++;
        thumbnailLoadQueued = false;
        lazyThumbnailFiles.Clear();
        loadingThumbnailTiles.Clear();
        filmThumbnailLoadVersion++;
        filmThumbnailLoadQueued = false;
        lazyFilmThumbnailFiles.Clear();
        loadingFilmThumbnailTiles.Clear();
        tileFiles.Clear();
        filmTiles.Clear();
        selectedFiles.Clear();
        galleryFiles.Clear();
        selectionAnchor = null;
    }

    private void RememberFolder(string folder)
    {
        if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        forgetFolderHistoryUntilNextScan = false;
        recentFolders.RemoveAll(item => String.Equals(item, folder, StringComparison.OrdinalIgnoreCase));
        recentFolders.Insert(0, folder);
        if (recentFolders.Count > 3) recentFolders.RemoveRange(3, recentFolders.Count - 3);
        SaveSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            foreach (var line in File.ReadAllLines(settingsPath))
            {
                if (line.StartsWith("gps_exiftool_path=", StringComparison.OrdinalIgnoreCase))
                    gpsToolPath=line.Substring(18);
                else if (line.StartsWith("last_folder=", StringComparison.OrdinalIgnoreCase))
                    lastFolder = line.Substring(12);
                else if (line.StartsWith("view=", StringComparison.OrdinalIgnoreCase))
                    singleViewMode = String.Equals(line.Substring(5), "single", StringComparison.OrdinalIgnoreCase);
                else if (line.StartsWith("current_photo=", StringComparison.OrdinalIgnoreCase))
                    currentSingleFile = line.Substring(14);
                else if (line.StartsWith("single_fit=", StringComparison.OrdinalIgnoreCase))
                    fitSingleImage = String.Equals(line.Substring(11), "true", StringComparison.OrdinalIgnoreCase);
                else if (line.StartsWith("single_zoom=", StringComparison.OrdinalIgnoreCase))
                {
                    double zoom;
                    if (Double.TryParse(line.Substring(12), NumberStyles.Float, CultureInfo.InvariantCulture, out zoom))
                        singleZoom = Math.Max(0.1, Math.Min(6.0, zoom));
                }
                else if (line.StartsWith("grid_tile_width=", StringComparison.OrdinalIgnoreCase))
                {
                    double width;
                    if (Double.TryParse(line.Substring(16), NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                        gridTileWidth = Math.Max(160, Math.Min(280, width));
                }
                else if (line.StartsWith("sort=", StringComparison.OrdinalIgnoreCase))
                    gallerySortMode = NormalizeSortMode(line.Substring(5));
                else if (line.StartsWith("filter=", StringComparison.OrdinalIgnoreCase))
                    activeFilter = NormalizeFilter(line.Substring(7));
                else if (line.StartsWith("folder=", StringComparison.OrdinalIgnoreCase))
                {
                    var folder = line.Substring(7);
                    if (!String.IsNullOrWhiteSpace(folder) && !recentFolders.Any(item => String.Equals(item, folder, StringComparison.OrdinalIgnoreCase)))
                        recentFolders.Add(folder);
                }
            }
            if (recentFolders.Count > 3) recentFolders.RemoveRange(3, recentFolders.Count - 3);
            if (String.IsNullOrWhiteSpace(lastFolder)) lastFolder = recentFolders.FirstOrDefault();
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var lines = new List<string>();
            var currentFolder = folderBox == null ? lastFolder : (folderBox.Text ?? String.Empty).Trim();
            lines.Add("last_folder=" + (forgetFolderHistoryUntilNextScan ? String.Empty : (currentFolder ?? String.Empty)));
            lines.Add("gps_exiftool_path=" + (gpsToolPath ?? String.Empty));
            lines.Add("view=" + (singleViewMode ? "single" : "grid"));
            lines.Add("current_photo=" + (currentSingleFile ?? String.Empty));
            lines.Add("single_fit=" + (fitSingleImage ? "true" : "false"));
            lines.Add("single_zoom=" + singleZoom.ToString("0.########", CultureInfo.InvariantCulture));
            lines.Add("grid_tile_width=" + gridTileWidth.ToString("0", CultureInfo.InvariantCulture));
            lines.Add("sort=" + gallerySortMode);
            lines.Add("filter=" + activeFilter);
            if (!forgetFolderHistoryUntilNextScan)
                lines.AddRange(recentFolders.Take(3).Select(folder => "folder=" + folder));
            File.WriteAllLines(settingsPath, lines);
        }
        catch { }
    }

    private static string NormalizeSortMode(string value)
    {
        switch ((value ?? String.Empty).ToLowerInvariant())
        {
            case "name_desc":
            case "capture_asc":
            case "capture_desc":
                return value.ToLowerInvariant();
            // Migrate the two former modification-time settings to EXIF capture-time sorting.
            case "modified_asc":
                return "capture_asc";
            case "modified_desc":
                return "capture_desc";
            default:
                return "name_asc";
        }
    }

    private static string NormalizeFilter(string value)
    {
        switch ((value ?? String.Empty).ToLowerInvariant())
        {
            case "picked":
            case "rejected":
            case "undecided":
                return value.ToLowerInvariant();
            case "unmarked":
                return "undecided";
            default:
                return "all";
        }
    }

    private void RestoreSession()
    {
        var folder = (folderBox.Text ?? String.Empty).Trim();
        if (!Directory.Exists(folder))
        {
            singleViewMode = false;
            SetStatus("选择一个相机照片目录，然后扫描或创建分类。", false);
            return;
        }

        restoringCollectionSession=true;RefreshCurrentFolder();
    }

    private void LoadCullMarks()
    {
        try
        {
            var legacy = !File.Exists(cullMarksPath);
            var source = legacy ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate", "cull-marks.txt") : cullMarksPath;
            if (!File.Exists(source)) return;
            var lines = File.ReadAllLines(source);
            if (!legacy && (lines.Length == 0 || lines[0] != "RAWMate-CullMarks|2"))
                throw new InvalidDataException("无法识别挑片记录格式，已停止写入和清理。请保留记录文件。");
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length != 2) continue;
                // Old R rows were ratings, never reinterpret them as RAW-only decisions.
                if (legacy && parts[0] != "P" && parts[0] != "X") continue;
                if (!legacy && !new[] { "P", "J", "R", "X", "K" }.Contains(parts[0])) continue;
                try
                {
                    var file = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                    if (String.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file)) continue;
                    file = Path.GetFullPath(file);
                    if (parts[0] == "K" && IsRaw(file)) retainedRawFiles.Add(file);
                    else if (IsJpg(file)) SetDecision(file, parts[0]);
                }
                catch { if (!legacy) throw; }
            }
            // Recover a completed R operation if the process stopped after recycling its JPG.
            foreach (var jpg in rawOnlyFiles.Where(file => !File.Exists(file)).ToList())
            {
                if (retainedRawFiles.Any(raw => File.Exists(raw) && String.Equals(PhotoCollection.AbsolutePairKey(raw),
                    PhotoCollection.AbsolutePairKey(jpg),StringComparison.OrdinalIgnoreCase))) rawOnlyFiles.Remove(jpg);
            }
            // A K record is only meaningful once its JPG has gone. If an interrupted
            // R operation was later changed to another decision while the JPG still
            // existed, do not let the old survivor intent reappear on a reused path.
            foreach (var raw in retainedRawFiles.ToList())
            {
                string scope=Path.GetDirectoryName(PhotoCollection.AbsolutePairKey(raw));
                var jpg = PhotoCollection.ReadPairScope(scope,raw).Photos.Where(p=>p.IsJpg&&String.Equals(PhotoCollection.AbsolutePairKey(p.AbsolutePath),PhotoCollection.AbsolutePairKey(raw),StringComparison.OrdinalIgnoreCase)).Select(p=>p.AbsolutePath).FirstOrDefault();
                if (jpg != null && DecisionOf(jpg) != "R") retainedRawFiles.Remove(raw);
            }
        }
        catch (Exception ex)
        {
            cullMarksReadFailed = true;
            MessageBox.Show(ex.Message, "无法读取挑片记录", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveCullMarks()
    {
        if (cullMarksReadFailed) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cullMarksPath));
            var lines = new List<string> { "RAWMate-CullMarks|2" };
            foreach (var file in pickedFiles) lines.Add("P|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            foreach (var file in rejectedFiles) lines.Add("X|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            foreach (var file in jpgOnlyFiles) lines.Add("J|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            foreach (var file in rawOnlyFiles) lines.Add("R|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            foreach (var file in retainedRawFiles) lines.Add("K|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            var temporary = cullMarksPath + ".tmp";
            File.WriteAllLines(temporary, lines);
            if (File.Exists(cullMarksPath)) File.Replace(temporary, cullMarksPath, cullMarksPath + ".bak");
            else File.Move(temporary, cullMarksPath);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("挑片记录未能保存：" + ex.Message + "\n请解决后重试；后续清理已停止。", "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private string DecisionOf(string file)
    {
        if (file == null) return "";
        if (pickedFiles.Contains(file)) return "P";
        if (jpgOnlyFiles.Contains(file)) return "J";
        if (rawOnlyFiles.Contains(file)) return "R";
        if (rejectedFiles.Contains(file)) return "X";
        return "";
    }

    private bool IsKept(string file)
    {
        var decision = DecisionOf(file);
        return decision == "P" || decision == "J" || decision == "R";
    }

    private bool IsDecided(string file) { return DecisionOf(file).Length > 0; }

    private void SetDecision(string file, string decision)
    {
        if (decision != "R") RemoveRetainedRawIntentForJpg(file);
        pickedFiles.Remove(file);
        jpgOnlyFiles.Remove(file);
        rawOnlyFiles.Remove(file);
        rejectedFiles.Remove(file);
        if (decision == "P") pickedFiles.Add(file);
        if (decision == "J") jpgOnlyFiles.Add(file);
        if (decision == "R") rawOnlyFiles.Add(file);
        if (decision == "X") rejectedFiles.Add(file);
    }

    private void RemoveRetainedRawIntentForJpg(string jpg)
    {
        if (String.IsNullOrWhiteSpace(jpg)) return;
        foreach (var raw in retainedRawFiles.Where(raw => String.Equals(PhotoCollection.AbsolutePairKey(raw),PhotoCollection.AbsolutePairKey(jpg),StringComparison.OrdinalIgnoreCase)).ToList())
            retainedRawFiles.Remove(raw);
    }

    private void EnsureGalleryFiles()
    {
        if (galleryFiles.Count > 0) return;
        var root = RootFolder(false);
        if (root != null) galleryFiles.AddRange(SortGalleryFiles(CollectionJpgs(root)));
    }

    private List<string> SortGalleryFiles(IEnumerable<string> files)
    {
        var source = files.ToList();
        switch (gallerySortMode)
        {
            case "name_desc":
                return source.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
            case "capture_asc":
                return source.OrderBy(CaptureSortBucket).ThenBy(CaptureSortValue).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
            case "capture_desc":
                return source.OrderBy(CaptureSortBucket).ThenByDescending(CaptureSortValue).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
            default:
                return source.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private int CaptureSortBucket(string file)
    {
        return ReadCaptureDate(file).HasValue ? 0 : 1;
    }

    private DateTime CaptureSortValue(string file)
    {
        return ReadCaptureDate(file) ?? DateTime.MinValue;
    }

    private DateTime? ReadCaptureDate(string file)
    {
        DateTime? cached;
        if (IsCaptureDateCached(file))
        {
            lock (captureDateCacheLock)
                if (captureDateCache.TryGetValue(file, out cached)) return cached;
        }
        LoadGalleryMetadata(file);
        lock (captureDateCacheLock)
        {
            return captureDateCache.TryGetValue(file, out cached) ? cached : null;
        }
    }

    private void LoadGalleryMetadata(string file)
    {
        var stamp = FileStamp(file);
        var hasCaptureDate = IsCaptureDateCached(file);
        BitmapSource cachedThumbnail;
        int cachedWidth;
        var hasEmbeddedThumbnail = TryGetAnyCachedThumbnail(file, out cachedThumbnail, out cachedWidth) && cachedWidth >= 160;
        if (hasCaptureDate && hasEmbeddedThumbnail) return;

        DateTime? captureDate = null;
        BitmapSource embeddedThumbnail = null;
        try
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                if (decoder.Frames.Count > 0)
                {
                    var frame = decoder.Frames[0];
                    var metadata = frame.Metadata as BitmapMetadata;
                    captureDate = ParseExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36867}"));
                    if (!captureDate.HasValue) captureDate = ParseExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36868}"));
                    if (!captureDate.HasValue) captureDate = ParseExifDate(ReadMetadataValue(metadata, "/app1/ifd/{ushort=306}"));

                    if (!hasEmbeddedThumbnail && frame.Thumbnail != null)
                    {
                        var orientationValue = ReadMetadataValue(metadata, "/app1/ifd/{ushort=274}");
                        var orientation = orientationValue == null ? 1 : Convert.ToInt32(orientationValue, CultureInfo.InvariantCulture);
                        if (orientation < 1 || orientation > 8) orientation = 1;
                        embeddedThumbnail = frame.Thumbnail;
                        if (embeddedThumbnail.CanFreeze) embeddedThumbnail.Freeze();
                        embeddedThumbnail = ApplyExifOrientation(embeddedThumbnail, orientation);
                    }
                }
            }
        }
        catch { }
        lock (captureDateCacheLock)
        {
            if(stamp!=FileStamp(file))return;
            captureDateCache[file] = captureDate;
            captureDateStampCache[file] = stamp;
        }
        if (embeddedThumbnail != null)
            CacheThumbnail(file, Math.Max(embeddedThumbnail.PixelWidth, embeddedThumbnail.PixelHeight), embeddedThumbnail, stamp);
    }

    private bool IsCaptureDateCached(string file)
    {
        var stamp = FileStamp(file);
        lock (captureDateCacheLock)
        {
            string cachedStamp;
            if (stamp != null && captureDateCache.ContainsKey(file) && captureDateStampCache.TryGetValue(file, out cachedStamp)
                && String.Equals(stamp, cachedStamp, StringComparison.Ordinal)) return true;
            captureDateCache.Remove(file);
            captureDateStampCache.Remove(file);
            return false;
        }
    }

    private void PreloadGalleryMetadata(IEnumerable<string> files)
    {
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, LoadGalleryMetadata);
        SaveCaptureDateCache();
    }

    private void LoadCaptureDateCache()
    {
        try
        {
            if (!File.Exists(CaptureDateCachePath)) return;
            foreach (var line in File.ReadLines(CaptureDateCachePath))
            {
                var parts = line.Split(new[] { '|' }, 3);
                if (parts.Length != 3) continue;
                long ticks;
                var path = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                DateTime? date = null;
                if (parts[1] != "-" && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                    date = new DateTime(ticks, DateTimeKind.Unspecified);
                captureDateCache[path] = date;
                captureDateStampCache[path] = parts[0];
            }
        }
        catch
        {
            lock (captureDateCacheLock)
            {
                captureDateCache.Clear();
                captureDateStampCache.Clear();
            }
        }
    }

    private void SaveCaptureDateCache()
    {
        try
        {
            List<string> lines;
            lock (captureDateCacheLock)
            {
                lines = captureDateCache.Take(10000).Select(item =>
                {
                    string stamp;
                    if (!captureDateStampCache.TryGetValue(item.Key, out stamp) || String.IsNullOrEmpty(stamp)) return null;
                    var ticks = item.Value.HasValue ? item.Value.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "-";
                    return stamp + "|" + ticks + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Key));
                }).Where(line => line != null).ToList();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(CaptureDateCachePath));
            var temporary = CaptureDateCachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllLines(temporary, lines);
            if (File.Exists(CaptureDateCachePath)) File.Replace(temporary, CaptureDateCachePath, null);
            else File.Move(temporary, CaptureDateCachePath);
        }
        catch { }
    }

    private void InitializeNavigationForFolder(string root)
    {
        if (String.IsNullOrWhiteSpace(root))
        {
            navigationRoot = null;
            advanceToUndecided = true;
            UpdateAdvanceTargetButton();
            return;
        }

        var fullRoot = Path.GetFullPath(root);
        var pathRoot = Path.GetPathRoot(fullRoot);
        var normalizedRoot = String.Equals(fullRoot, pathRoot, StringComparison.OrdinalIgnoreCase)
            ? pathRoot
            : fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (String.Equals(navigationRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return;

        navigationRoot = normalizedRoot;
        // Navigation belongs to the current batch, not to a global preference.
        // An empty batch starts in culling mode; a completed batch opens ready
        // for sequential review.
        advanceToUndecided = FolderStartsInCullMode(normalizedRoot);
        UpdateAdvanceTargetButton();
    }

    private bool FolderStartsInCullMode(string root)
    {
        var jpgs = CollectionJpgs(root);
        return jpgs.Count == 0 || jpgs.Any(file => !IsDecided(file));
    }

    private static bool IsCullCompletionTransition(bool shouldAdvance, bool wasUndecided, int total, int undecidedAfter)
    {
        return shouldAdvance && wasUndecided && total > 0 && undecidedAfter == 0;
    }

    private void ScanDirectory()
    {
        var root = RootFolder(false);
        if (root == null) return;
        if(collectionUiEnabled&&(collection==null||!GpsFileSafety.Same(collection.Root,root))) {if(!collectionScanning)RefreshCurrentFolder();return;}
        InitializeNavigationForFolder(root);
        RememberFolder(root);
        var snapshot=CollectionState(root);
        var jpgTotal=snapshot.Photos.Count(p=>p.IsJpg);
        var rawTotal=snapshot.Photos.Count-jpgTotal;
        jpgCount.Text = jpgTotal.ToString("N0") + " 张";
        rawCount.Text = rawTotal.ToString("N0") + " 张";
        pendingCount.Text = snapshot.PendingCount.ToString("N0") + " 个";
        UpdateCullProgress();
        int classified=snapshot.Photos.Count(p=>!p.IsUnclassified&&p.RelativeDirectory.Length>0);
        organizeButton.IsHitTestVisible = !collectionScanning&&!classificationBusy&&snapshot.PendingCount>0;
        organizeButton.IsEnabled=organizeButton.IsHitTestVisible;
        organizeButton.Opacity = organizeButton.IsHitTestVisible?1.0:0.52;
        unorganizeButton.IsHitTestVisible = !collectionScanning&&!classificationBusy&&classified>0;
        unorganizeButton.IsEnabled=unorganizeButton.IsHitTestVisible;
        unorganizeButton.Opacity = unorganizeButton.IsHitTestVisible?1.0:0.52;
        folderHint.Text = "递归集合 · 待分类 "+snapshot.PendingCount+" 个；在各自父目录分类";
        SetStatus("扫描完成：JPG " + jpgTotal + " 张，RAW " + rawTotal + " 张。"+(snapshot.Diagnostics.Count>0?" 已跳过不可访问/链接项 "+snapshot.Diagnostics.Count+" 项。":""), false);
    }

    private PhotoCollection CollectionState(string root) {
        if(collection!=null&&GpsFileSafety.Same(collection.Root,root))return collection;
        // Headless model fixtures have no UI lifecycle. Real windows must never
        // perform a recursive fallback scan on the Dispatcher.
        return collectionUiEnabled?new PhotoCollection(root,new PhotoItem[0],new string[0]):PhotoCollection.Scan(root,CancellationToken.None);
    }
    private List<string> CollectionJpgs(string root) {return CollectionState(root).Photos.Where(p=>p.IsJpg).Select(p=>p.AbsolutePath).ToList();}

    private void OrganizeFiles(object sender, RoutedEventArgs e)
    {
        StartClassification(false);
    }

    private void UnorganizeFiles(object sender, RoutedEventArgs e)
    {
        StartClassification(true);
    }

    private void StartClassification(bool undo) {
        string root=RootFolder(true);if(root==null||classificationBusy||gpsBusy||cullMarksReadFailed)return;
        BeginClassificationContext();
        var cancel=new CancellationTokenSource();bool finished=false;var body=new StackPanel();var text=GpsDialogText("正在递归检查分类计划…");body.Children.Add(text);
        var stop=GpsSmallButton("取消剩余");body.Children.Add(stop);var window=GpsDialog(undo?"取消分类":"创建分类",body,520);
        stop.Click+=delegate{cancel.Cancel();stop.IsEnabled=false;text.Text+="\n已请求取消；当前文件及状态保存完成后停止。";};
        window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!finished){e.Cancel=true;cancel.Cancel();stop.IsEnabled=false;}};
        window.Loaded+=async delegate {
            ClassificationResult result=null;ClassificationPlan plan=null;string failure=null;
            try {
                plan=await Task.Factory.StartNew(delegate{return ClassificationPlan.Build(root,undo,cancel.Token);});
                cancel.Token.ThrowIfCancellationRequested();
                string summary="集合："+root+"\n\n可移动："+plan.ReadyCount+"\n冲突："+plan.ConflictCount+"\n跳过："+plan.Entries.Count(e=>e.Disposition=="skipped")+
                    "\n\n"+(undo?"在每个类型目录自己的父目录内还原。":"在每张照片自己的父目录内创建 jpg / arw。")+"\n保留日期目录；不覆盖、不自动改名。";
                if(plan.ReadyCount>0&&MessageBox.Show(window,summary,undo?"确认取消分类":"确认递归分类",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                result=await Task.Factory.StartNew(delegate{return ClassificationExecutor.Execute(plan,cancel.Token,delegate(string source,string destination){
                    return (bool)Dispatcher.Invoke(new Func<bool>(delegate{return CommitClassificationMove(source,destination,SaveCullMarks);}));
                },delegate(int i,int n,string file){Dispatcher.BeginInvoke(new Action(delegate{text.Text=(undo?"正在还原":"正在分类")+" "+i+" / "+n+"\n"+Path.GetFileName(file);}));});});
            }catch(OperationCanceledException){}catch(Exception error){failure=error.Message;}
            finally {
                finished=true;window.Close();EndClassificationContext(result);
                if(failure!=null)MessageBox.Show(this,failure,"分类未完成",MessageBoxButton.OK,MessageBoxImage.Warning);
                else if(result!=null){
                    var details=result.Conflicts.Concat(result.Skipped).Concat(result.Failed).Concat(plan.Diagnostics).ToList();
                    string summary="成功："+result.Moved.Count+"\n冲突："+result.Conflicts.Count+"\n跳过："+result.Skipped.Count+"\n失败："+result.Failed.Count+
                        (result.Cancelled?"\n已取消剩余。":"")+(details.Count>0?"\n\n"+String.Join("\n",details.Take(10)):"");
                    MessageBox.Show(this,summary,undo?"取消分类完成":"分类完成",MessageBoxButton.OK,result.Failed.Count>0?MessageBoxImage.Warning:MessageBoxImage.Information);
                }
                cancel.Dispose();
            }
        };
        window.ShowDialog();
    }

    private void BeginClassificationContext() {
        classificationBusy=true;
        if(collectionCancellation!=null)collectionCancellation.Cancel();Interlocked.Increment(ref collectionGeneration);
        Interlocked.Increment(ref gpsDisplayGeneration);Interlocked.Increment(ref gpsContextEpoch);collectionScanning=false;gpsDirectoryRefreshing=false;
        gpsRefreshSourcePath=gpsSession.Source==null?gpsRefreshSourcePath:gpsSession.Source.SourcePath;
        if(gpsSourceWatcher!=null){gpsSourceWatcher.Dispose();gpsSourceWatcher=null;}if(gpsSourcePoll!=null)gpsSourcePoll.Stop();
    }
    private void EndClassificationContext(ClassificationResult result) {
        classificationBusy=false;
        if(result!=null&&result.Moved.Count>0)RemoveImageCachesForPaths(result.Moved.Keys.Concat(result.Moved.Values));
        gpsSession.Clear();WatchGpsSource();RefreshCurrentFolder();
    }

    private bool CommitClassificationMove(string source,string destination,Func<bool> persist) {
        var sets=new[]{pickedFiles,jpgOnlyFiles,rawOnlyFiles,rejectedFiles,retainedRawFiles,selectedFiles};
        var prior=sets.Select(s=>Tuple.Create(s.Contains(source),s.Contains(destination))).ToArray();string current=currentSingleFile;
        try {
            for(int i=0;i<sets.Length;i++){sets[i].Remove(source);sets[i].Remove(destination);if(prior[i].Item1)sets[i].Add(destination);}
            if(String.Equals(currentSingleFile,source,StringComparison.OrdinalIgnoreCase))currentSingleFile=destination;
            if(!persist())throw new IOException("无法保存路径迁移状态");
        }catch {
            for(int i=0;i<sets.Length;i++){sets[i].Remove(source);sets[i].Remove(destination);if(prior[i].Item1)sets[i].Add(source);if(prior[i].Item2)sets[i].Add(destination);}
            currentSingleFile=current;return false;
        }
        if(gpsRefreshSourcePath!=null&&GpsFileSafety.Same(gpsRefreshSourcePath,source))gpsRefreshSourcePath=destination;
        foreach(var tile in tileFiles.Where(p=>String.Equals(p.Value,source,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key).ToArray())tileFiles[tile]=destination;
        gpsDisplayCache.Remove(source);gpsDisplayCache.Remove(destination);
        return true;
    }

    private void RemapPhotoState(string source, string destination)
    {
        if (pickedFiles.Remove(source)) pickedFiles.Add(destination);
        if (rejectedFiles.Remove(source)) rejectedFiles.Add(destination);
        if (jpgOnlyFiles.Remove(source)) jpgOnlyFiles.Add(destination);
        if (rawOnlyFiles.Remove(source)) rawOnlyFiles.Add(destination);
        if (retainedRawFiles.Remove(source)) retainedRawFiles.Add(destination);
        if (selectedFiles.Remove(source)) selectedFiles.Add(destination);
        if (String.Equals(currentSingleFile, source, StringComparison.OrdinalIgnoreCase)) currentSingleFile = destination;
    }

    private void RefreshGallery()
    {
        ClearGallery();
        var root = RootFolder(false);
        if (root == null) return;
        var sourceJpgs = CollectionJpgs(root);
        if ((gallerySortMode == "capture_asc" || gallerySortMode == "capture_desc") && sourceJpgs.Any(file => !IsCaptureDateCached(file)))
        {
            var version = galleryThumbnailLoadVersion;
            galleryCaption.Text = "正在读取 " + sourceJpgs.Count.ToString("N0") + " 张照片信息…";
            gallery.Children.Add(new TextBlock
            {
                Text = "正在准备缩略图…",
                Margin = new Thickness(20),
                FontSize = 15,
                Foreground = Brush("#75869A")
            });
            Task.Factory.StartNew(delegate { PreloadGalleryMetadata(sourceJpgs); }).ContinueWith(delegate(Task task)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
                {
                    if (version != galleryThumbnailLoadVersion || singleViewMode) return;
                    var currentRoot = RootFolder(false);
                    if (!String.Equals(currentRoot, root, StringComparison.OrdinalIgnoreCase)) return;
                    PopulateGallery(sourceJpgs);
                    RestoreGridCurrentPhoto();
                }));
            });
            return;
        }
        PopulateGallery(sourceJpgs);
    }

    private void PopulateGallery(IEnumerable<string> sourceJpgs)
    {
        gallery.Children.Clear();
        var jpgs = SortGalleryFiles(sourceJpgs);
        galleryFiles.AddRange(jpgs);
        var visibleJpgs = jpgs.Where(MatchesFilter).ToList();
        if (visibleJpgs.Count == 0)
        {
            gallery.Children.Add(new TextBlock { Text = jpgs.Count == 0 ? (collectionScanning?"正在递归扫描集合…":"此集合没有可浏览的 JPG/JPEG。") : "当前筛选没有匹配的照片。", Margin = new Thickness(20), FontSize = 15, Foreground = Brush("#6A7787") });
            galleryCaption.Text = jpgs.Count == 0 ? "没有可浏览的 JPG" : "筛选：" + FilterLabel() + " · 0 张";
            return;
        }
        foreach (var jpg in visibleJpgs) gallery.Children.Add(CreateTile(jpg));
        QueueVisibleThumbnailLoads();
        galleryCaption.Text = "筛选：" + FilterLabel() + " · " + visibleJpgs.Count + "/" + jpgs.Count + " 张 · 单击选择，双击单张浏览";
        SetStatus("P 双格式 · J 仅 JPG · R 仅 RAW · X / Delete 废片，标记后自动前进\n← / → 切换照片 · 空格切换单张与网格视图", false);
    }

    private bool MatchesFilter(string file)
    {
        switch (activeFilter)
        {
            case "picked": return IsKept(file);
            case "rejected": return rejectedFiles.Contains(file);
            case "undecided": return !IsDecided(file);
            default: return true;
        }
    }

    private void RefreshCurrentFolder()
    {
        var root = RootFolder(false);
        // The restored view can exist before its GPS session/navigation root is
        // initialized. GPS session identity must not decide whether to discard
        // that view's current photo, selection or browse/cull mode.
        bool sameRoot=root!=null&&(String.Equals(root,navigationRoot,StringComparison.OrdinalIgnoreCase)||
            (navigationRoot==null&&((currentSingleFile!=null&&PhotoCollection.ContainsPath(root,currentSingleFile))||
                galleryFiles.Any(f=>PhotoCollection.ContainsPath(root,f)))));
        if(!String.Equals(root,gpsSession.Root,StringComparison.OrdinalIgnoreCase))SelectGpsRoot(root);
        if(sameRoot&&navigationRoot==null)navigationRoot=root;
        var previousSingleFile = currentSingleFile;

        if (root == null)
        {
            if(collectionCancellation!=null)collectionCancellation.Cancel();Interlocked.Increment(ref collectionGeneration);
            collectionScanning=false;gpsDirectoryRefreshing=false;collection=null;
            navigationRoot = null;
            advanceToUndecided = true;
            currentSingleFile = null;
            singleViewMode = false;
            RebuildUi();
            return;
        }

        if(collectionCancellation!=null)collectionCancellation.Cancel();
        var cancel=new CancellationTokenSource();collectionCancellation=cancel;
        int generation=Interlocked.Increment(ref collectionGeneration);
        Interlocked.Increment(ref gpsDisplayGeneration);Interlocked.Increment(ref gpsContextEpoch);
        gpsDisplayPending.Clear();gpsDirectoryRefreshing=true;collectionScanning=true;
        if(collection==null||!GpsFileSafety.Same(collection.Root,root))collection=new PhotoCollection(root,new PhotoItem[0],new string[0]);
        organizeButton.IsHitTestVisible=false;unorganizeButton.IsHitTestVisible=false;
        organizeButton.Opacity=unorganizeButton.Opacity=0.52;
        UpdateRejectedButtonState();SetStatus("正在递归扫描集合…",false);
        bool capture=gallerySortMode=="capture_asc"||gallerySortMode=="capture_desc";
        Task.Factory.StartNew(delegate {
            var found=PhotoCollection.Scan(root,cancel.Token);
            if(capture)foreach(var item in found.Photos.Where(p=>p.IsJpg)) {cancel.Token.ThrowIfCancellationRequested();if(!IsCaptureDateCached(item.AbsolutePath))LoadGalleryMetadata(item.AbsolutePath);}
            cancel.Token.ThrowIfCancellationRequested();return found;
        },cancel.Token).ContinueWith(delegate(Task<PhotoCollection> task) {
            if(Dispatcher.HasShutdownStarted)return;
            Dispatcher.BeginInvoke(new Action(delegate {
                if(generation!=collectionGeneration||cancel.IsCancellationRequested||!String.Equals(RootFolder(false),root,StringComparison.OrdinalIgnoreCase))return;
                collectionScanning=false;
                if(task.Status!=TaskStatus.RanToCompletion){gpsDirectoryRefreshing=false;SetStatus("集合扫描未完成，请重新刷新。",true);return;}
                collection=task.Result;ScanDirectory();
                var files=collection.Photos.Where(p=>p.IsJpg).Select(p=>p.AbsolutePath).ToList();
                BeginGpsDirectoryRefresh(root,files);
                RefreshDirectoryView(SortGalleryFiles(files),sameRoot?currentSingleFile:previousSingleFile,sameRoot);
                // Startup initially has only a grid shell, even when restoring
                // a single-photo session. Build it once after the scan completes.
                if(restoringCollectionSession){
                    restoringCollectionSession=false;
                    if(singleViewMode&&currentSingleFile!=null){RebuildUi();ShowPhotoInfo(currentSingleFile);}
                    else if(!singleViewMode&&currentSingleFile!=null)RestoreGridCurrentPhoto();
                }
            }));
        });
    }

    private void RefreshDirectoryView(List<string> files,string previousSingleFile,bool sameRoot) {
        int viewGeneration=gpsDisplayGeneration;
        bool keepCurrent=sameRoot&&!String.IsNullOrWhiteSpace(previousSingleFile)&&files.Contains(previousSingleFile,StringComparer.OrdinalIgnoreCase);
        bool sameFiles=sameRoot&&galleryFiles.SequenceEqual(files,StringComparer.OrdinalIgnoreCase);
        var selection=sameRoot?selectedFiles.Where(f=>files.Contains(f,StringComparer.OrdinalIgnoreCase)).ToArray():new string[0];
        string anchor=selectionAnchor!=null&&tileFiles.ContainsKey(selectionAnchor)?tileFiles[selectionAnchor]:null;
        double horizontal=singleViewer.HorizontalOffset,vertical=singleViewer.VerticalOffset;
        double gridOffset=galleryScrollViewer.VerticalOffset;
        currentSingleFile=keepCurrent?previousSingleFile:(singleViewMode?files.FirstOrDefault():null);
        if(!sameFiles) {
            if(singleViewMode) {
                galleryFiles.Clear();galleryFiles.AddRange(files);
                if(String.IsNullOrWhiteSpace(currentSingleFile)){singleViewMode=false;RebuildUi();}
                else {
                    if(!keepCurrent)fitSingleImage=true;
                    RebuildUi();
                    if(keepCurrent)Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {
                        if(viewGeneration==gpsDisplayGeneration&&singleViewMode&&String.Equals(currentSingleFile,previousSingleFile,StringComparison.OrdinalIgnoreCase)) {
                            singleViewer.ScrollToHorizontalOffset(horizontal);singleViewer.ScrollToVerticalOffset(vertical);QueueNavigatorUpdate();
                        }
                    }));
                }
            } else {
                ClearGallery();PopulateGallery(files);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {if(viewGeneration==gpsDisplayGeneration)galleryScrollViewer.ScrollToVerticalOffset(gridOffset);}));
            }
            selectedFiles.Clear();foreach(var file in selection)selectedFiles.Add(file);
            selectionAnchor=tileFiles.FirstOrDefault(p=>String.Equals(p.Value,anchor,StringComparison.OrdinalIgnoreCase)).Key;
            UpdateTileSelectionAppearance();
            recycleSelectedButton.IsHitTestVisible=selectedFiles.Count>0;recycleSelectedButton.Opacity=selectedFiles.Count>0?1:0.52;
        }
        // Do not rebuild an unchanged view: preserve the main image, zoom,
        // navigator/scroll position, multi-selection and all culling state.
        UpdateGpsLine();
    }

    private static string PreferredPhotoAfterCleanup(IEnumerable<string> orderedFiles, string activeFile, IEnumerable<string> successPaths)
    {
        var files = orderedFiles == null ? new List<string>() : orderedFiles.ToList();
        if (files.Count == 0) return null;
        var removedJpgs = new HashSet<string>((successPaths ?? Enumerable.Empty<string>()).Where(IsJpg), StringComparer.OrdinalIgnoreCase);
        if (!String.IsNullOrWhiteSpace(activeFile) && !removedJpgs.Contains(activeFile) && File.Exists(activeFile)) return activeFile;

        var activeIndex = files.FindIndex(file => String.Equals(file, activeFile, StringComparison.OrdinalIgnoreCase));
        if (activeIndex < 0) activeIndex = files.Count - 1;
        for (var offset = 1; offset <= files.Count; offset++)
        {
            var candidate = files[(activeIndex + offset) % files.Count];
            if (!removedJpgs.Contains(candidate) && File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private void RefreshAfterCleanup(string preferredFile)
    {
        var root = RootFolder(false);
        if (root == null)
        {
            currentSingleFile = null;
            singleViewMode = false;
            RebuildUi();
            return;
        }

        currentSingleFile = preferredFile;
        RefreshCurrentFolder();
    }

    private void ClearGallery()
    {
        galleryThumbnailLoadVersion++;
        thumbnailLoadQueued = false;
        lazyThumbnailFiles.Clear();
        loadingThumbnailTiles.Clear();
        gallery.Children.Clear();
        tileFiles.Clear();
        galleryFiles.Clear();
        selectedFiles.Clear();
        selectionAnchor = null;
        recycleSelectedButton.IsHitTestVisible = false;
        recycleSelectedButton.Opacity = 0.52;
        UpdateRejectedButtonState();
        photoInfoTitle.Text = "照片信息";
        photoInfo.Text = "点击缩略图查看拍摄信息";
        UpdateCullProgress();
    }

    private Border CreateTile(string file)
    {
        var previewHeight = Math.Round(gridTileWidth * 0.724);
        var tile = new Border { Width = gridTileWidth, Margin = new Thickness(6), Padding = new Thickness(6), Background = Brush("#FFFFFF"), BorderBrush = Brush("#D6E0EA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand };
        var stack = new StackPanel();
        var previewBorder = new Border { Height = previewHeight, Background = Brush("#EAF0F5"), CornerRadius = new CornerRadius(4), ClipToBounds = true };
        var decodeWidth = (int)Math.Round(gridTileWidth * 1.33);
        BitmapSource cachedThumbnail;
        int cachedWidth;
        if (TryGetAnyCachedThumbnail(file, out cachedThumbnail, out cachedWidth))
            previewBorder.Child = CreateThumbnailImage(cachedThumbnail);
        else
            previewBorder.Child = new TextBlock { Text = "正在加载…", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (cachedThumbnail == null || cachedWidth < decodeWidth) lazyThumbnailFiles[previewBorder] = file;
        var previewGrid = new Grid { Height = previewHeight };
        previewGrid.Children.Add(previewBorder);
        var gpsBadge=new Border{Width=20,Height=20,Background=Brush("#D9202833"),CornerRadius=new CornerRadius(3),Margin=new Thickness(5),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Focusable=false};
        gpsBadge.Child=new System.Windows.Shapes.Path{Width=14,Height=14,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        gpsBadges[gpsBadge]=file;previewGrid.Children.Add(gpsBadge);UpdateGpsBadge(gpsBadge,file);QueueGpsDisplay(file);
        var mark = MarkLabel(file);
        if (!String.IsNullOrWhiteSpace(mark))
        {
            var badge = new Border { Background = Brush("#202833"), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.92 };
            badge.Child = new TextBlock { Text = mark, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            previewGrid.Children.Add(badge);
        }
        stack.Children.Add(previewGrid);
        var fileName = Path.GetFileName(file);
        stack.Children.Add(new TextBlock
        {
            Text = fileName,
            Margin = new Thickness(1, 7, 1, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = fileName,
            FontSize = 12,
            Foreground = Brush("#2D3A4A")
        });
        tile.Child = stack;
        tileFiles.Add(tile, file);
        tile.MouseLeftButtonDown += TileMouseDown;
        tile.ContextMenu = BuildTileMenu(file);
        return tile;
    }

    private void QueueVisibleThumbnailLoads()
    {
        if (singleViewMode || thumbnailLoadQueued || Dispatcher.HasShutdownStarted) return;
        thumbnailLoadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
        {
            thumbnailLoadQueued = false;
            LoadVisibleThumbnails();
        }));
    }

    private void LoadVisibleThumbnails()
    {
        if (singleViewMode || galleryScrollViewer == null || !galleryScrollViewer.IsLoaded) return;
        var version = galleryThumbnailLoadVersion;
        var pending = new Queue<Tuple<Border, string, int>>();
        foreach (var item in lazyThumbnailFiles.ToList())
        {
            var preview = item.Key;
            if (loadingThumbnailTiles.Contains(preview) || !IsThumbnailNearViewport(preview)) continue;
            loadingThumbnailTiles.Add(preview);
            pending.Enqueue(Tuple.Create(preview, item.Value, (int)Math.Round(gridTileWidth * 1.33)));
        }
        if (pending.Count == 0) return;

        var queueLock = new object();
        var workerCount = Math.Min(2, pending.Count);
        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            Task.Factory.StartNew(delegate
            {
                while (version == galleryThumbnailLoadVersion)
                {
                    Tuple<Border, string, int> work;
                    lock (queueLock)
                    {
                        if (pending.Count == 0) return;
                        work = pending.Dequeue();
                    }

                    var preview = work.Item1;
                    var file = work.Item2;
                    BitmapSource quickThumbnail = null;
                    try { quickThumbnail = LoadEmbeddedThumbnail(file); }
                    catch { }
                    if (quickThumbnail != null)
                    {
                        var quickResult = quickThumbnail;
                        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                        {
                            string expected;
                            if (version != galleryThumbnailLoadVersion || !lazyThumbnailFiles.TryGetValue(preview, out expected) ||
                                !String.Equals(expected, file, StringComparison.OrdinalIgnoreCase)) return;
                            preview.Child = CreateThumbnailImage(quickResult);
                        }));
                    }

                    if (version != galleryThumbnailLoadVersion) return;
                    BitmapSource qualityThumbnail = null;
                    var qualityLoaded = false;
                    try
                    {
                        qualityThumbnail = LoadCachedThumbnail(file, work.Item3);
                        qualityLoaded = qualityThumbnail != null;
                    }
                    catch { }

                    var finalResult = qualityThumbnail;
                    var finalLoaded = qualityLoaded;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        loadingThumbnailTiles.Remove(preview);
                        string expected;
                        if (version != galleryThumbnailLoadVersion || !lazyThumbnailFiles.TryGetValue(preview, out expected) ||
                            !String.Equals(expected, file, StringComparison.OrdinalIgnoreCase)) return;
                        if (finalLoaded)
                            preview.Child = CreateThumbnailImage(finalResult);
                        else if (!(preview.Child is Image))
                            preview.Child = new TextBlock { Text = "无法预览", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        lazyThumbnailFiles.Remove(preview);
                    }));
                }
            });
        }
    }

    private bool IsThumbnailNearViewport(Border preview)
    {
        if (!preview.IsLoaded || galleryScrollViewer.ViewportHeight <= 0) return false;
        try
        {
            var bounds = preview.TransformToAncestor(galleryScrollViewer).TransformBounds(
                new Rect(0, 0, Math.Max(1, preview.ActualWidth), Math.Max(1, preview.ActualHeight)));
            var buffer = galleryScrollViewer.ViewportHeight;
            return bounds.Bottom >= -buffer && bounds.Top <= galleryScrollViewer.ViewportHeight + buffer;
        }
        catch { return false; }
    }

    private string MarkLabel(string file)
    {
        return DecisionOf(file);
    }

    private ContextMenu BuildTileMenu(string file)
    {
        var menu = new ContextMenu { Background = Brush("#FFFFFF"), Foreground = Brush("#1F2937"), Padding = new Thickness(0), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
        StyleFolderHistoryMenu(menu);
        var reject = new MenuItem { Header = "标记为废片" };
        StyleContextMenuItem(reject);
        reject.Click += delegate { ToggleReject(file); };
        menu.Items.Add(reject);
        var pick = new MenuItem { Header = "保留双格式" };
        StyleContextMenuItem(pick);
        pick.Click += delegate { TogglePick(file); };
        menu.Items.Add(pick);
        var keepJpg = new MenuItem { Header = "保留 JPG" };
        StyleContextMenuItem(keepJpg);
        keepJpg.Click += delegate { ToggleDecision(file, "J", false); };
        menu.Items.Add(keepJpg);
        var keepRaw = new MenuItem { Header = "保留 RAW" };
        StyleContextMenuItem(keepRaw);
        keepRaw.Click += delegate { ToggleDecision(file, "R", false); };
        menu.Items.Add(keepRaw);
        menu.Items.Add(new Separator{Background=Brush("#36404B"),Margin=new Thickness(6,3,6,3)});
        var source=new MenuItem{Header="设为 GPS 来源"};var apply=new MenuItem{Header="应用 GPS 到当前照片"};
        foreach(var item in new[]{source,apply}){StyleContextMenuItem(item);menu.Items.Add(item);}
        source.Click+=delegate {var files=SelectedGpsFiles(file);if(files.Length==1)SetGpsSource(files[0]);};
        apply.Click+=delegate {ApplyGpsSelection(SelectedGpsFiles(file));};
        menu.Opened+=delegate {
            var files=SelectedGpsFiles(file);bool valid=gpsSession.IsValid();
            source.IsEnabled=files.Length==1&&GpsUxState.CanSetSource(GetGpsDisplay(files[0]),IsGpsSource(files[0]),gpsBusy);
            apply.Header=singleViewMode?"应用 GPS 到当前照片":"应用 GPS 到所选照片";
            apply.IsEnabled=!gpsBusy&&valid&&files.Any(IsLegalGpsCandidate);UpdateGpsLine();
        };
        return menu;
    }

    private void TileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) OpenTile(sender, e);
        else ToggleTile(sender, e);
    }

    private void ToggleTile(object sender, MouseButtonEventArgs e)
    {
        var tile = sender as Border;
        if (tile == null || !tileFiles.ContainsKey(tile)) return;
        var file = tileFiles[tile];
        currentSingleFile = file;
        ShowPhotoInfo(file);
        var modifiers = Keyboard.Modifiers;
        var controlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (shiftPressed && selectionAnchor != null && tileFiles.ContainsKey(selectionAnchor))
        {
            if (!controlPressed) selectedFiles.Clear();
            var tiles = gallery.Children.OfType<Border>().Where(item => tileFiles.ContainsKey(item)).ToList();
            var start = tiles.IndexOf(selectionAnchor);
            var end = tiles.IndexOf(tile);
            if (start >= 0 && end >= 0)
            {
                var first = Math.Min(start, end);
                var last = Math.Max(start, end);
                for (var index = first; index <= last; index++) selectedFiles.Add(tileFiles[tiles[index]]);
            }
        }
        else if (controlPressed)
        {
            if (!selectedFiles.Add(file)) selectedFiles.Remove(file);
            selectionAnchor = tile;
        }
        else
        {
            selectedFiles.Clear();
            selectedFiles.Add(file);
            selectionAnchor = tile;
        }

        UpdateTileSelectionAppearance();
        recycleSelectedButton.IsHitTestVisible = selectedFiles.Count > 0;
        recycleSelectedButton.Opacity = selectedFiles.Count > 0 ? 1.0 : 0.52;
        galleryCaption.Text = selectedFiles.Count > 0 ? "已选择 " + selectedFiles.Count + " 张 · 可批量标记废片" : tileFiles.Count + " 张 JPG · 单击选择，双击单张浏览";
        UpdateGpsLine();
    }

    private void RestoreGridCurrentPhoto()
    {
        if (String.IsNullOrWhiteSpace(currentSingleFile) || !File.Exists(currentSingleFile)) return;
        var tile = tileFiles.FirstOrDefault(item => String.Equals(item.Value, currentSingleFile, StringComparison.OrdinalIgnoreCase)).Key;
        if (tile == null) return;
        selectedFiles.Clear();
        selectedFiles.Add(currentSingleFile);
        selectionAnchor = tile;
        ShowPhotoInfo(currentSingleFile);
        UpdateTileSelectionAppearance();
        recycleSelectedButton.IsHitTestVisible = true;
        recycleSelectedButton.Opacity = 1.0;
    }

    private void UpdateTileSelectionAppearance()
    {
        foreach (var item in tileFiles)
        {
            var selected = selectedFiles.Contains(item.Value);
            item.Key.Background = selected ? Brush("#EAF3FF") : Brush("#FFFFFF");
            item.Key.BorderBrush = selected ? Brush("#2678D4") : Brush("#D6E0EA");
            item.Key.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox || Keyboard.FocusedElement is ComboBox) return;
        if (e.Key == Key.Left)
        {
            if (singleViewMode) NavigateSingle(-1); else SelectRelativeInGrid(-1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Right)
        {
            if (singleViewMode) NavigateSingle(1); else SelectRelativeInGrid(1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space)
        {
            SetViewMode(!singleViewMode);
            e.Handled = true;
            return;
        }
        var active = ActivePhotoFile();
        if (String.IsNullOrWhiteSpace(active)) return;
        if (e.Key == Key.P)
        {
            TogglePick(active, true);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.X || e.Key == Key.Delete)
        {
            ToggleReject(active, true);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.J || e.Key == Key.R)
        {
            ToggleDecision(active, e.Key == Key.J ? "J" : "R", true);
            e.Handled = true;
            return;
        }
    }

    private string ActivePhotoFile()
    {
        if (singleViewMode && !String.IsNullOrWhiteSpace(currentSingleFile)) return currentSingleFile;
        if (!String.IsNullOrWhiteSpace(currentSingleFile) && selectedFiles.Contains(currentSingleFile)) return currentSingleFile;
        var selected = selectedFiles.FirstOrDefault();
        if (!String.IsNullOrWhiteSpace(selected)) return selected;
        return galleryFiles.FirstOrDefault(MatchesFilter);
    }

    private string NextUndecidedPhoto(string active)
    {
        return UndecidedPhotoInDirection(active, 1);
    }

    private string PreviousUndecidedPhoto(string active)
    {
        return UndecidedPhotoInDirection(active, -1);
    }

    private string UndecidedPhotoInDirection(string active, int direction)
    {
        if (String.IsNullOrWhiteSpace(active)) return null;
        EnsureGalleryFiles();
        if (galleryFiles.Count == 0) return null;
        var step = direction < 0 ? -1 : 1;
        var index = galleryFiles.FindIndex(item => String.Equals(item, active, StringComparison.OrdinalIgnoreCase));
        var candidateCount = index < 0 ? galleryFiles.Count : galleryFiles.Count - 1;
        if (index < 0) index = step > 0 ? -1 : 0;
        for (var offset = 1; offset <= candidateCount; offset++)
        {
            var candidateIndex = (index + step * offset) % galleryFiles.Count;
            if (candidateIndex < 0) candidateIndex += galleryFiles.Count;
            var candidate = galleryFiles[candidateIndex];
            if (!IsDecided(candidate) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private string NextPhotoInCurrentSort(string active)
    {
        if (String.IsNullOrWhiteSpace(active)) return null;
        EnsureGalleryFiles();
        if (galleryFiles.Count == 0) return null;
        var index = galleryFiles.FindIndex(item => String.Equals(item, active, StringComparison.OrdinalIgnoreCase));
        var candidateCount = index < 0 ? galleryFiles.Count : galleryFiles.Count - 1;
        for (var offset = 1; offset <= candidateCount; offset++)
        {
            var candidate = galleryFiles[(index + offset + galleryFiles.Count) % galleryFiles.Count];
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private void SelectRelativeInGrid(int direction)
    {
        EnsureGalleryFiles();
        if (galleryFiles.Count == 0) return;
        var active = selectedFiles.FirstOrDefault();
        if (advanceToUndecided && active != null)
        {
            var next = UndecidedPhotoInDirection(active, direction);
            if (next == null) { SetStatus("没有其他未决定照片。", false); return; }
            SelectGridFile(next);
            return;
        }
        var index = galleryFiles.FindIndex(item => String.Equals(item, active, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = direction > 0 ? -1 : 0;
        index = (index + direction + galleryFiles.Count) % galleryFiles.Count;
        SelectGridFile(galleryFiles[index]);
    }

    private void SelectGridFile(string file)
    {
        selectedFiles.Clear();
        selectedFiles.Add(file);
        currentSingleFile = file;
        selectionAnchor = tileFiles.FirstOrDefault(item => String.Equals(item.Value, file, StringComparison.OrdinalIgnoreCase)).Key;
        ShowPhotoInfo(file);
        UpdateTileSelectionAppearance();
        recycleSelectedButton.IsHitTestVisible = true;
        recycleSelectedButton.Opacity = 1.0;
    }

    private void TogglePick(string file, bool advanceRequested = false)
    {
        ToggleDecision(file, "P", advanceRequested);
    }

    private void ToggleReject(string file, bool advanceRequested = false)
    {
        ToggleDecision(file, "X", advanceRequested);
    }

    private void ToggleDecision(string file, string decision, bool advanceRequested)
    {
        if (cullMarksReadFailed || !File.Exists(file)) return;
        var previous = DecisionOf(file);
        var wasUndecided = previous.Length == 0;
        var becameUndecided = previous == decision;
        if (!becameUndecided && (decision == "P" || decision == "R"))
        {
            var root = RootFolder(false);
            var pairs = root == null ? null : PhotoCollection.ReadPairScope(root,file);
            var key=root==null?null:new PhotoItem(root,file).PairKey;
            if (pairs==null||!pairs.RawPairs.ContainsKey(key)||pairs.RawPairs[key].Count!=1||!pairs.JpgPairs.ContainsKey(key)||pairs.JpgPairs[key].Count!=1||pairs.Diagnostics.Count>0)
            {
                MessageBox.Show("没有找到唯一的同名 RAW。请先进行文件对应检查；仅有 JPG 时可选择 J。", "无法保留 RAW", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        SetDecision(file, becameUndecided ? "" : decision);
        if (!SaveCullMarks()) { SetDecision(file, previous); return; }
        ApplyCullChange(file, "决定已更新；执行清理前不会移动文件。", advanceRequested, wasUndecided, becameUndecided);
    }

    private void ApplyCullChange(string file, string message, bool advanceRequested, bool wasUndecided, bool becameUndecided)
    {
        EnsureGalleryFiles();
        var cullingFilter = activeFilter == "all" || activeFilter == "undecided";
        var shouldAdvance = advanceRequested && !becameUndecided && cullingFilter;
        // Completion is a state transition, not merely the fact that the batch is
        // currently at 100%. Reclassifying an already-decided photo must not show
        // the completion dialog again.
        var undecidedAfter = galleryFiles.Count(item => !IsDecided(item));
        var completed = IsCullCompletionTransition(shouldAdvance, wasUndecided, galleryFiles.Count, undecidedAfter);
        var nextFile = shouldAdvance && !completed
            ? (advanceToUndecided ? NextUndecidedPhoto(file) : NextPhotoInCurrentSort(file))
            : null;
        var targetFile = file;
        var advanceMessage = String.Empty;

        if (becameUndecided)
        {
            if (!advanceToUndecided)
            {
                var undecided = galleryFiles.Count(item => !IsDecided(item));
                advanceMessage = " 当前有 " + undecided.ToString("N0") + " 张未决定照片，可切换到“挑片”继续处理。";
            }
            else advanceMessage = " 当前照片已取消旗标并回到未决定。";
        }
        else if (shouldAdvance && !completed)
        {
            if (!String.IsNullOrWhiteSpace(nextFile) && File.Exists(nextFile))
            {
                targetFile = nextFile;
                advanceMessage = advanceToUndecided
                    ? " 已跳到下一张未决定照片。"
                    : " 已按当前排序跳到下一张照片。";
            }
        }
        else if (advanceRequested && !cullingFilter)
            advanceMessage = " 当前为复查筛选，仅刷新旗标结果。";

        currentSingleFile = targetFile;
        if (singleViewMode)
        {
            // Flagging a photo must not tear down the entire single-photo view.
            // Rebuilding here discarded every filmstrip thumbnail and made a
            // 33-photo folder visibly reload for several seconds.
            UpdateCullProgress();
            UpdateSingleCullStatus();
            UpdateFilmStripSelection();
            if (!String.Equals(file, targetFile, StringComparison.OrdinalIgnoreCase))
            {
                ShowPhotoInfo(currentSingleFile);
                LoadSinglePhoto();
            }
            else singleViewer.ContextMenu = BuildTileMenu(currentSingleFile);
        }
        else
        {
            // A mark changes only culling state. Rebuilding the whole window would
            // rescan the folder and recreate every shell control for no benefit.
            RefreshGallery();
            currentSingleFile = targetFile;
            RestoreGridCurrentPhoto();
        }
        if (completed)
        {
            var completedRaw = CompletedRawOnlyInRoot(RootFolder(false)).Count;
            var picked = galleryFiles.Count(IsKept) + completedRaw;
            var rejected = galleryFiles.Count(rejectedFiles.Contains);
            var total = galleryFiles.Count + completedRaw;
            message = "所有照片均已完成去留标记。";
            SetStatus(message, false);
            MessageBox.Show(
                "已决定 " + total.ToString("N0") + " / " + total.ToString("N0") + "\n" +
                "保留 " + picked.ToString("N0") + "\n" +
                "废片 " + rejected.ToString("N0") + "\n\n" +
                "双格式 " + galleryFiles.Count(pickedFiles.Contains).ToString("N0") + "\n" +
                "仅 JPG " + galleryFiles.Count(jpgOnlyFiles.Contains).ToString("N0") + "\n" +
                "仅 RAW " + (galleryFiles.Count(rawOnlyFiles.Contains) + completedRaw).ToString("N0") + "\n\n点击“执行清理”后确认处理文件。",
                "挑片完成", MessageBoxButton.OK, MessageBoxImage.Information);
            // A completed culling task becomes a review task. Keep the current
            // photo in place; only the navigation semantics change after OK.
            advanceToUndecided = false;
            var restoreAllFilter = activeFilter == "undecided";
            if (restoreAllFilter) activeFilter = "all";
            UpdateAdvanceTargetButton();
            if (restoreAllFilter && !singleViewMode)
            {
                RefreshGallery();
                currentSingleFile = file;
                RestoreGridCurrentPhoto();
            }
            SetStatus("挑片已完成，已切换到“浏览”；可用前后导航逐张复查。", false);
            return;
        }
        else message += advanceMessage;
        SetStatus(message, false);
    }

    private void OpenTile(object sender, MouseButtonEventArgs e)
    {
        var tile = sender as Border;
        if (tile == null || !tileFiles.ContainsKey(tile)) return;
        ShowSinglePhoto(tileFiles[tile]);
    }

    private void SetViewMode(bool single)
    {
        if (single && String.IsNullOrWhiteSpace(currentSingleFile))
        {
            currentSingleFile = selectedFiles.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(currentSingleFile)) currentSingleFile = galleryFiles.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(currentSingleFile))
            {
                SetStatus("请先扫描并选择一张 JPG。", true);
                return;
            }
        }
        singleViewMode = single;
        RebuildUi();
        if (single && !String.IsNullOrWhiteSpace(currentSingleFile)) ShowPhotoInfo(currentSingleFile);
    }

    private void ShowSinglePhoto(string file)
    {
        if (String.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        currentSingleFile = file;
        singleViewMode = true;
        fitSingleImage = true;
        RebuildUi();
        ShowPhotoInfo(file);
    }

    private void RebuildUi()
    {
        // Every visible control must be recreated. Reusing even the folder input while the old tree is active
        // causes WPF to throw "already a logical child" during a grid/single-view switch.
        var currentFolder = folderBox.Text;
        ResetUiControls(currentFolder);
        Content = BuildUi();
        if (Directory.Exists(currentFolder.Trim()))
        {
            ScanDirectory();
            // BuildSingleView already enumerates the JPG folder for the filmstrip.
            // Rebuilding the hidden grid here decodes every thumbnail a second time
            // and was the main source of the visible pause when pressing Space.
            if (!singleViewMode)
            {
                RefreshGallery();
                RestoreGridCurrentPhoto();
            }
        }
        else SetStatus("选择一个相机照片目录，然后扫描或创建分类。", false);
    }

    private void NavigateSingle(int direction)
    {
        if (galleryFiles.Count == 0)
        {
            var root = RootFolder(false);
            if (root != null) galleryFiles.AddRange(SortGalleryFiles(CollectionJpgs(root)));
        }
        var index = galleryFiles.FindIndex(item => String.Equals(item, currentSingleFile, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || galleryFiles.Count == 0) return;
        if (advanceToUndecided)
        {
            var undecided = direction < 0
                ? PreviousUndecidedPhoto(currentSingleFile)
                : NextUndecidedPhoto(currentSingleFile);
            if (String.IsNullOrWhiteSpace(undecided))
            {
                SetStatus("没有其他未决定照片。可切换到“浏览”逐张查看。", false);
                return;
            }
            currentSingleFile = undecided;
            SetStatus(direction < 0 ? "已跳到上一张未决定照片。" : "已跳到下一张未决定照片。", false);
        }
        else
        {
            index = (index + direction + galleryFiles.Count) % galleryFiles.Count;
            currentSingleFile = galleryFiles[index];
        }
        ShowPhotoInfo(currentSingleFile);
        LoadSinglePhoto();
        UpdateFilmStripSelection();
    }

    private void LoadSinglePhoto()
    {
        if (singleImage == null) return;
        var file = currentSingleFile;
        if (String.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        var targetImage = singleImage;
        var version = ++singleLoadVersion;
        singleCaption.Text = Path.GetFileName(file);
        singleCaption.ToolTip = singleCaption.Text;
        UpdateSingleCullStatus();
        singleViewer.ContextMenu = BuildTileMenu(file);
        targetImage.Source = null;
        navigatorImage.Source = null;
        GetFullImageTask(file).ContinueWith(delegate(Task<BitmapSource> task)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                if (version != singleLoadVersion || !singleViewMode || targetImage != singleImage ||
                    !String.Equals(file, currentSingleFile, StringComparison.OrdinalIgnoreCase)) return;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    targetImage.Source = task.Result;
                    navigatorImage.Source = task.Result;
                    ApplySingleZoom();
                    QueueNavigatorUpdate();
                    PreloadAdjacentPhotos(file);
                }
                else
                {
                    targetImage.Source = null;
                    navigatorImage.Source = null;
                    singleCaption.Text = "无法载入原图";
                    zoomBox.Text = "—";
                }
            }));
        });
    }

    private Task<BitmapSource> GetFullImageTask(string file)
    {
        lock (fullImageCacheLock)
        {
            BitmapSource cached;
            if (fullImageCache.TryGetValue(file, out cached))
            {
                TouchFullImageCacheLocked(file);
                var completion = new TaskCompletionSource<BitmapSource>();
                completion.SetResult(cached);
                return completion.Task;
            }

            Task<BitmapSource> existing;
            if (fullImageLoadTasks.TryGetValue(file, out existing)) return existing;

            var generation = fullImageCacheGeneration;
            int pathEpoch;imagePathEpochs.TryGetValue(file,out pathEpoch);
            var task = Task.Factory.StartNew(delegate { return LoadOrientedJpeg(file, 0); });
            fullImageLoadTasks[file] = task;
            task.ContinueWith(delegate(Task<BitmapSource> completed)
            {
                lock (fullImageCacheLock)
                {
                    Task<BitmapSource> registered;
                    if(fullImageLoadTasks.TryGetValue(file,out registered)&&Object.ReferenceEquals(registered,completed)) fullImageLoadTasks.Remove(file);
                    int nowEpoch;imagePathEpochs.TryGetValue(file,out nowEpoch);
                    if (completed.Status == TaskStatus.RanToCompletion && generation == fullImageCacheGeneration && pathEpoch==nowEpoch)
                        AddFullImageCacheLocked(file, completed.Result);
                }
            });
            return task;
        }
    }

    private void PreloadAdjacentPhotos(string file)
    {
        if (galleryFiles.Count < 2) return;
        var index = galleryFiles.FindIndex(item => String.Equals(item, file, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var next = galleryFiles[(index + 1) % galleryFiles.Count];
        // Culling primarily moves forward. Loading both neighbours at full camera
        // resolution made startup and every cache miss compete for CPU and memory.
        // The previous image is already retained by the LRU after a forward move.
        if (File.Exists(next)) GetFullImageTask(next);
    }

    private void AddFullImageCacheLocked(string file, BitmapSource image)
    {
        fullImageCache[file] = image;
        TouchFullImageCacheLocked(file);
        while (fullImageCacheLru.Count > 3)
        {
            var oldest = fullImageCacheLru.First.Value;
            fullImageCacheLru.RemoveFirst();
            fullImageCache.Remove(oldest);
        }
    }

    private void TouchFullImageCacheLocked(string file)
    {
        var node = fullImageCacheLru.Find(file);
        if (node != null) fullImageCacheLru.Remove(node);
        fullImageCacheLru.AddLast(file);
    }

    private BitmapSource LoadCachedThumbnail(string file, int decodePixelWidth)
    {
        var readStamp=FileStamp(file);
        BitmapSource cached;
        int cachedWidth;
        if (TryGetAnyCachedThumbnail(file, out cached, out cachedWidth) && cachedWidth >= decodePixelWidth)
            return cached;

        if (TryLoadPersistentThumbnail(file, out cached))
            return CacheThumbnail(file, PersistentThumbnailWidth, cached, readStamp);

        var targetWidth = Math.Max(PersistentThumbnailWidth, decodePixelWidth);
        var loaded = LoadOrientedJpeg(file, targetWidth);
        var result = CacheThumbnail(file, targetWidth, loaded, readStamp);
        SavePersistentThumbnail(file, result);
        return result;
    }

    private BitmapSource LoadEmbeddedThumbnail(string file)
    {
        var readStamp=FileStamp(file);
        BitmapSource cached;
        int cachedWidth;
        if (TryGetAnyCachedThumbnail(file, out cached, out cachedWidth) && cachedWidth >= 160)
            return cached;
        if (TryLoadPersistentThumbnail(file, out cached))
            return CacheThumbnail(file, PersistentThumbnailWidth, cached, readStamp);
        LoadGalleryMetadata(file);
        return TryGetAnyCachedThumbnail(file, out cached, out cachedWidth) ? cached : null;
    }

    private static string PersistentThumbnailPath(string file)
    {
        var stamp = FileStamp(file);
        if (stamp == null) return null;
        var identity = Path.GetFullPath(file).ToUpperInvariant() + "|" + stamp;
        using (var hash = SHA256.Create())
        {
            var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(identity));
            return Path.Combine(ThumbnailCacheDirectory, BitConverter.ToString(bytes).Replace("-", String.Empty) + ".png");
        }
    }

    private static bool TryLoadPersistentThumbnail(string file, out BitmapSource image)
    {
        image = null;
        try
        {
            var cacheFile = PersistentThumbnailPath(file);
            if (cacheFile == null || !File.Exists(cacheFile)) return false;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(cacheFile, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            File.SetLastWriteTimeUtc(cacheFile, DateTime.UtcNow);
            image = bitmap;
            return true;
        }
        catch { return false; }
    }

    private static void SavePersistentThumbnail(string file, BitmapSource image)
    {
        if (image == null) return;
        string temporary = null;
        try
        {
            var cacheFile = PersistentThumbnailPath(file);
            if (cacheFile == null || File.Exists(cacheFile)) return;
            Directory.CreateDirectory(ThumbnailCacheDirectory);
            temporary = cacheFile + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }
            if (!File.Exists(cacheFile)) File.Move(temporary, cacheFile);
            else File.Delete(temporary);
        }
        catch
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private BitmapSource CacheThumbnail(string file, int decodePixelWidth, BitmapSource loaded, string expectedStamp = null)
    {
        if (loaded == null) return null;
        lock (thumbnailCacheLock)
        {
            string stamp=FileStamp(file);
            if(expectedStamp!=null&&stamp!=expectedStamp)return null;
            Tuple<int, BitmapSource> existing;
            string oldStamp;
            if (thumbnailStamps.TryGetValue(file,out oldStamp)&&oldStamp==stamp&&thumbnailCache.TryGetValue(file, out existing) && existing.Item1 >= decodePixelWidth)
            {
                TouchThumbnailCacheLocked(file);
                return existing.Item2;
            }
            thumbnailCache[file] = Tuple.Create(decodePixelWidth, loaded);thumbnailStamps[file]=stamp;
            TouchThumbnailCacheLocked(file);
            while (thumbnailCacheLru.Count > MaxThumbnailCacheItems)
            {
                var oldest = thumbnailCacheLru.First.Value;
                thumbnailCacheLru.RemoveFirst();
                thumbnailCache.Remove(oldest);thumbnailStamps.Remove(oldest);
            }
        }
        return loaded;
    }

    private bool TryGetAnyCachedThumbnail(string file, out BitmapSource image, out int decodePixelWidth)
    {
        lock (thumbnailCacheLock)
        {
            Tuple<int, BitmapSource> cached;
            string stamp;
            if (thumbnailStamps.TryGetValue(file,out stamp)&&stamp==FileStamp(file)&&thumbnailCache.TryGetValue(file, out cached))
            {
                decodePixelWidth = cached.Item1;
                image = cached.Item2;
                TouchThumbnailCacheLocked(file);
                return true;
            }
        }
        decodePixelWidth = 0;
        image = null;
        return false;
    }

    private void TouchThumbnailCacheLocked(string file)
    {
        var node = thumbnailCacheLru.Find(file);
        if (node != null) thumbnailCacheLru.Remove(node);
        thumbnailCacheLru.AddLast(file);
    }

    private static Image CreateThumbnailImage(BitmapSource source)
    {
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private void ClearFullImageCache()
    {
        lock (fullImageCacheLock)
        {
            fullImageCacheGeneration++;
            fullImageCache.Clear();
            fullImageLoadTasks.Clear();
            fullImageCacheLru.Clear();
        }
        lock (thumbnailCacheLock)
        {
            thumbnailCache.Clear();thumbnailStamps.Clear();
            thumbnailCacheLru.Clear();
        }
        // Capture dates are stamp-validated and safely reusable across folder switches.
        // Keeping them avoids re-reading EXIF for every photo when returning to a folder.
    }

    private void RemoveImageCachesForPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(IsJpg).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            lock (fullImageCacheLock)
            {
                int epoch;imagePathEpochs.TryGetValue(path,out epoch);imagePathEpochs[path]=epoch+1;
                fullImageCache.Remove(path);
                fullImageLoadTasks.Remove(path);
                var node = fullImageCacheLru.Find(path);
                if (node != null) fullImageCacheLru.Remove(node);
            }
            lock (thumbnailCacheLock)
            {
                thumbnailCache.Remove(path);thumbnailStamps.Remove(path);
                var node = thumbnailCacheLru.Find(path);
                if (node != null) thumbnailCacheLru.Remove(node);
            }
            lock (captureDateCacheLock)
            {
                captureDateCache.Remove(path);
                captureDateStampCache.Remove(path);
            }
        }
    }

    private void SingleViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        ChangeSingleZoom(e.Delta > 0 ? 1.25 : 1.0 / 1.25);
        e.Handled = true;
    }

    private void SingleViewerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (singleImage.Source == null) return;
        singlePanStart = e.GetPosition(singleViewer);
        singlePanHorizontal = singleViewer.HorizontalOffset;
        singlePanVertical = singleViewer.VerticalOffset;
        singlePanning = false;
        singleViewer.CaptureMouse();
    }

    private void SingleViewerMouseMove(object sender, MouseEventArgs e)
    {
        if (!singleViewer.IsMouseCaptured) return;
        var point = e.GetPosition(singleViewer);
        var dx = point.X - singlePanStart.X;
        var dy = point.Y - singlePanStart.Y;
        if (!singlePanning && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
        singlePanning = true;
        singleViewer.Cursor = Cursors.SizeAll;
        singleViewer.ScrollToHorizontalOffset(singlePanHorizontal - dx);
        singleViewer.ScrollToVerticalOffset(singlePanVertical - dy);
        e.Handled = true;
    }

    private void SingleViewerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!singleViewer.IsMouseCaptured) return;
        singleViewer.ReleaseMouseCapture();
        singleViewer.Cursor = Cursors.Hand;
        if (singlePanning) { e.Handled = true; return; }
        ToggleSingleClickZoom();
        e.Handled = true;
    }

    private void ToggleSingleClickZoom()
    {
        if (singleImage.Source == null) return;
        if (fitSingleImage)
        {
            fitSingleImage = false;
            singleZoom = GetSingleInspectionZoom();
            ApplySingleZoom();
        }
        else
        {
            fitSingleImage = true;
            ApplySingleZoom();
        }
    }

    private double GetSingleInspectionZoom()
    {
        var source = singleImage.Source as BitmapSource;
        if (source == null) return 1.0;
        return CalculateSingleFitZoom(source) >= 1.0 ? 2.0 : 1.0;
    }

    private double CalculateSingleFitZoom(BitmapSource source)
    {
        if (source == null) return 1.0;
        var width = singleViewer.ActualWidth - 16;
        var height = singleViewer.ActualHeight - 16;
        if (width <= 0 || height <= 0 || source.Width <= 0 || source.Height <= 0)
            return Math.Max(0.1, Math.Min(6.0, singleZoom));
        return Math.Max(0.1, Math.Min(6.0, Math.Min(width / source.Width, height / source.Height)));
    }

    private void ZoomBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyZoomText();
        singleViewer.Focus();
        e.Handled = true;
    }

    private void ApplyZoomText()
    {
        var text = (zoomBox.Text ?? String.Empty).Trim().TrimEnd('%').Trim();
        double percent;
        if (!Double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out percent) && !Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            ApplySingleZoom();
            return;
        }
        fitSingleImage = false;
        singleZoom = Math.Max(0.1, Math.Min(6.0, percent / 100.0));
        ApplySingleZoom();
    }

    private void ChangeSingleZoom(double factor)
    {
        if (singleImage.Source == null) return;
        fitSingleImage = false;
        singleZoom = Math.Max(0.1, Math.Min(6.0, singleZoom * factor));
        ApplySingleZoom();
    }

    private void ApplySingleZoom()
    {
        var source = singleImage.Source as BitmapSource;
        if (source == null) return;
        if (fitSingleImage)
            singleZoom = CalculateSingleFitZoom(source);
        singleImage.LayoutTransform = new ScaleTransform(singleZoom, singleZoom);
        zoomBox.Text = Math.Round(singleZoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
        QueueNavigatorUpdate();
    }

    private void OpenPhoto(string file)
    {
        try { Process.Start(file); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开照片", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private sealed class CleanupItem
    {
        public string Jpg;
        public string Raw;
        public string Decision;
        public string JpgStamp;
        public string RawStamp;
    }

    private sealed class CleanupPlan
    {
        public string Root;
        public readonly List<CleanupItem> Items = new List<CleanupItem>();
        public readonly List<string> Issues = new List<string>();
    }

    private sealed class CorrespondenceReport
    {
        public int Paired;
        public int JpgOnly;
        public int RawOnly;
        public int UnrecordedSingles;
        public readonly List<string> Issues = new List<string>();
    }

    private static string FileStamp(string file)
    {
        if (String.IsNullOrEmpty(file) || !File.Exists(file)) return null;
        var info = new FileInfo(file);
        return info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    private static bool HasReparsePoint(string file)
    {
        return (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0;
    }

    private List<string> CompletedRawOnlyInRoot(string root)
    {
        if (String.IsNullOrWhiteSpace(root)) return new List<string>();
        var snapshot=CollectionState(root);
        return snapshot.Photos.Where(p=>!p.IsJpg&&retainedRawFiles.Contains(p.AbsolutePath)&&!snapshot.JpgPairs.ContainsKey(p.PairKey)).Select(p=>p.AbsolutePath).ToList();
    }

    private CleanupPlan BuildCleanupPlan(string root)
    {
        return BuildCleanupFromCollection(PhotoCollection.Scan(root,CancellationToken.None));
    }

    private CleanupPlan BuildCleanupFromCollection(PhotoCollection snapshot)
    {
        var plan = new CleanupPlan { Root = snapshot.Root };
        plan.Issues.AddRange(snapshot.Diagnostics);
        // Incomplete enumeration is fine for browsing, not evidence of an
        // absent partner. A cleanup plan must be complete or fail closed.
        if(snapshot.Diagnostics.Count>0)return plan;
        foreach (var group in snapshot.JpgPairs)
        {
            var marked = group.Value.Select(p=>p.AbsolutePath).Where(file => new[] { "J", "R", "X" }.Contains(DecisionOf(file))).ToList();
            if (marked.Count == 0) continue;
            List<PhotoItem> raws;
            if (!snapshot.RawPairs.TryGetValue(group.Key, out raws)) raws = new List<PhotoItem>();
            if (group.Value.Count != 1 || raws.Count > 1)
            {
                plan.Issues.Add(group.Key + "：同名主体存在多个文件，未加入清理。");
                continue;
            }
            var jpg = marked[0];
            var raw = raws.Count==0?null:raws[0].AbsolutePath;
            var decision = DecisionOf(jpg);
            if (!PhotoCollection.SafePath(jpg) || (raw != null && !PhotoCollection.SafePath(raw)))
            {
                plan.Issues.Add(group.Key + "：文件是链接，未加入清理。");
                continue;
            }
            if (decision == "R" && raw == null)
            {
                plan.Issues.Add(group.Key + "：选择仅 RAW，但 RAW 不存在，JPG 已保护。");
                continue;
            }
            if (decision == "J" && raw == null) continue;
            plan.Items.Add(new CleanupItem { Jpg = jpg, Raw = raw, Decision = decision, JpgStamp = FileStamp(jpg), RawStamp = FileStamp(raw) });
        }
        return plan;
    }

    private void RecycleSelectedPairs(object sender, RoutedEventArgs e)
    {
        if (cullMarksReadFailed) return;
        var root = RootFolder(false);
        if (root == null) return;
        var files = CollectionJpgs(root).Where(selectedFiles.Contains).ToList();
        if (files.Count == 0) return;
        var previous = files.ToDictionary(file => file, DecisionOf, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) SetDecision(file, "X");
        if (!SaveCullMarks())
        {
            foreach (var item in previous) SetDecision(item.Key, item.Value);
            return;
        }
        var active = currentSingleFile;
        if (singleViewMode)
        {
            UpdateSingleCullStatus();
            UpdateCullProgress();
        }
        else
        {
            RefreshGallery();
            currentSingleFile = active;
            RestoreGridCurrentPhoto();
        }
        SetStatus("已将 " + files.Count + " 张选中照片标记为废片；执行清理前不会移动文件。", false);
    }

    private async void RecycleRejectedPairs(object sender, RoutedEventArgs e)
    {
        var root = RootFolder(true);
        if (root == null || cullMarksReadFailed) return;
        int generation=collectionGeneration;
        var snapshot=await Task.Factory.StartNew(delegate {return PhotoCollection.Scan(root,CancellationToken.None);});
        if(generation!=collectionGeneration||!String.Equals(root,RootFolder(false),StringComparison.OrdinalIgnoreCase))return;
        var plan = BuildCleanupFromCollection(snapshot);
        if (plan.Items.Count == 0)
        {
            MessageBox.Show("当前目录没有待执行的 J / R / X 清理。" +
                (plan.Issues.Count > 0 ? "\n\n" + String.Join("\n", plan.Issues.Take(8)) : ""),
                "无需清理", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var jpgCountToRecycle = plan.Items.Count(item => item.Decision == "R" || item.Decision == "X");
        var rawCountToRecycle = plan.Items.Count(item => item.Raw != null && (item.Decision == "J" || item.Decision == "X"));
        var message = "当前目录：\n" + plan.Root + "\n\n" +
            "J 仅 JPG：" + plan.Items.Count(item => item.Decision == "J") + " 张，回收对应 RAW\n" +
            "R 仅 RAW：" + plan.Items.Count(item => item.Decision == "R") + " 张，回收对应 JPG\n" +
            "X 废片：" + plan.Items.Count(item => item.Decision == "X") + " 张，回收 JPG 与 RAW\n\n" +
            "共移入 Windows 回收站：JPG " + jpgCountToRecycle + " 个，RAW " + rawCountToRecycle + " 个。\n" +
            "P 和未决定照片保持原样。文件可从回收站恢复。\n" +
            (plan.Issues.Count > 0 ? "\n跳过以下异常：\n" + String.Join("\n", plan.Issues.Take(8)) + "\n" : "") +
            "\n确定执行清理吗？";
        if (MessageBox.Show(message, "确认执行清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        EnsureGalleryFiles();
        var orderedBefore = galleryFiles.ToList();
        if (orderedBefore.Count == 0)
            orderedBefore = SortGalleryFiles(CollectionJpgs(root));
        var activeBefore = ActivePhotoFile();
        var result = ExecuteCleanupPlan(plan, MoveToRecycleBin, SaveCullMarks);
        var preferred = PreferredPhotoAfterCleanup(orderedBefore, activeBefore, result.SuccessPaths);
        RemoveImageCachesForPaths(result.SuccessPaths);
        RefreshAfterCleanup(preferred);
        ShowRecycleResult(result);
    }

    private RecycleResult ExecuteCleanupPlan(CleanupPlan plan, Func<string, bool> recycle, Func<bool> persist)
    {
        var result = new RecycleResult();
        if (!persist()) { result.Notes.Add("无法保存决定，本次未开始清理。"); return result; }
        foreach (var item in plan.Items)
        {
            var current = BuildCleanupFromCollection(PhotoCollection.ReadPairScope(plan.Root,item.Jpg)).Items.FirstOrDefault(candidate =>
                String.Equals(candidate.Jpg, item.Jpg, StringComparison.OrdinalIgnoreCase));
            // Never act on a changed file/decision or on a path outside the confirmed root.
            if (current == null || !String.Equals(current.Raw, item.Raw, StringComparison.OrdinalIgnoreCase) ||
                !PhotoCollection.ContainsPath(plan.Root,item.Jpg) ||
                (item.Raw != null && (!PhotoCollection.ContainsPath(plan.Root,item.Raw)||!String.Equals(new PhotoItem(plan.Root,item.Jpg).PairKey,new PhotoItem(plan.Root,item.Raw).PairKey,StringComparison.OrdinalIgnoreCase))) ||
                DecisionOf(item.Jpg) != item.Decision ||
                FileStamp(item.Jpg) != item.JpgStamp || FileStamp(item.Jpg) == null ||
                FileStamp(item.Raw) != item.RawStamp ||
                !PhotoCollection.SafePath(item.Jpg) || (item.Raw != null && !PhotoCollection.SafePath(item.Raw)))
            {
                result.Notes.Add(Path.GetFileName(item.Jpg) + "：文件或决定已变化，本组未处理，请重新检查。");
                continue;
            }
            var rawIntentAdded = false;
            if (item.Decision == "R")
            {
                // Persist the survivor intent first so a process interruption after recycling
                // the JPG cannot turn this RAW into an unexplained orphan.
                if (item.Raw == null || !File.Exists(item.Raw))
                {
                    result.Notes.Add(Path.GetFileName(item.Jpg) + "：RAW 已不存在，JPG 已保护。");
                    continue;
                }
                rawIntentAdded = retainedRawFiles.Add(item.Raw);
                if (!persist())
                {
                    if (rawIntentAdded) retainedRawFiles.Remove(item.Raw);
                    result.Notes.Add("无法保存 RAW 保留记录，清理已停止。");
                    break;
                }
            }
            var targets = new List<string>();
            if (item.Decision == "J") targets.Add(item.Raw);
            if (item.Decision == "R") targets.Add(item.Jpg);
            if (item.Decision == "X")
            {
                // RAW first: a failed RAW recycle must leave its X-marked JPG for retry.
                if (item.Raw != null) targets.Add(item.Raw);
                targets.Add(item.Jpg);
            }
            foreach (var target in targets)
            {
                var attempt = RecycleFiles(new[] { target }, recycle);
                result.SuccessPaths.AddRange(attempt.SuccessPaths);
                result.FailedPaths.AddRange(attempt.FailedPaths);
                RemoveCullMarksForSuccessfulJpgs(attempt.SuccessPaths);
                foreach (var success in attempt.SuccessPaths.Where(IsRaw)) retainedRawFiles.Remove(success);
                if (attempt.FailedPaths.Count > 0)
                {
                    if (item.Decision == "X" && target == item.Raw)
                        result.Notes.Add(Path.GetFileName(item.Jpg) + "：RAW 回收失败，JPG 和 X 状态已保留。");
                    break;
                }
            }
            if (item.Decision == "R" && File.Exists(item.Jpg) && rawIntentAdded)
                retainedRawFiles.Remove(item.Raw);
            if (!persist())
            {
                result.Notes.Add("记录保存失败，后续清理已停止；请保留记录文件并重新检查。");
                break;
            }
        }
        return result;
    }

    private CorrespondenceReport InspectCorrespondence(string root)
    {
        return InspectCollectionCorrespondence(PhotoCollection.Scan(root,CancellationToken.None));
    }

    private CorrespondenceReport InspectCollectionCorrespondence(PhotoCollection snapshot)
    {
        string root=snapshot.Root;
        var report = new CorrespondenceReport();
        report.Issues.AddRange(snapshot.Diagnostics);
        var jpgGroups = snapshot.JpgPairs.ToDictionary(g=>g.Key,g=>g.Value.Select(p=>p.AbsolutePath).ToList(),StringComparer.OrdinalIgnoreCase);
        var rawGroups = snapshot.RawPairs.ToDictionary(g=>g.Key,g=>g.Value.Select(p=>p.AbsolutePath).ToList(),StringComparer.OrdinalIgnoreCase);
        var raws=snapshot.Photos.Where(p=>!p.IsJpg).Select(p=>p.AbsolutePath).ToList();
        foreach (var stem in jpgGroups.Keys.Concat(rawGroups.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            List<string> js, rs;
            if (!jpgGroups.TryGetValue(stem, out js)) js = new List<string>();
            if (!rawGroups.TryGetValue(stem, out rs)) rs = new List<string>();
            if (js.Count > 1 || rs.Count > 1)
            {
                report.Issues.Add(stem + "：同名主体存在多个文件。");
                continue;
            }
            if (js.Count == 1 && rs.Count == 1) report.Paired++;
            else if (js.Count == 1)
            {
                report.JpgOnly++;
                var decision = DecisionOf(js[0]);
                if (decision == "P" || decision == "R") report.Issues.Add(stem + "：决定需要 RAW，但 RAW 缺失。");
                else if (decision == "") report.UnrecordedSingles++;
            }
            else
            {
                report.RawOnly++;
                if (!retainedRawFiles.Contains(rs[0])) report.UnrecordedSingles++;
            }
        }
        foreach (var file in pickedFiles.Concat(jpgOnlyFiles).Concat(rawOnlyFiles).Concat(rejectedFiles)
            .Where(file => PhotoCollection.ContainsPath(root,file) && !File.Exists(file)))
        {
            var expectedRawSurvived = rawOnlyFiles.Contains(file) && raws.Any(raw =>
                retainedRawFiles.Contains(raw) && String.Equals(new PhotoItem(root,raw).PairKey,new PhotoItem(root,file).PairKey,StringComparison.OrdinalIgnoreCase));
            if (!expectedRawSurvived) report.Issues.Add(Path.GetFileName(file) + "：仍有决定记录，但 JPG 已不存在。");
        }
        foreach (var raw in retainedRawFiles.Where(raw => PhotoCollection.ContainsPath(root,raw) && !File.Exists(raw)))
            report.Issues.Add(Path.GetFileName(raw) + "：记录为仅 RAW 保留，但 RAW 已不存在。");
        return report;
    }

    private async void CheckMissingPairs(object sender, RoutedEventArgs e)
    {
        var root = RootFolder(true);
        if (root == null) return;
        int generation=collectionGeneration;
        var snapshot=await Task.Factory.StartNew(delegate {return PhotoCollection.Scan(root,CancellationToken.None);});
        if(generation!=collectionGeneration||!String.Equals(root,RootFolder(false),StringComparison.OrdinalIgnoreCase))return;
        var report = InspectCollectionCorrespondence(snapshot);
        var plan = BuildCleanupFromCollection(snapshot);
        var text = "完整配对：" + report.Paired + "\n仅 JPG：" + report.JpgOnly + "\n仅 RAW：" + report.RawOnly +
            "\n异常：" + report.Issues.Count + "\n\n待执行清理：" + plan.Items.Count + " 组" +
            "\n未记录保留意图的单格式：" + report.UnrecordedSingles +
            "\n\nJ / R 主动保留的单格式属于正常结果。\n本次检查只报告，不移动文件。" +
            (report.Issues.Count > 0 ? "\n\n" + String.Join("\n", report.Issues.Take(12)) : "") +
            (plan.Issues.Count > 0 ? "\n\n清理保护：\n" + String.Join("\n", plan.Issues.Take(8)) : "");
        MessageBox.Show(text, "文件检查完成", MessageBoxButton.OK,
            report.Issues.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static RecycleResult RecycleFiles(IEnumerable<string> files)
    {
        return RecycleFiles(files, MoveToRecycleBin);
    }

    private static RecycleResult RecycleFiles(IEnumerable<string> files, Func<string, bool> recycleFile)
    {
        var result = new RecycleResult();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file))
            {
                result.FailedPaths.Add(file);
                continue;
            }

            try
            {
                if (recycleFile(file)) result.SuccessPaths.Add(file);
                else result.FailedPaths.Add(file);
            }
            catch { result.FailedPaths.Add(file); }
        }
        return result;
    }

    private bool RemoveCullMarksForSuccessfulJpgs(IEnumerable<string> successPaths)
    {
        var changed = false;
        foreach (var jpg in successPaths.Where(IsJpg).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            changed |= pickedFiles.Remove(jpg);
            changed |= rejectedFiles.Remove(jpg);
            changed |= jpgOnlyFiles.Remove(jpg);
            changed |= rawOnlyFiles.Remove(jpg);
        }
        return changed;
    }

    private void ShowRecycleResult(RecycleResult result)
    {
        var text = "已移入 Windows 回收站：" + result.SuccessPaths.Count + " 个文件。";
        if (result.FailedPaths.Count > 0)
        {
            var failedNames = result.FailedPaths.Select(Path.GetFileName).Take(8).ToArray();
            text += "\n\n无法处理 " + result.FailedPaths.Count + " 个：\n" + String.Join("\n", failedNames);
        }
        if (result.Notes.Count > 0) text += "\n\n" + String.Join("\n", result.Notes.Take(8));
        var errors = result.FailedPaths.Count > 0 || result.Notes.Count > 0;
        SetStatus(text.Split('\n')[0], errors);
        MessageBox.Show(text, errors ? "清理部分完成，请检查" : "清理完成", MessageBoxButton.OK, errors ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private string RootFolder(bool notify)
    {
        var root = folderBox.Text.Trim();
        if (Directory.Exists(root)) return PhotoCollection.Normalize(root);
        if (notify) MessageBox.Show("请选择一个有效的照片目录。", "请选择目录", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private static IEnumerable<string> FilesIn(string folder)
    {
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly) : Enumerable.Empty<string>();
    }

    private static BitmapSource LoadOrientedJpeg(string file, int decodePixelWidth)
    {
        var orientation = ReadExifOrientation(file);
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(file, UriKind.Absolute);
        if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (orientation == 3) image.Rotation = Rotation.Rotate180;
        else if (orientation == 5 || orientation == 6 || orientation == 7) image.Rotation = Rotation.Rotate90;
        else if (orientation == 8) image.Rotation = Rotation.Rotate270;
        image.EndInit();
        image.Freeze();

        BitmapSource source = image;
        ScaleTransform mirror = null;
        if (orientation == 2 || orientation == 5) mirror = new ScaleTransform(-1, 1);
        else if (orientation == 4 || orientation == 7) mirror = new ScaleTransform(1, -1);
        if (mirror != null)
        {
            mirror.Freeze();
            var transformed = new TransformedBitmap(source, mirror);
            transformed.Freeze();
            source = transformed;
        }
        return source;
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource source, int orientation)
    {
        var degrees = 0;
        if (orientation == 3) degrees = 180;
        else if (orientation == 5 || orientation == 6 || orientation == 7) degrees = 90;
        else if (orientation == 8) degrees = 270;
        if (degrees != 0)
        {
            var rotation = new RotateTransform(degrees);
            rotation.Freeze();
            var rotated = new TransformedBitmap(source, rotation);
            rotated.Freeze();
            source = rotated;
        }

        ScaleTransform mirror = null;
        if (orientation == 2 || orientation == 5) mirror = new ScaleTransform(-1, 1);
        else if (orientation == 4 || orientation == 7) mirror = new ScaleTransform(1, -1);
        if (mirror != null)
        {
            mirror.Freeze();
            var mirrored = new TransformedBitmap(source, mirror);
            mirrored.Freeze();
            source = mirrored;
        }
        if (source.CanFreeze) source.Freeze();
        return source;
    }

    private static int ReadExifOrientation(string file)
    {
        try
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                var metadata = decoder.Frames.Count > 0 ? decoder.Frames[0].Metadata as BitmapMetadata : null;
                var value = ReadMetadataValue(metadata, "/app1/ifd/{ushort=274}");
                var orientation = value == null ? 1 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return orientation >= 1 && orientation <= 8 ? orientation : 1;
            }
        }
        catch { return 1; }
    }

    private static bool IsJpg(string path) { return JpgExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
    private static bool IsRaw(string path) { return RawExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
    private void ShowPhotoInfo(string file)
    {
        var date = "—";
        var model = "—";
        var iso = "—";
        var aperture = "—";
        var exposure = "—";
        var focalLength = "—";
        GpsMetadata gps=null;
        try
        {
            // Metadata-only access keeps the UI thread from synchronously decoding
            // the full-resolution JPEG whenever the user advances to a new photo.
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                var metadata = decoder.Frames.Count > 0 ? decoder.Frames[0].Metadata as BitmapMetadata : null;
                gps=GpsMetadata.ReadWic(metadata,file);
                date = FormatExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36867}"));
                if (date == "—") date = FormatExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36868}"));
                if (date == "—") date = FormatExifDate(ReadMetadataValue(metadata, "/app1/ifd/{ushort=306}"));
                model = ToDisplayText(ReadMetadataValue(metadata, "/app1/ifd/{ushort=272}"));
                iso = ToDisplayText(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=34855}"));
                aperture = FormatAperture(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=33437}"));
                exposure = FormatExposure(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=33434}"));
                focalLength = FormatFocalLength(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=37386}"));
            }
        }
        catch { }

        photoInfo.Text = "拍摄日期：" + date
            + "    照相机型号：" + model
            + "    ISO：" + iso
            + "    焦段：" + focalLength
            + "    光圈值：" + aperture
            + "    曝光时间：" + exposure;
        gpsInfo.Tag=gps;UpdateGpsLine();
    }


    private void SelectGpsRoot(string root)
    {
        string previous=gpsSession.Root;
        try { gpsSession.SelectRoot(root);gpsSession.IsValid(); }
        catch { gpsSession.Clear();gpsSession.SelectRoot(null); }
        if(!String.Equals(previous,gpsSession.Root,StringComparison.OrdinalIgnoreCase)){
            Interlocked.Increment(ref gpsContextEpoch);Interlocked.Increment(ref gpsDisplayGeneration);
            gpsDisplayPending.Clear();gpsDirectoryRefreshing=false;gpsRefreshSourcePath=null;WatchGpsSource();
        }
        UpdateGpsLine();
    }
    private bool GpsToolReady() { return gpsAdapter!=null&&!String.IsNullOrEmpty(gpsToolPath)&&File.Exists(gpsToolPath); }
    private static string GpsCoordinates(GpsMetadata gps) { return gps==null?"—":gps.Latitude.ToString("0.000000",CultureInfo.InvariantCulture)+", "+gps.Longitude.ToString("0.000000",CultureInfo.InvariantCulture); }
    private void UpdateGpsLine()
    {
        gpsInfo.ToolTip=gpsToolNotice+"；仅修改 JPG，RAW / XMP 不处理";
        bool source=gpsSession.Source!=null,current=IsGpsSource(currentSingleFile);var gps=gpsInfo.Tag as GpsMetadata;
        gpsSetButton.Visibility=singleViewMode&&!current?Visibility.Visible:Visibility.Collapsed;gpsSetButton.Content="设为来源";
        gpsSetButton.IsEnabled=GpsUxState.CanSetSource(gps,current,gpsBusy);
        gpsApplyButton.Visibility=singleViewMode&&source&&!current?Visibility.Visible:Visibility.Collapsed;gpsApplyButton.Content="应用来源";gpsApplyButton.IsEnabled=!gpsBusy;
        gpsSourceBar.Visibility=source?Visibility.Visible:Visibility.Collapsed;
        gpsSourceText.Text=source?"来源  "+Path.GetFileName(gpsSession.Source.SourcePath):String.Empty;
        gpsSourceText.ToolTip=source?gpsSession.Source.SourcePath:null;gpsClearButton.IsEnabled=!gpsBusy;
        var selected=SelectedGpsFiles(null);GpsMetadata selectedGps=selected.Length==1?GetGpsDisplay(selected[0]):null;
        bool selectedSource=selected.Length==1&&IsGpsSource(selected[0]);
        gpsGridSetButton.Visibility=!singleViewMode&&!selectedSource?Visibility.Visible:Visibility.Collapsed;gpsGridSetButton.Content="设为来源";
        gpsGridSetButton.IsEnabled=selected.Length==1&&GpsUxState.CanSetSource(selectedGps,IsGpsSource(selected[0]),gpsBusy);
        gpsGridApplyButton.Visibility=!singleViewMode&&source&&!selectedSource?Visibility.Visible:Visibility.Collapsed;gpsGridApplyButton.Content="应用来源";gpsGridApplyButton.IsEnabled=!gpsBusy&&selected.Any(IsLegalGpsCandidate);
        gpsClearButton.Visibility=source?Visibility.Visible:Visibility.Collapsed;
        gpsGridActions.Visibility=!singleViewMode?Visibility.Visible:Visibility.Collapsed;
        gpsInfo.Text="GPS  "+(!singleViewMode&&selected.Length>1?"已选择 "+selected.Length+" 张":GpsCoordinates(singleViewMode?gps:selectedGps));
        gpsSingleCurrentSource.Visibility=(singleViewMode?current:selectedSource)?Visibility.Visible:Visibility.Collapsed;
        gpsSingleSourceDivider.Visibility=source?Visibility.Visible:Visibility.Collapsed;
        gpsSingleSourceCoordinates.Text=source?" · "+GpsCoordinates(gpsSession.Source)+(String.IsNullOrEmpty(gpsActivity)?"":"    "+gpsActivity):String.Empty;
        gpsSingleSourceCoordinates.ToolTip=source?GpsCoordinates(gpsSession.Source):null;
        foreach(var badge in gpsBadges.ToArray())UpdateGpsBadge(badge.Key,badge.Value);
        UpdateGpsSettings();
    }
    private void ChooseGpsTool(Window owner=null)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Title="显式选择 ExifTool 13.59 可执行文件",Filter="ExifTool 可执行文件|*.exe",CheckFileExists=true};
        if(dialog.ShowDialog(owner??this)==true)ConfigureGpsTool(dialog.FileName);
    }
    private void ConfigureGpsTool(string selected)
    {
        if(gpsBusy)return;
        gpsToolPath=selected;if(gpsAdapter!=null)gpsAdapter.Dispose();gpsAdapter=null;gpsBusy=true;gpsToolNotice="正在验证完整 ExifTool 身份";UpdateGpsLine();
        Task.Factory.StartNew(delegate {return new ExifToolAdapter(selected);}).ContinueWith(delegate(Task<ExifToolAdapter> task) {
            Dispatcher.BeginInvoke(new Action(delegate {
                gpsBusy=false;
                if(task.Status==TaskStatus.RanToCompletion){gpsAdapter=task.Result;gpsToolNotice="ExifTool 13.59 完整身份已验证";SaveSettings();}
                else {gpsToolNotice="ExifTool 不可用："+(task.Exception==null?"验证取消":task.Exception.GetBaseException().Message);}
                UpdateGpsLine();SetStatus(gpsToolNotice,task.Status!=TaskStatus.RanToCompletion);
            }));
        });
    }
    private void SetGpsSource(string file)
    {
        if(gpsBusy)return;
        try {
            SelectGpsRoot(RootFolder(false));gpsSession.SetSource(file);
            gpsRefreshSourcePath=gpsSession.Source.SourcePath;
            Interlocked.Increment(ref gpsContextEpoch);WatchGpsSource();UpdateGpsLine();SetStatus("GPS 来源："+Path.GetFileName(file),false);
        } catch(Exception e){MessageBox.Show(this,e.Message,"无法设为 GPS 来源",MessageBoxButton.OK,MessageBoxImage.Information);}
    }
    private void ApplyGps(string file)
    {
        ApplyGpsSelection(new[]{file});
    }

    private static Button GpsSmallButton(string text) {
        var b=new Button{Content=text};StyleButton(b,true,0);b.Height=26;b.FontSize=12;b.Margin=new Thickness(8,0,0,0);b.VerticalAlignment=VerticalAlignment.Center;
        // The shared legacy template does not bind Padding; keep this sizing local
        // to new GPS controls rather than changing existing culling buttons.
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));border.SetValue(Border.BackgroundProperty,b.Background);border.SetValue(Border.PaddingProperty,new Thickness(10,2,10,2));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);var template=new ControlTemplate(typeof(Button)){VisualTree=border};var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.5));template.Triggers.Add(disabled);b.Template=template;return b;
    }
    private bool IsGpsSource(string file){return !String.IsNullOrEmpty(file)&&gpsSession.Source!=null&&GpsFileSafety.Same(file,gpsSession.Source.SourcePath);}
    private bool IsLegalGpsCandidate(string file){try{return !collectionScanning&&!IsGpsSource(file)&&GpsFileSafety.IsJpg(file)&&gpsSession.Root!=null&&PhotoCollection.ContainsPath(gpsSession.Root,file)&&PhotoCollection.SafePath(file)&&File.Exists(file);}catch{return false;}}
    private string[] SelectedGpsFiles(string clicked) {
        if(singleViewMode)return String.IsNullOrEmpty(clicked??currentSingleFile)?new string[0]:new[]{clicked??currentSingleFile};
        if(clicked!=null&&!selectedFiles.Contains(clicked))return new[]{clicked};
        return selectedFiles.OrderBy(f=>galleryFiles.IndexOf(f)).ToArray();
    }
    private static GpsMetadata ReadGpsDisplay(string file) {
        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            var d=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnDemand);
            return d.Frames.Count==0?null:GpsMetadata.ReadWic(d.Frames[0].Metadata as BitmapMetadata,file);
        }
    }
    private static string GpsDisplayStamp(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))return GpsFileSafety.HandleIdentity(stream);}
    private GpsMetadata GetGpsDisplay(string file) {
        if(gpsDirectoryRefreshing){GpsDisplay fresh;return gpsDisplayCache.TryGetValue(file,out fresh)?fresh.Gps:null;}
        try {string stamp=GpsDisplayStamp(file);GpsDisplay value;if(gpsDisplayCache.TryGetValue(file,out value)&&value.Stamp==stamp)return value.Gps;
            var gps=ReadGpsDisplay(file);if(GpsDisplayStamp(file)!=stamp)return null;gpsDisplayCache[file]=new GpsDisplay{Stamp=stamp,Gps=gps};return gps;
        }catch{return null;}
    }
    private void QueueGpsDisplay(string file) {
        if(!gpsDisplayPending.Add(file))return;
        int generation=gpsDisplayGeneration;
        Task.Factory.StartNew(delegate {try {string stamp=GpsDisplayStamp(file);var gps=ReadGpsDisplay(file);return GpsDisplayStamp(file)==stamp?new GpsDisplay{Stamp=stamp,Gps=gps}:null;}catch{return null;}}).ContinueWith(delegate(Task<GpsDisplay> task) {
            if(Dispatcher.HasShutdownStarted)return;
            Dispatcher.BeginInvoke(new Action(delegate {
                if(generation!=gpsDisplayGeneration)return;
                ApplyGpsDisplayResult(file,task.Status==TaskStatus.RanToCompletion?task.Result:null,generation);UpdateGpsLine();
            }));
        });
    }
    private void ApplyGpsDisplayResult(string file,GpsDisplay value,int generation) {
        if(generation!=gpsDisplayGeneration)return;
        gpsDisplayPending.Remove(file);
        try {
            if(value!=null&&GpsDisplayStamp(file)==value.Stamp)gpsDisplayCache[file]=value;
            else {gpsDisplayCache.Remove(file);if(value!=null&&File.Exists(file))QueueGpsDisplay(file);}
        }catch{gpsDisplayCache.Remove(file);}
        if(String.Equals(file,currentSingleFile,StringComparison.OrdinalIgnoreCase)){
            GpsDisplay current;gpsInfo.Tag=gpsDisplayCache.TryGetValue(file,out current)?current.Gps:null;
        }
    }
    private void BeginGpsDirectoryRefresh(string root,List<string> files) {
        int generation=Interlocked.Increment(ref gpsDisplayGeneration);
        int context=Interlocked.Increment(ref gpsContextEpoch);
        string source=gpsSession.Source==null?gpsRefreshSourcePath:gpsSession.Source.SourcePath;
        gpsSession.Clear();WatchGpsSource();gpsDirectoryRefreshing=true;
        gpsDisplayPending.Clear();
        var previousStamps=gpsDisplayCache.Where(p=>PhotoCollection.ContainsPath(root,p.Key)).ToDictionary(p=>p.Key,p=>p.Value.Stamp,StringComparer.OrdinalIgnoreCase);
        foreach(var path in previousStamps.Keys)gpsDisplayCache.Remove(path);
        foreach(var file in files)gpsDisplayPending.Add(file);
        gpsInfo.Tag=null;
        Task.Factory.StartNew(delegate {
            // One sequential metadata worker, not 500 parallel decoders. No
            // ExifTool, pixel decoding, write transaction or integrity gate.
            var batch=new List<KeyValuePair<string,GpsDisplay>>();
            foreach(var file in files) {
                if(generation!=Volatile.Read(ref gpsDisplayGeneration))return;
                GpsDisplay value=null;
                try {string stamp=GpsDisplayStamp(file);var gps=ReadGpsDisplay(file);string previous;
                    if(GpsDisplayStamp(file)==stamp)value=new GpsDisplay{Stamp=stamp,Gps=gps,ImageIdentityChanged=previousStamps.TryGetValue(file,out previous)&&previous!=stamp};
                }catch{}
                batch.Add(new KeyValuePair<string,GpsDisplay>(file,value));
                if(batch.Count==16){PublishGpsRefreshBatch(batch.ToArray(),generation);batch.Clear();}
            }
            if(batch.Count>0)PublishGpsRefreshBatch(batch.ToArray(),generation);
            var refreshed=new GpsSourceSession();refreshed.SelectRoot(root);
            if(source!=null&&files.Contains(source,StringComparer.OrdinalIgnoreCase))try{refreshed.SetSource(source);}catch{}
            if(Dispatcher.HasShutdownStarted)return;
            Dispatcher.BeginInvoke(new Action(delegate {
                if(generation!=gpsDisplayGeneration)return;
                gpsDirectoryRefreshing=false;
                if(context==gpsContextEpoch){
                    if(refreshed.Source!=null)try{if(!refreshed.SourceFileIdentity.StartsWith(GpsDisplayStamp(refreshed.Source.SourcePath)+"|",StringComparison.Ordinal))refreshed.Clear();}catch{refreshed.Clear();}
                    gpsSession=refreshed;gpsRefreshSourcePath=refreshed.Source==null?null:refreshed.Source.SourcePath;WatchGpsSource();
                }
                UpdateGpsLine();
                if(source!=null&&context==gpsContextEpoch&&gpsSession.Source==null)SetStatus("GPS 来源已失效，已清除",false);
            }));
        });
    }
    private void PublishGpsRefreshBatch(KeyValuePair<string,GpsDisplay>[] batch,int generation) {
        if(Dispatcher.HasShutdownStarted)return;
        Dispatcher.BeginInvoke(new Action(delegate {
            if(generation!=gpsDisplayGeneration)return;
            foreach(var item in batch){
                if(item.Value!=null&&item.Value.ImageIdentityChanged)RemoveImageCachesForPaths(new[]{item.Key});
                ApplyGpsDisplayResult(item.Key,item.Value,generation);
            }
            UpdateGpsLine();
        }));
    }
    // Frozen GPS grid icons: Fluent Location / Location Off 16 Filled, Location Ripple 20 Filled.
    // Source asset: assets/Location Ripple/SVG/ic_fluent_location_ripple_20_filled.svg
    // https://github.com/microsoft/fluentui-system-icons (MIT)
    // MIT License
    // Copyright (c) 2020 Microsoft Corporation
    // Permission is hereby granted, free of charge, to any person obtaining a copy
    // of this software and associated documentation files (the "Software"), to deal
    // in the Software without restriction, including without limitation the rights
    // to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    // copies of the Software, and to permit persons to whom the Software is
    // furnished to do so, subject to the following conditions:
    // The above copyright notice and this permission notice shall be included in all
    // copies or substantial portions of the Software.
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    // SOFTWARE.
    private static Geometry GpsBadgeGeometry(string data) {var geometry=Geometry.Parse("F1 "+data);geometry.Freeze();return geometry;}
    private static readonly Geometry gpsLocationIcon=GpsBadgeGeometry("M9.15573 14.5444C10.8991 13.0097 14 9.87562 14 7C14 3.68629 11.3137 1 8 1C4.68629 1 2 3.68629 2 7C2 9.87562 5.10094 13.0097 6.84427 14.5444C7.51146 15.1317 8.48854 15.1317 9.15573 14.5444ZM6 7C6 5.89543 6.89543 5 8 5C9.10457 5 10 5.89543 10 7C10 8.10457 9.10457 9 8 9C6.89543 9 6 8.10457 6 7Z");
    private static readonly Geometry gpsLocationOffIcon=GpsBadgeGeometry("M1.14648 1.14648C1.34174 0.951241 1.65825 0.951229 1.85351 1.14648L14.8535 14.1465C15.0488 14.3418 15.0488 14.6583 14.8535 14.8535C14.6583 15.0488 14.3418 15.0488 14.1465 14.8535L11.4893 12.1963C10.6781 13.142 9.81909 13.9596 9.15528 14.544C8.48811 15.1312 7.5119 15.1312 6.84473 14.544C5.1014 13.0092 1.99999 9.87563 1.99999 7C1.99999 5.77973 2.36517 4.64531 2.99121 3.69824L1.14648 1.85352C0.951215 1.65825 0.951215 1.34175 1.14648 1.14648ZM8 1C11.3137 1.00002 14 3.68631 14 7C14 8.19635 13.4624 9.43684 12.71 10.5889L9.85743 7.73633C9.94794 7.5082 10 7.26036 10 7C10 5.89545 9.10456 5.00002 8 5C7.73946 5 7.49096 5.05097 7.2627 5.1416L4.35547 2.23438C5.36586 1.46051 6.62899 1 8 1ZM6.02051 6.72754C6.00818 6.81668 6 6.90746 6 7C6 8.10457 6.89543 9.00001 8 9.00001C8.09229 9 8.18266 8.99095 8.27149 8.97852L6.02051 6.72754Z");
    private static readonly Geometry gpsSourceIcon=GpsBadgeGeometry("M10 2C6.68629 2 4 4.68629 4 8C4 9.46824 4.84298 11.0073 5.80741 12.3063C6.78625 13.6247 7.95937 14.786 8.75165 15.5133C9.46337 16.1667 10.5366 16.1667 11.2484 15.5133C12.0406 14.786 13.2137 13.6247 14.1926 12.3063C15.157 11.0073 16 9.46824 16 8C16 4.68629 13.3137 2 10 2ZM12 8C12 9.10457 11.1046 10 10 10C8.89543 10 8 9.10457 8 8C8 6.89543 8.89543 6 10 6C11.1046 6 12 6.89543 12 8ZM14.4196 13.6379C14.1832 13.9254 13.9428 14.2018 13.7048 14.4641C13.9587 14.5358 14.1967 14.6144 14.4167 14.699C14.9712 14.9123 15.3857 15.154 15.6528 15.3969C15.9198 15.6397 16 15.8442 16 16.0001C16 16.1559 15.9198 16.3605 15.6528 16.6033C15.3857 16.8462 14.9712 17.0879 14.4167 17.3012C13.311 17.7265 11.7496 18.0001 10 18.0001C8.2504 18.0001 6.68901 17.7265 5.5833 17.3012C5.02877 17.0879 4.61433 16.8462 4.34719 16.6033C4.08019 16.3605 4 16.1559 4 16.0001C4 15.8442 4.08019 15.6397 4.34719 15.3969C4.61433 15.154 5.02877 14.9123 5.5833 14.699C5.80327 14.6144 6.04128 14.5358 6.29523 14.4641C6.05719 14.2018 5.81683 13.9254 5.58044 13.6379C5.45813 13.6788 5.33934 13.7214 5.22432 13.7656C4.60257 14.0048 4.06511 14.3018 3.67442 14.657C3.28358 15.0125 3 15.4656 3 16.0001C3 16.5346 3.28358 16.9877 3.67442 17.3431C4.06511 17.6984 4.60257 17.9954 5.22432 18.2345C6.47114 18.7141 8.15975 19.0001 10 19.0001C11.8403 19.0001 13.5289 18.7141 14.7757 18.2345C15.3974 17.9954 15.9349 17.6984 16.3256 17.3431C16.7164 16.9877 17 16.5346 17 16.0001C17 15.4656 16.7164 15.0125 16.3256 14.657C15.9349 14.3018 15.3974 14.0048 14.7757 13.7656C14.6607 13.7214 14.5419 13.6788 14.4196 13.6379Z");
    private void UpdateGpsBadge(Border badge,string file) {
        GpsDisplay value;var gps=gpsDisplayCache.TryGetValue(file,out value)?value.Gps:null;bool source=IsGpsSource(file);
        var icon=(System.Windows.Shapes.Path)badge.Child;
        icon.Data=source?gpsSourceIcon:gps!=null?gpsLocationIcon:gpsLocationOffIcon;
        icon.Width=icon.Height=source||gps==null?15:14;
        icon.Fill=Brush(source?"#B5D8FF":gps!=null?"#A8C9EC":"#738297");
        string meaning=source?"当前 GPS 来源":gps!=null?"已有 GPS 信息":"无 GPS 信息";
        badge.ToolTip=meaning;System.Windows.Automation.AutomationProperties.SetName(badge,meaning);
    }
    private void ClearGpsSource(){InvalidateGpsSource(true);}
    private void InvalidateGpsSource(bool forget){if(forget)gpsRefreshSourcePath=null;gpsSession.Clear();Interlocked.Increment(ref gpsContextEpoch);WatchGpsSource();UpdateGpsLine();}
    private void WatchGpsSource() {
        if(gpsSourceWatcher!=null){gpsSourceWatcher.Dispose();gpsSourceWatcher=null;}if(gpsSourcePoll!=null)gpsSourcePoll.Stop();
        if(gpsSession.Source==null)return;string path=gpsSession.Source.SourcePath;int epoch=gpsContextEpoch;
        Action recheck=delegate {if(epoch==gpsContextEpoch&&gpsSession.Source!=null&&!gpsSession.IsValid())InvalidateGpsSource(false);};
        try {
            gpsSourceWatcher=new FileSystemWatcher(Path.GetDirectoryName(path),Path.GetFileName(path)){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.FileName|NotifyFilters.Attributes|NotifyFilters.CreationTime};
            FileSystemEventHandler changed=delegate {Dispatcher.BeginInvoke(new Action(recheck));};
            gpsSourceWatcher.Changed+=changed;gpsSourceWatcher.Deleted+=changed;gpsSourceWatcher.Renamed+=delegate {Dispatcher.BeginInvoke(new Action(recheck));};
            gpsSourceWatcher.Error+=delegate {Dispatcher.BeginInvoke(new Action(delegate {if(epoch==gpsContextEpoch)InvalidateGpsSource(false);}));};gpsSourceWatcher.EnableRaisingEvents=true;
            if(gpsSourcePoll==null){gpsSourcePoll=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};gpsSourcePoll.Tick+=delegate {
                if(gpsSession.Source==null)return;try {if(!gpsSession.SourceFileIdentity.StartsWith(GpsDisplayStamp(gpsSession.Source.SourcePath)+"|",StringComparison.Ordinal))InvalidateGpsSource(false);}catch{InvalidateGpsSource(false);}
            };}gpsSourcePoll.Start();
        }catch{ClearGpsSource();}
    }
    private Window GpsDialog(string title,Panel content,double width=560) {
        var window=new Window{Owner=this,Title=title,Width=width,SizeToContent=SizeToContent.Height,MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-60),WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Brush("#1B2027"),Foreground=Brush("#DCE3EC"),FontFamily=FontFamily,FontSize=13};
        content.Margin=new Thickness(20);window.Content=content;
        window.SourceInitialized+=delegate {try {var handle=new WindowInteropHelper(window).Handle;int dark=1;if(DwmSetWindowAttribute(handle,DwmUseImmersiveDarkMode,ref dark,sizeof(int))!=0)DwmSetWindowAttribute(handle,DwmUseImmersiveDarkModeBefore20H1,ref dark,sizeof(int));}catch{}};
        return window;
    }
    private static TextBlock GpsDialogText(string text) {return new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),Foreground=Brush("#DCE3EC")};}
    private void ShowGpsSettings() {
        var body=new StackPanel();body.Children.Add(GpsDialogText("GPS / 元数据"));gpsSettingsStatus=GpsDialogText("");body.Children.Add(gpsSettingsStatus);
        var window=GpsDialog("设置",body);gpsSettingsChoose=GpsSmallButton("选择/更换 ExifTool");gpsSettingsChoose.Margin=new Thickness(0);gpsSettingsChoose.HorizontalAlignment=HorizontalAlignment.Left;gpsSettingsChoose.Click+=delegate {ChooseGpsTool(window);};body.Children.Add(gpsSettingsChoose);
        body.Children.Add(GpsDialogText("\nExifTool 仅用于 JPG GPS 写入。不下载、不安装，不影响挑片；RAW / XMP 不处理。"));
        var close=GpsSmallButton("关闭");close.HorizontalAlignment=HorizontalAlignment.Right;close.Click+=delegate{window.Close();};body.Children.Add(close);
        UpdateGpsSettings();window.Closed+=delegate{gpsSettingsStatus=null;gpsSettingsChoose=null;};window.ShowDialog();
    }
    private void UpdateGpsSettings(){if(gpsSettingsStatus!=null)gpsSettingsStatus.Text="ExifTool\n状态："+GpsUxState.ToolStatus(GpsToolReady(),gpsBusy,gpsToolPath)+"\n版本："+(gpsAdapter==null?"13.59（要求）":gpsAdapter.Version)+"\n当前路径："+(String.IsNullOrEmpty(gpsToolPath)?"—":gpsToolPath)+"\n\n"+gpsToolNotice;if(gpsSettingsChoose!=null)gpsSettingsChoose.IsEnabled=!gpsBusy;}
    private void RequireGpsTool() {
        var body=new StackPanel();body.Children.Add(GpsDialogText("GPS 写入需要先配置 ExifTool。"));var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(row);var window=GpsDialog("GPS 写入不可用",body,400);
        var cancel=GpsSmallButton("取消");cancel.Click+=delegate{window.Close();};row.Children.Add(cancel);var settings=GpsSmallButton("前往设置");settings.Click+=delegate{window.DialogResult=true;};row.Children.Add(settings);
        if(window.ShowDialog()==true)ShowGpsSettings();
    }
    private bool ConfirmGpsBatch(GpsBatchPlan plan,out bool overwrite) {
        var body=new StackPanel();body.Children.Add(GpsDialogText("来源："+Path.GetFileName(plan.SourcePath)+"\n"+GpsCoordinates(plan.Source)+"\n\n已选择："+plan.Entries.Count+" 张\n"+plan.Count(GpsSelectionKind.Missing)+" 张缺少 GPS\n"+plan.Count(GpsSelectionKind.Existing)+" 张已有 GPS\n"+plan.Count(GpsSelectionKind.Source)+" 张为 GPS 来源（永远跳过）\n"+plan.Count(GpsSelectionKind.Invalid)+" 张不可写 / 无效"));
        var missing=new CheckBox{Content="仅应用到缺少 GPS 的照片",IsChecked=true,Foreground=Brush("#DCE3EC"),Margin=new Thickness(0,0,0,10)};
        var replace=new CheckBox{Content="覆盖已有 GPS",IsChecked=false,Foreground=Brush("#DCE3EC"),Margin=new Thickness(0,0,0,10)};body.Children.Add(missing);body.Children.Add(replace);
        var warning=GpsDialogText("");body.Children.Add(warning);
        var details=new StackPanel();foreach(var e in plan.Entries)details.Children.Add(GpsDialogText(Path.GetFileName(e.Path)+" · "+(e.Kind==GpsSelectionKind.Existing?"当前 GPS："+GpsCoordinates(e.Plan.Targets[0].Before.Gps):e.Kind==GpsSelectionKind.Missing?"GPS：—":e.Reason)));
        body.Children.Add(new ScrollViewer{Content=details,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});body.Children.Add(GpsDialogText("仅写所选 JPG 的五个 GPS 字段。RAW / XMP 不处理。"));
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(row);var window=GpsDialog("应用 GPS",body);
        var apply=GpsSmallButton("应用");apply.IsDefault=true;apply.Click+=delegate{window.DialogResult=true;};row.Children.Add(apply);
        var cancel=GpsSmallButton("取消");cancel.IsCancel=true;cancel.Background=Brushes.Transparent;cancel.Foreground=Brush("#A9B8CA");cancel.FontWeight=FontWeights.Normal;
        var cancelBorder=new FrameworkElementFactory(typeof(Border));cancelBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));cancelBorder.SetValue(Border.BackgroundProperty,cancel.Background);
        cancelBorder.SetValue(Border.BorderBrushProperty,Brush("#465464"));cancelBorder.SetValue(Border.BorderThicknessProperty,new Thickness(1));cancelBorder.SetValue(Border.PaddingProperty,new Thickness(10,2,10,2));
        var cancelContent=new FrameworkElementFactory(typeof(ContentPresenter));cancelContent.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);cancelContent.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);cancelBorder.AppendChild(cancelContent);
        cancel.Template=new ControlTemplate(typeof(Button)){VisualTree=cancelBorder};cancel.Click+=delegate{window.Close();};row.Children.Add(cancel);
        Action update=delegate {bool yes=replace.IsChecked==true;warning.Text=yes?plan.Count(GpsSelectionKind.Existing)+" 张照片已有 GPS，将被来源位置覆盖。":"已有 GPS 的照片不会修改。";apply.IsEnabled=plan.Count(GpsSelectionKind.Missing)+(yes?plan.Count(GpsSelectionKind.Existing):0)>0;};
        replace.Checked+=delegate{missing.IsChecked=false;update();};replace.Unchecked+=delegate{missing.IsChecked=true;update();};missing.Checked+=delegate{replace.IsChecked=false;update();};missing.Unchecked+=delegate{if(replace.IsChecked!=true)missing.IsChecked=true;};update();
        bool confirmed=window.ShowDialog()==true;overwrite=replace.IsChecked==true;return confirmed;
    }
    private void ApplyGpsSelection(IEnumerable<string> selection) {
        if(gpsBusy)return;if(!gpsSession.IsValid()){UpdateGpsLine();return;}if(!GpsToolReady()){RequireGpsTool();return;}
        var paths=selection.ToArray();if(paths.Length==0)return;string root=gpsSession.Root,source=gpsSession.Source.SourcePath;int epoch=gpsContextEpoch;var tool=gpsAdapter;
        gpsBusy=true;gpsActivity="正在检查所选 JPG…";UpdateGpsLine();
        Task.Factory.StartNew(delegate {return new GpsBatchPlan(root,source,paths,tool,delegate(int i,int n){Dispatcher.BeginInvoke(new Action(delegate{gpsActivity="正在检查 "+i+" / "+n;UpdateGpsLine();}));});}).ContinueWith(delegate(Task<GpsBatchPlan> planned) {
            Dispatcher.BeginInvoke(new Action(delegate {
                if(planned.Status!=TaskStatus.RanToCompletion){gpsBusy=false;gpsActivity=null;UpdateGpsLine();MessageBox.Show(this,planned.Exception.GetBaseException().Message,"GPS 规划失败",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
                if(epoch!=gpsContextEpoch||!gpsSession.IsValid()){gpsBusy=false;gpsActivity=null;UpdateGpsLine();return;}
                bool overwrite;if(!ConfirmGpsBatch(planned.Result,out overwrite)){gpsBusy=false;gpsActivity=null;UpdateGpsLine();return;}
                RunGpsBatch(planned.Result,overwrite,tool,epoch);
            }));
        });
    }
    private void RunGpsBatch(GpsBatchPlan plan,bool overwrite,ExifToolAdapter tool,int epoch) {
        int cancel=0;bool finished=false;var body=new StackPanel();var text=GpsDialogText("正在应用 GPS\n0 / "+plan.Entries.Count);body.Children.Add(text);var stop=GpsSmallButton("取消剩余");body.Children.Add(stop);var window=GpsDialog("正在应用 GPS",body,420);
        stop.Click+=delegate{Interlocked.Exchange(ref cancel,1);stop.IsEnabled=false;text.Text+="\n取消剩余已请求；当前事务将先安全结束。";};
        window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!finished){e.Cancel=true;Interlocked.Exchange(ref cancel,1);stop.IsEnabled=false;}};window.Show();
        Task.Factory.StartNew(delegate {return GpsBatchExecutor.Execute(plan,overwrite,delegate{return Interlocked.CompareExchange(ref cancel,0,0)!=0||epoch!=Interlocked.CompareExchange(ref gpsContextEpoch,0,0);},delegate(GpsBatchProgress progress) {
            Dispatcher.BeginInvoke(new Action(delegate {
                text.Text="正在应用 GPS\n"+progress.Index+" / "+progress.Total+"\n"+Path.GetFileName(progress.Path)+(cancel!=0?"\n已取消剩余；当前事务安全结束后停止。":"");
                if(progress.Result!=null&&progress.Result.Success)GpsCommitted(progress.Path);
            }));
        },delegate(GpsSelectionEntry entry) {
            return new GpsFileTransaction(tool,GpsFileSafety.ValidateParsing,null,delegate {if(epoch!=Interlocked.CompareExchange(ref gpsContextEpoch,0,0))throw new IOException("根目录或 GPS 来源已改变，停止提交。");}).Execute(entry.Plan,entry.Plan.Targets[0],overwrite);
        });}).ContinueWith(delegate(Task<GpsBatchResult> done) {
            Dispatcher.BeginInvoke(new Action(delegate {
                finished=true;window.Close();gpsBusy=false;gpsActivity=null;UpdateGpsLine();
                if(done.Status!=TaskStatus.RanToCompletion){MessageBox.Show(this,done.Exception.GetBaseException().Message,"GPS 操作异常",MessageBoxButton.OK,MessageBoxImage.Error);return;}
                var result=done.Result;try{Directory.CreateDirectory(StateDirectory);File.WriteAllText(Path.Combine(StateDirectory,"gps-performance.log"),String.Join("\n\n",result.Success.Concat(result.Failed).Select(r=>r.Diagnostics??"")),new UTF8Encoding(false));}catch{}
                ShowGpsBatchResult(result);
            }));
        });
    }
    private void GpsCommitted(string file) {
        RemoveImageCachesForPaths(new[]{file});gpsDisplayCache.Remove(file);QueueGpsDisplay(file);
        if(String.Equals(currentSingleFile,file,StringComparison.OrdinalIgnoreCase)){singleLoadVersion++;ShowPhotoInfo(file);}UpdateGpsLine();
    }
    private void ShowGpsBatchResult(GpsBatchResult result) {
        var body=new StackPanel();body.Children.Add(GpsDialogText("成功："+result.Success.Count+"\n跳过："+result.Skipped.Count+"\n失败："+result.Failed.Count));
        if(result.Failed.Count>0)body.Children.Add(GpsDialogText("失败：\n"+String.Join("\n",result.Failed.Select(r=>Path.GetFileName(r.Path)))));
        var detail=GpsDialogText(String.Join("\n\n",result.Failed.Concat(result.Skipped).Select(r=>Path.GetFileName(r.Path)+"："+r.Message+(String.IsNullOrEmpty(r.RecoveryPath)?"":"\n恢复材料："+r.RecoveryPath))));detail.Visibility=Visibility.Collapsed;
        var scroll=new ScrollViewer{Content=detail,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(scroll);var window=GpsDialog("GPS 应用完成",body);
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(row);
        if(result.Failed.Count+result.Skipped.Count>0){var more=GpsSmallButton("查看详情");more.Click+=delegate{detail.Visibility=detail.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;};row.Children.Add(more);}
        var close=GpsSmallButton("确定");close.Click+=delegate{window.Close();};row.Children.Add(close);window.ShowDialog();
    }
    private void EnableGpsMarquee(ScrollViewer viewer,Grid panel) {
        var canvas=new Canvas{IsHitTestVisible=false,ClipToBounds=true};var rectangle=new System.Windows.Shapes.Rectangle{Stroke=Brush("#436794"),Fill=Brush("#33436794"),StrokeThickness=1,Visibility=Visibility.Collapsed};canvas.Children.Add(rectangle);Grid.SetRow(canvas,1);panel.Children.Add(canvas);
        Point start=new Point();bool dragging=false;var previous=new List<string>();
        viewer.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
            for(DependencyObject node=e.OriginalSource as DependencyObject;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node)){var tile=node as Border;if(tile!=null&&tileFiles.ContainsKey(tile)||node is ScrollBar)return;if(node==viewer)break;}
            start=e.GetPosition(canvas);previous=(Keyboard.Modifiers&ModifierKeys.Control)!=0?selectedFiles.ToList():new List<string>();dragging=true;viewer.CaptureMouse();
        };
        viewer.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {
            if(!dragging||e.LeftButton!=MouseButtonState.Pressed)return;var now=e.GetPosition(canvas);if((now-start).Length<4)return;var bounds=new Rect(start,now);rectangle.Visibility=Visibility.Visible;rectangle.Width=bounds.Width;rectangle.Height=bounds.Height;Canvas.SetLeft(rectangle,bounds.Left);Canvas.SetTop(rectangle,bounds.Top);
            selectedFiles.Clear();foreach(var path in previous)selectedFiles.Add(path);foreach(var tile in tileFiles)if(bounds.IntersectsWith(tile.Key.TransformToVisual(canvas).TransformBounds(new Rect(0,0,tile.Key.ActualWidth,tile.Key.ActualHeight))))selectedFiles.Add(tile.Value);
            UpdateTileSelectionAppearance();UpdateGpsLine();galleryCaption.Text=selectedFiles.Count>0?"已选择 "+selectedFiles.Count+" 张 · 可批量标记废片":tileFiles.Count+" 张 JPG · 单击选择，双击单张浏览";recycleSelectedButton.IsHitTestVisible=selectedFiles.Count>0;recycleSelectedButton.Opacity=selectedFiles.Count>0?1:0.52;e.Handled=true;
        };
        viewer.PreviewMouseLeftButtonUp+=delegate {if(dragging){dragging=false;rectangle.Visibility=Visibility.Collapsed;viewer.ReleaseMouseCapture();}};
        viewer.LostMouseCapture+=delegate{dragging=false;rectangle.Visibility=Visibility.Collapsed;};
    }

    private static object ReadMetadataValue(BitmapMetadata metadata, string query)
    {
        if (metadata == null) return null;
        try { return metadata.GetQuery(query); }
        catch { return null; }
    }

    private static string ToDisplayText(object value)
    {
        if (value == null) return "—";
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return String.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
    }

    private static string FormatExifDate(object value)
    {
        var text = ToDisplayText(value);
        if (text == "—") return text;
        var date = ParseExifDate(value);
        if (date.HasValue) return date.Value.ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture);
        return text;
    }

    private static DateTime? ParseExifDate(object value)
    {
        var text = ToDisplayText(value);
        if (text == "—") return null;
        DateTime date;
        if (DateTime.TryParseExact(text, new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;
        return null;
    }

    private static bool TryGetRational(object value, out double number, out uint numerator, out uint denominator)
    {
        number = 0;
        numerator = 0;
        denominator = 0;
        if (value == null) return false;
        try
        {
            var packed = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            // WIC stores EXIF rationals as denominator in the high 32 bits and numerator in the low 32 bits.
            numerator = (uint)packed;
            denominator = (uint)(packed >> 32);
            if (denominator > 0)
            {
                number = (double)numerator / denominator;
                return true;
            }
        }
        catch { }
        var text = ToDisplayText(value);
        var parts = text.Split('/');
        if (parts.Length == 2 && UInt32.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator) && UInt32.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out denominator) && denominator > 0)
        {
            number = (double)numerator / denominator;
            return true;
        }
        return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string FormatAperture(object value)
    {
        double aperture;
        uint numerator, denominator;
        if (!TryGetRational(value, out aperture, out numerator, out denominator) || aperture <= 0) return "—";
        return "f/" + aperture.ToString(aperture % 1 == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture);
    }

    private static string FormatFocalLength(object value)
    {
        double focalLength;
        uint numerator, denominator;
        if (!TryGetRational(value, out focalLength, out numerator, out denominator) || focalLength <= 0) return "—";
        return focalLength.ToString(focalLength % 1 == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture) + " mm";
    }

    private static string FormatExposure(object value)
    {
        double seconds;
        uint numerator, denominator;
        if (!TryGetRational(value, out seconds, out numerator, out denominator) || seconds <= 0) return "—";
        return FormatExposureSeconds(seconds);
    }

    private static string FormatExposureSeconds(double seconds)
    {
        if (seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return "—";
        if (seconds < 1)
        {
            var reciprocal = 1.0 / seconds;
            if (!Double.IsNaN(reciprocal) && !Double.IsInfinity(reciprocal) && reciprocal <= Int64.MaxValue)
            {
                var nearestDenominator = (long)Math.Round(reciprocal, MidpointRounding.AwayFromZero);
                if (nearestDenominator >= 2) return "1/" + nearestDenominator.ToString(CultureInfo.InvariantCulture) + " 秒";
            }
        }
        return seconds.ToString(seconds % 1 == 0 ? "0" : "0.###", CultureInfo.InvariantCulture) + " 秒";
    }

    private void SetStatus(string text, bool error)
    {
        status.Text = text;
        status.Foreground = error ? Brush("#B42318") : Brush("#5F6B7A");
    }

    private static Border Card()
    {
        return new Border { Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(20) };
    }

    private static Border Divider()
    {
        return new Border { Height = 1, Background = Brush("#E4EAF0"), Margin = new Thickness(0, 18, 0, 18) };
    }

    private static TextBlock SectionTitle(string text)
    {
        return new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2937") };
    }

    private static UIElement Stat(string label, TextBlock value, string color)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.Normal,
            Foreground = Brush("#6A7787"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center
        });
        value.Text = "—";
        value.Margin = new Thickness(0);
        value.FontSize = 22;
        value.FontWeight = FontWeights.SemiBold;
        value.Foreground = Brush(color);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.TextAlignment = TextAlignment.Center;
        panel.Children.Add(new Viewbox
        {
            Child = value,
            Height = 30,
            Margin = new Thickness(6, 3, 6, 0),
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        return panel;
    }

    private static Button PrimaryButton(string text, double width) { var button = new Button(); button.Content = text; StyleButton(button, true, width); return button; }
    private static Button SecondaryButton(string text, double width) { var button = new Button(); button.Content = text; StyleButton(button, false, width); return button; }

    private static void StyleButton(Button button, bool primary, double width)
    {
        button.Width = width > 0 ? width : double.NaN;
        button.Height = 40;
        button.FontSize = 14;
        button.FontWeight = FontWeights.SemiBold;
        button.Cursor = Cursors.Hand;
        button.BorderThickness = new Thickness(0);
        // Keep every action in one visual family; availability is communicated by opacity.
        button.Background = Brush(ButtonBlue);
        button.Foreground = Brushes.White;
        ApplyRoundedButtonTemplate(button, 5);
    }

    private static void StyleDestructiveButton(Button button, double width)
    {
        StyleButton(button, true, width);
    }

    private static void ApplyRoundedButtonTemplate(Button button, double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
    }

    private static void StyleViewButton(Button button)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
    }

    private static void StyleFolderHistoryButton(Button button)
    {
        button.Width = 42;
        button.Height = 40;
        button.Padding = new Thickness(0);
        button.FontSize = 11;
        button.Cursor = Cursors.Hand;
        button.BorderBrush = Brush(ButtonBlue);
        button.BorderThickness = new Thickness(1, 1, 1, 1);
        button.Background = Brush(ButtonBlue);
        button.Foreground = Brushes.White;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 6, 6, 0));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
    }

    private static ScrollBar CreateMiniVerticalScrollBar()
    {
        var railColor = darkMode ? "#111820" : "#D8E0E8";
        var thumbColor = darkMode ? "#586574" : "#566473";
        var templateXaml =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ScrollBar}\">" +
            "<Grid Width=\"8\" Background=\"Transparent\">" +
            "<Border Width=\"6\" HorizontalAlignment=\"Center\" Background=\"" + railColor + "\" CornerRadius=\"3\"/>" +
            "<Track x:Name=\"PART_Track\" Orientation=\"Vertical\" IsDirectionReversed=\"True\" " +
            "Minimum=\"{TemplateBinding Minimum}\" Maximum=\"{TemplateBinding Maximum}\" " +
            "Value=\"{TemplateBinding Value}\" ViewportSize=\"{TemplateBinding ViewportSize}\">" +
            "<Track.DecreaseRepeatButton><RepeatButton Command=\"{x:Static ScrollBar.PageUpCommand}\" Opacity=\"0\" Focusable=\"False\"/></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb MinHeight=\"28\" Focusable=\"False\"><Thumb.Template>" +
            "<ControlTemplate TargetType=\"{x:Type Thumb}\"><Border Margin=\"1,1\" Background=\"" + thumbColor + "\" CornerRadius=\"3\"/></ControlTemplate>" +
            "</Thumb.Template></Thumb></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Command=\"{x:Static ScrollBar.PageDownCommand}\" Opacity=\"0\" Focusable=\"False\"/></Track.IncreaseRepeatButton>" +
            "</Track></Grid></ControlTemplate>";
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = 8,
            Minimum = 0,
            SmallChange = 80,
            LargeChange = 520,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 22, 5, 10),
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            Template = (ControlTemplate)XamlReader.Parse(templateXaml)
        };
        Panel.SetZIndex(scrollBar, 5);
        return scrollBar;
    }

    private static void StyleContextMenuItem(MenuItem item)
    {
        item.Height = 34;
        item.Padding = new Thickness(12, 0, 14, 0);
        var normalBackground = darkMode ? Brush("#242A31") : Brush("#FFFFFF");
        var normalForeground = darkMode ? Brush("#F0F4F8") : Brush("#1F2937");
        var hoverBackground = darkMode ? Brush("#436794") : Brush("#D7E9FF");
        var hoverForeground = darkMode ? Brushes.White : Brush("#17324D");
        item.Background = normalBackground;
        item.Foreground = normalForeground;
        item.FontWeight = FontWeights.Normal;
        item.FocusVisualStyle = null;
        item.BorderBrush = Brushes.Transparent;
        item.BorderThickness = new Thickness(0);
        item.SnapsToDevicePixels = true;
        item.UseLayoutRounding = true;
        item.MouseEnter += delegate
        {
            if (!item.IsEnabled) return;
            item.Background = hoverBackground;
            item.Foreground = hoverForeground;
        };
        item.MouseLeave += delegate
        {
            item.Background = normalBackground;
            item.Foreground = normalForeground;
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetValue(Border.MarginProperty, new Thickness(3, 1, 3, 1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, item.Padding);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(MenuItem));
        template.VisualTree = border;
        item.Template = template;
    }

    private static void StyleFolderHistoryMenu(ContextMenu menu)
    {
        menu.Padding = new Thickness(0);
        menu.BorderThickness = new Thickness(0);
        menu.SnapsToDevicePixels = true;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, menu.Background);
        border.SetValue(Border.BorderBrushProperty, darkMode ? Brush("#4A5664") : Brush("#B7C3D0"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ContextMenu));
        template.VisualTree = border;
        menu.Template = template;
    }

    private static void StyleRoundedContextMenuAction(MenuItem item)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.BackgroundProperty, item.Background);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, item.Padding);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(MenuItem));
        template.VisualTree = border;
        item.Template = template;
    }

    private static SolidColorBrush Brush(string hex)
    {
        if (darkMode)
        {
            switch (hex.ToUpperInvariant())
            {
                case "#F4F7FB": hex = "#171B21"; break;
                case "#FFFFFF": hex = "#242A31"; break;
                case "#FBFCFE": hex = "#1E232A"; break;
                case "#EAF1F8": hex = "#303A46"; break;
                case "#EAF0F5": hex = "#2A3038"; break;
                case "#EAF3FF": hex = "#193A58"; break;
                case "#D9E2EC": hex = "#3F4955"; break;
                case "#D6E0EA": hex = "#46515E"; break;
                case "#E4EAF0": hex = "#3A444F"; break;
                case "#B7C3D0": hex = "#5A6674"; break;
                case "#1F2937": hex = "#F0F4F8"; break;
                case "#2D3A4A": hex = "#E2EAF2"; break;
                case "#5F6B7A": hex = "#B5C0CD"; break;
                case "#66778B": hex = "#B7C3D0"; break;
                case "#6A7787": hex = "#B5C0CD"; break;
                case "#75869A": hex = "#B7C3D0"; break;
                case "#245F98": hex = "#B9DDFF"; break;
                case "#2678D4": hex = "#4D9DFF"; break;
                case "#8167C8": hex = "#B69AF4"; break;
                case "#E28825": hex = "#FFAD4E"; break;
                case "#B42318": hex = "#FF9B96"; break;
            }
        }
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
    }

    private static UIElement HeaderBadgeImage()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream("RAWMateHeader.png");
            if (stream == null)
                throw new FileNotFoundException("Embedded header image was not found.");

            var image = new BitmapImage();
            using (stream)
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return new Image { Source = image, Stretch = Stretch.Uniform };
        }
        catch
        {
            return new TextBlock { Text = "RAW", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
    }

    // SHFileOperation with FOF_ALLOWUNDO is the native Windows Recycle Bin API.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    private static bool MoveToRecycleBin(string file)
    {
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(file) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };
        var result = SHFileOperation(ref operation);
        return result == 0 && !operation.fAnyOperationsAborted;
    }
}
