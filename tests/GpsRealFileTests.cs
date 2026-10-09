using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml;

// Diagnostic harness: all baseline scans are READ ONLY. Compiles with the single
// production source to use its exact process runner/model/parser, not a second app.
internal static class GpsRealFileTests
{
    private static ExifToolProcessRunner runner;
    private static string output;
    private static XmlDocument Document(byte[] bytes)
    {
        var doc = new XmlDocument { XmlResolver = null };
        using (var stream = new MemoryStream(bytes))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
        return doc;
    }
    private static string Value(XmlDocument doc, string group, string name)
    {
        return doc.GetElementsByTagName("*").Cast<XmlNode>().Where(n => n.LocalName == name && n.Prefix == group).Select(n => n.InnerText).FirstOrDefault() ?? "";
    }
    private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    private static string HashBytes(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    private static GpsMetadata Coordinate(XmlDocument doc, string path)
    {
        double lat, lon;
        string latRef = Value(doc, "GPS", "GPSLatitudeRef"), lonRef = Value(doc, "GPS", "GPSLongitudeRef");
        if (!Double.TryParse(Value(doc, "GPS", "GPSLatitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
            !Double.TryParse(Value(doc, "GPS", "GPSLongitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out lon) ||
            !(latRef == "N" || latRef == "S") || !(lonRef == "E" || lonRef == "W")) return null;
        try { return new GpsMetadata(Math.Abs(lat) * (latRef == "S" ? -1 : 1), Math.Abs(lon) * (lonRef == "W" ? -1 : 1), null, null, path); }
        catch { return null; }
    }
    private static IList<string> ReadArguments(string file)
    {
        return new[] { "-X", "-G1", "-a", "-n", "-struct", "-u", "-e", "-api", "ImageHashType=SHA256", "-all", "-ImageDataHash", "-validate", "-warning", "-error", "--", file };
    }
    private static GpsToolResult Read(string file)
    {
        var args = ReadArguments(file);
        File.WriteAllLines(Path.Combine(output, Path.GetFileName(file) + ".read.args.txt"), args, new UTF8Encoding(false));
        return runner.Run(args);
    }
    private static void Probe(string file)
    {
        var result = Read(file);
        File.WriteAllBytes(Path.Combine(output, "probe-metadata.xml"), result.Output);
        Console.WriteLine("exit=" + result.ExitCode + "; stderr=" + result.Error);
        var doc = Document(result.Output);
        foreach (var name in new[] { "FileType", "Model", "ImageWidth", "ImageHeight", "ImageDataHash", "Validate", "Warning", "GPSLatitude", "GPSLongitude" })
            foreach (XmlNode node in doc.GetElementsByTagName("*").Cast<XmlNode>().Where(n => n.LocalName == name)) Console.WriteLine(node.Name + "=" + node.InnerText);
        result = runner.Run(new[] { "-X", "-G1", "-b", "-MakerNotes", "-ThumbnailImage", "-PreviewImage", "-JpgFromRaw", "-ICC_Profile", "--", file });
        File.WriteAllBytes(Path.Combine(output, "probe-blocks.xml"), result.Output);
        Console.WriteLine("binary exit=" + result.ExitCode + "; stderr=" + result.Error);
        var blocks = Document(result.Output);
        foreach (XmlNode node in blocks.DocumentElement.FirstChild.ChildNodes)
            if (node.NodeType == XmlNodeType.Element) Console.WriteLine(node.Name + " bytes-text=" + node.InnerText.Length + " start=" + node.InnerText.Substring(0, Math.Min(60, node.InnerText.Length)));
    }
    private static string ParseImage(byte[] bytes)
    {
        using (var stream = new MemoryStream(bytes))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new IOException("No preview frame");
            foreach (var frame in decoder.Frames)
            {
                int stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
                byte[] pixels = new byte[checked(stride * Math.Min(16, frame.PixelHeight))];
                for (int y = 0; y < frame.PixelHeight; y += 16)
                    frame.CopyPixels(new Int32Rect(0, y, frame.PixelWidth, Math.Min(16, frame.PixelHeight - y)), pixels, stride, 0);
            }
            return decoder.Frames[0].PixelWidth + "x" + decoder.Frames[0].PixelHeight;
        }
    }
    private static void Scan(string root)
    {
        root = Path.GetFullPath(root); GpsFileSafety.CheckPath(root);
        var files = new List<string>();
        foreach (string folder in new[] { root, Path.Combine(root, "jpg"), Path.Combine(root, "arw") })
        {
            if (!Directory.Exists(folder)) continue;
            GpsFileSafety.CheckPath(folder);
            files.AddRange(Directory.GetFiles(folder).Where(p => new[] { ".jpg", ".jpeg", ".arw" }.Contains(Path.GetExtension(p).ToLowerInvariant())));
        }
        files = files.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        var csv = new List<string> { "Name,Path,Bytes,SHA256,Model,GPSLatitude,GPSLongitude,GPSStatus,MakerNotesBytes,MakerNotesSHA256,ThumbnailStatus,PreviewStatus,JpgFromRawStatus,ICCStatus,PixelWidth,PixelHeight,ImageDataHash,ExifToolRead,Validation,StrictMetadataGate,WICHeader,Unchanged" };
        var binaryCsv = new List<string> { "File,Tag,Bytes,SHA256,Parse" };
        int index = 0, gpsCount = 0, noGpsCount = 0, rawCount = 0, warnings = 0, fail = 0;
        foreach (string file in files)
        {
            GpsFileSafety.CheckPath(file);
            string originalHash = GpsFileSafety.Hash(file).Replace("-", "");
            long length = new FileInfo(file).Length;
            var result = Read(file);
            File.WriteAllBytes(Path.Combine(output, Path.GetFileName(file) + ".metadata.xml"), result.Output);
            File.WriteAllText(Path.Combine(output, Path.GetFileName(file) + ".stderr.txt"), result.Error ?? "", new UTF8Encoding(false));
            var metadata = Document(result.Output);
            var gps = Coordinate(metadata, file);
            bool coordinateFields = metadata.GetElementsByTagName("*").Cast<XmlNode>().Any(n => n.Prefix == "GPS" && new[] { "GPSLatitude", "GPSLatitudeRef", "GPSLongitude", "GPSLongitudeRef" }.Contains(n.LocalName));
            string gpsStatus = gps != null ? "valid" : coordinateFields ? "invalid/partial" : "absent";
            if (gps != null) gpsCount++; else if (!coordinateFields) noGpsCount++;
            string readStatus = result.ExitCode == 0 && String.IsNullOrWhiteSpace(result.Error) && Value(metadata, "ExifTool", "Error") == "" ? "OK" : "FAILED: " + result.Error + Value(metadata, "ExifTool", "Error");
            if (readStatus != "OK") fail++;
            string validation = Value(metadata, "ExifTool", "Validate");
            var warningNodes = metadata.GetElementsByTagName("*").Cast<XmlNode>().Where(n => n.Prefix == "ExifTool" && (n.LocalName == "Warning" || n.LocalName == "Error")).Select(n => n.InnerText).Distinct().ToArray();
            if (warningNodes.Length > 0) warnings++;
            string strict = "OK";
            try { ExifToolAdapter.ParseXml(result.Output, file); } catch (Exception error) { strict = error.Message; }
            var binaryArgs = new[] { "-X", "-G1", "-b", "-MakerNotes", "-ThumbnailImage", "-PreviewImage", "-JpgFromRaw", "-ICC_Profile", "--", file };
            File.WriteAllLines(Path.Combine(output, Path.GetFileName(file) + ".blocks.args.txt"), binaryArgs, new UTF8Encoding(false));
            var binaryResult = runner.Run(binaryArgs);
            File.WriteAllBytes(Path.Combine(output, Path.GetFileName(file) + ".blocks.xml"), binaryResult.Output);
            var blocks = Document(binaryResult.Output);
            string makerSize = "0", makerHash = "absent";
            var states = new Dictionary<string, string> { { "ThumbnailImage", "absent" }, { "PreviewImage", "absent" }, { "JpgFromRaw", "absent" }, { "ICC_Profile", "absent" } };
            foreach (XmlNode node in blocks.GetElementsByTagName("*").Cast<XmlNode>().Where(n => n.Attributes != null && n.Attributes["datatype", "http://www.w3.org/1999/02/22-rdf-syntax-ns#"] != null))
            {
                byte[] bytes = Convert.FromBase64String(node.InnerText);
                string hash = HashBytes(bytes), parse = "not-image";
                if (node.LocalName.StartsWith("MakerNote", StringComparison.Ordinal)) { makerSize = bytes.Length.ToString(CultureInfo.InvariantCulture); makerHash = hash; }
                else if (node.LocalName == "ICC_Profile") states["ICC_Profile"] = "present " + bytes.Length + " bytes SHA256=" + hash;
                else if (states.ContainsKey(node.LocalName))
                {
                    try { parse = "OK " + ParseImage(bytes); } catch (Exception error) { parse = "FAILED " + error.Message; }
                    states[node.LocalName] = "present " + bytes.Length + " bytes; " + parse + "; SHA256=" + hash;
                }
                binaryCsv.Add(String.Join(",", new[] { Path.GetFileName(file), node.Name, bytes.Length.ToString(CultureInfo.InvariantCulture), hash, parse }.Select(Csv)));
            }
            if (binaryResult.ExitCode != 0 || !String.IsNullOrWhiteSpace(binaryResult.Error)) strict += "; binary read failed: " + binaryResult.Error;
            string wic;
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    wic = decoder.CodecInfo.FriendlyName + "; " + decoder.Frames.Count + " frame(s); " + decoder.Frames[0].PixelWidth + "x" + decoder.Frames[0].PixelHeight;
                }
            }
            catch (Exception error) { wic = "FAILED: " + error.Message; }
            bool unchanged = originalHash == GpsFileSafety.Hash(file).Replace("-", "") && length == new FileInfo(file).Length;
            if (!unchanged) throw new IOException("Baseline changed while scanning: " + file);
            string type = Value(metadata, "File", "FileType");
            if (type == "ARW") rawCount++;
            csv.Add(String.Join(",", new[] { Path.GetFileName(file), file, length.ToString(CultureInfo.InvariantCulture), originalHash,
                Value(metadata, "IFD0", "Model"), gps == null ? "" : gps.Latitude.ToString("R", CultureInfo.InvariantCulture), gps == null ? "" : gps.Longitude.ToString("R", CultureInfo.InvariantCulture),
                gpsStatus, makerSize, makerHash, states["ThumbnailImage"], states["PreviewImage"], states["JpgFromRaw"], states["ICC_Profile"], Value(metadata, "File", "ImageWidth"), Value(metadata, "File", "ImageHeight"), Value(metadata, "File", "ImageDataHash"),
                readStatus, validation + (warningNodes.Length == 0 ? "" : "; " + String.Join(" | ", warningNodes)), strict, wic, unchanged ? "YES" : "NO" }.Select(Csv)));
            index++;
            if (index % 10 == 0 || index == files.Count) Console.WriteLine("Scanned " + index + "/" + files.Count + "; GPS valid=" + gpsCount + "; absent=" + noGpsCount + "; warnings=" + warnings + "; latest=" + Path.GetFileName(file));
            File.WriteAllLines(Path.Combine(output, "inventory.csv"), csv, new UTF8Encoding(true));
            File.WriteAllLines(Path.Combine(output, "binary-blocks.csv"), binaryCsv, new UTF8Encoding(true));
        }
        string summary = "Files=" + files.Count + "; ARW=" + rawCount + "; valid GPS=" + gpsCount + "; absent GPS=" + noGpsCount + "; files with validation warning=" + warnings + "; ExifTool failed=" + fail + "; all baseline hashes unchanged.\nRoot=" + root + "\n";
        File.WriteAllText(Path.Combine(output, "scan-summary.txt"), summary, new UTF8Encoding(false)); Console.WriteLine(summary);
    }
    private static void Preflight(string baselineRoot, string sourceName, string targetName)
    {
        if (Path.GetFileName(sourceName) != sourceName || Path.GetFileName(targetName) != targetName) throw new ArgumentException("Use exact baseline JPG filenames only.");
        baselineRoot = Path.GetFullPath(baselineRoot);
        string source = Path.Combine(baselineRoot, "jpg", sourceName), target = Path.Combine(baselineRoot, "jpg", targetName);
        GpsFileSafety.ValidateJpg(baselineRoot, source); GpsFileSafety.ValidateJpg(baselineRoot, target);
        string working = Path.Combine(output, "working");
        if (Directory.Exists(working)) throw new IOException("Working directory already exists; refuse to overwrite.");
        Directory.CreateDirectory(Path.Combine(working, "jpg")); Directory.CreateDirectory(Path.Combine(working, "arw"));
        var paths = new[] { source, target };
        var report = new StringBuilder("READ-ONLY PREFLIGHT; no ExifTool write command is executed by this mode.\n");
        foreach (string path in paths)
        {
            string destination = Path.Combine(working, Path.GetExtension(path).Equals(".arw", StringComparison.OrdinalIgnoreCase) ? "arw" : "jpg", Path.GetFileName(path));
            string before = GpsFileSafety.Hash(path);
            File.Copy(path, destination, false);
            if (GpsFileSafety.Hash(destination) != before || GpsFileSafety.Hash(path) != before) throw new IOException("Working-copy fingerprint mismatch.");
            report.AppendLine("Copied: " + destination + " | SHA256=" + before.Replace("-", ""));
        }
        var adapter = new ExifToolAdapter(runner);
        try
        {
            var plan = new GpsEditPlan(working, Path.Combine(working, "jpg", sourceName), Path.Combine(working, "jpg", targetName), adapter);
            report.AppendLine("PREFLIGHT PASS; targets=" + plan.Targets.Count + "; source=" + plan.Source.Display + "; NO WRITES EXECUTED.");
        }
        catch (Exception error)
        {
            report.AppendLine("PREFLIGHT BLOCKED: " + error.Message + "; NO WRITES EXECUTED.");
        }
        foreach (string path in paths)
        {
            string destination = Path.Combine(working, Path.GetExtension(path).Equals(".arw", StringComparison.OrdinalIgnoreCase) ? "arw" : "jpg", Path.GetFileName(path));
            if (GpsFileSafety.Hash(destination) != GpsFileSafety.Hash(path)) throw new IOException("Preflight changed a file.");
        }
        report.AppendLine("Baseline and both JPG working copies remain byte-identical; RAW not enumerated; no scratch, backup or transaction journal was created.");
        File.WriteAllText(Path.Combine(output, "preflight-result.txt"), report.ToString(), new UTF8Encoding(false));
        Console.WriteLine(report.ToString());
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            runner = new ExifToolProcessRunner(args[1], 30000);
            output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            var version = new ExifToolAdapter(runner).Version;
            Console.WriteLine("ExifTool " + version + " - READ ONLY");
            if (args[0] == "probe") { Probe(Path.GetFullPath(args[3])); return 0; }
            if (args[0] == "scan") { Scan(args[3]); return 0; }
            if (args[0] == "preflight") { Preflight(args[3], args[4], args[5]); return 0; }
            throw new ArgumentException("Unknown diagnostic mode.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
