using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

// Phase 3A diagnostic ONLY. All photo streams are FileAccess.Read. No adapter
// edit APIs, write commands, repairs, replacement operations or UI are invoked.
internal static class GpsBaselineAudit
{
    static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
    static string Row(params string[] s) { return String.Join(",", s.Select(Q)); }
    static string Hash(byte[] b) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", ""); }
    static string Digest(string s) { return Hash(Encoding.UTF8.GetBytes(s)); }
    static XmlDocument Xml(byte[] b) {
        var doc = new XmlDocument { XmlResolver = null };
        using (var stream = new MemoryStream(b))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
        return doc;
    }
    static IEnumerable<XmlNode> Nodes(XmlDocument d) { return d.GetElementsByTagName("*").Cast<XmlNode>(); }
    static string V(XmlDocument d, string group, string tag) { return Nodes(d).Where(n => n.Prefix == group && n.LocalName == tag).Select(n => n.InnerText).FirstOrDefault() ?? ""; }
    class Entry { public int Index, Tag, Type; public uint Count, Field; public long Position, Data, Bytes; public bool Inline; }
    class Tiff : IDisposable {
        FileStream stream; BinaryReader reader;
        static readonly int[] Sizes = { 0,1,1,2,4,8,1,1,2,4,8,4,8,4 };
        public long Length { get { return stream.Length; } }
        public uint First;
        public Tiff(string file) {
            stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x4949 || reader.ReadUInt16() != 42) throw new IOException("Audit only supports classic little-endian TIFF, not a writer");
            First = reader.ReadUInt32();
        }
        void Seek(long p, long bytes) { if (p < 0 || bytes < 0 || p > Length - bytes) throw new IOException("Out of bounds: " + p + "/" + bytes); stream.Position = p; }
        public List<Entry> Directory(long offset, out uint next) {
            Seek(offset,2); int count = reader.ReadUInt16(); Seek(offset+2,checked(count*12L+4));
            var list = new List<Entry>();
            for (int i=0;i<count;i++) {
                var e = new Entry { Index=i, Position=stream.Position, Tag=reader.ReadUInt16(), Type=reader.ReadUInt16(), Count=reader.ReadUInt32(), Field=reader.ReadUInt32() };
                if (e.Type<=0 || e.Type>=Sizes.Length) throw new IOException("Unsupported field type");
                e.Bytes=checked((long)e.Count*Sizes[e.Type]); e.Inline=e.Bytes<=4; e.Data=e.Inline?e.Position+8:e.Field;
                if (e.Data < 0 || e.Data > Length-e.Bytes) throw new IOException("Tag data outside file");
                list.Add(e);
            }
            next=reader.ReadUInt32(); return list;
        }
        public uint[] Numbers(Entry e) {
            if(e.Type!=3 && e.Type!=4 && e.Type!=13) throw new IOException("Not an integer pointer");
            Seek(e.Data,e.Bytes); var a=new uint[checked((int)e.Count)];
            for(int i=0;i<a.Length;i++) a[i]=e.Type==3?reader.ReadUInt16():reader.ReadUInt32(); return a;
        }
        public byte[] Bytes(Entry e) { Seek(e.Data,e.Bytes); return reader.ReadBytes(checked((int)e.Bytes)); }
        public void Dispose() { reader.Dispose(); stream.Dispose(); }
    }
    static Dictionary<int,string> names = new Dictionary<int,string> {{0x011a,"XResolution"},{0x7000,"SonyRawFileType"},{0x0111,"StripOffsets"},{0x74c7,"SonyCropTopLeft"},{0x0102,"BitsPerSample"},{0x0142,"TileWidth"}};
    static void DirectoryRows(Tiff t, string file,string group,long offset,List<Entry> entries,List<string> rows,List<string> schemas) {
        string schema=String.Join(";",entries.Select(e=>e.Tag.ToString("x4")+":"+e.Type+":"+e.Count));
        schemas.Add(Row(file,group,offset.ToString(),entries.Count.ToString(),Digest(schema),schema));
        for(int i=0;i<entries.Count;i++) {
            var e=entries[i]; string name=group=="SubIFD"&&names.ContainsKey(e.Tag)?names[e.Tag]:"";
            rows.Add(Row(file,group,offset.ToString(),e.Index.ToString(),e.Position.ToString(),"0x"+e.Tag.ToString("x4"),name,e.Type.ToString(),e.Count.ToString(),e.Bytes.ToString(),e.Inline?"inline":"offset",e.Field.ToString(),e.Data.ToString(),Hash(t.Bytes(e)),i==0?"":"0x"+entries[i-1].Tag.ToString("x4"),i>0&&e.Tag<entries[i-1].Tag?"YES":"NO","PASS"));
        }
    }
    static void SpanRows(Tiff t,string file,string group,List<Entry> entries,int start,int size,List<string> spans) {
        var a=entries.FirstOrDefault(e=>e.Tag==start); var b=entries.FirstOrDefault(e=>e.Tag==size);
        if(a==null || b==null) return;
        uint[] offsets=t.Numbers(a), lengths=t.Numbers(b);
        if(offsets.Length!=lengths.Length) throw new IOException("Span cardinality mismatch");
        for(int i=0;i<offsets.Length;i++) {
            if(offsets[i]>t.Length-(long)lengths[i]) throw new IOException("Image payload outside file");
            spans.Add(Row(file,group,"0x"+start.ToString("x4"),i.ToString(),offsets[i].ToString(),lengths[i].ToString(),"PASS"));
        }
    }
    static void VerboseSchema(string name,byte[] data,List<string> rows) {
        var stack=new Dictionary<int,Tuple<string,List<string>>>(); var occurrence=new Dictionary<string,int>(); var declared=new Dictionary<int,int>();
        Action<int> flush=depth=>{
            var pair=stack[depth]; int n=occurrence.ContainsKey(pair.Item1)?occurrence[pair.Item1]+1:1; occurrence[pair.Item1]=n;
            if(pair.Item2.Count!=declared[depth]) throw new IOException("Incomplete ExifTool directory transcript: "+name+" "+pair.Item1);
            string signature=String.Join(";",pair.Item2); rows.Add(Row(name,pair.Item1,n.ToString(),Digest(signature),signature,declared[depth].ToString(),pair.Item2.Count.ToString(),"PASS")); stack.Remove(depth); declared.Remove(depth);
        };
        foreach(string line in Encoding.UTF8.GetString(data).Split('\n')) {
            Match dir=Regex.Match(line,@"^(.*?)\+ \[([^\]]+) directory with (\d+) entries");
            if(dir.Success) {
                int depth=dir.Groups[1].Value.Count(c=>c=='|');
                foreach(int k in stack.Keys.Where(k=>k>=depth).OrderByDescending(k=>k).ToArray()) flush(k);
                stack[depth]=Tuple.Create(dir.Groups[2].Value,new List<string>()); declared[depth]=Int32.Parse(dir.Groups[3].Value,CultureInfo.InvariantCulture); continue;
            }
            Match tag=Regex.Match(line,@"^(.*?)\- Tag (0x[0-9a-fA-F]+) \((.*)\):");
            if(tag.Success) { int depth=tag.Groups[1].Value.Count(c=>c=='|')-1; if(stack.ContainsKey(depth)) stack[depth].Item2.Add(tag.Groups[2].Value+":"+tag.Groups[3].Value); }
        }
        foreach(int k in stack.Keys.OrderByDescending(k=>k).ToArray()) flush(k);
    }
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length==2 && args[0]=="schemas") {
                var rows=new List<string>{"File,Directory,Occurrence,EntryDescriptorSHA256,EntryDescriptors,DeclaredEntries,CapturedEntries,Completeness"};
                foreach(string path in Directory.GetFiles(args[1],"*.ARW.v3.txt").OrderBy(p=>p)) VerboseSchema(Path.GetFileName(path).Replace(".v3.txt",""),File.ReadAllBytes(path),rows);
                File.WriteAllLines(Path.Combine(args[1],"exiftool-directory-schemas.csv"),rows,new UTF8Encoding(true));
                Console.WriteLine("Recomputed directory schemas from existing read-only ExifTool transcripts."); return 0;
            }
            string root=Path.GetFullPath(args[1]), output=Path.GetFullPath(args[2]), baseline=Path.GetFullPath(args[3]);
            Directory.CreateDirectory(output);
            var runner=new ExifToolProcessRunner(args[0],30000);
            Console.WriteLine("Read-only audit; ExifTool="+new ExifToolAdapter(runner).Version);
            var warnings=new List<string>{"File,GPSGroup,Order,Warning,TagID,IFD,OccurrencesInFile,AggregateInversions"};
            var files=new List<string>{"File,GPSGroup,SHA256Before,SHA256After,Validate,Errors,WarningCount,OrderedSignatureSHA256,OrderedSignature,Software,ReadStatus"};
            var entriesCsv=new List<string>{"File,IFD,DirectoryOffset,IndexZeroBased,EntryOffset,TagID,Name,Type,Count,DataBytes,Storage,ValueOrOffsetField,DataOffset,DataSHA256,PreviousTagID,Descending,Bounds"};
            var schemas=new List<string>{"File,IFD,Offset,Entries,SchemaSHA256,OrderedTagTypeCount"};
            var spans=new List<string>{"File,IFD,OffsetTagID,Index,PayloadOffset,PayloadBytes,Bounds"};
            var verboseSchemas=new List<string>{"File,Directory,Occurrence,EntryDescriptorSHA256,EntryDescriptors,DeclaredEntries,CapturedEntries,Completeness"};
            string[] photos=Directory.GetFiles(Path.Combine(root,"arw")).Where(p=>Path.GetExtension(p).Equals(".arw",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
            int index=0;
            foreach(string file in photos) {
                GpsFileSafety.CheckPath(file); string name=Path.GetFileName(file), before=GpsFileSafety.Hash(file).Replace("-","");
                var original=Xml(File.ReadAllBytes(Path.Combine(baseline,name+".metadata.xml")));
                string gps=V(original,"GPS","GPSLatitude")==""?"absent":"present";
                var readArgs=new[]{"-X","-G1","-a","-n","-validate","-warning","-error","--",file};
                var result=runner.Run(readArgs); if(result.ExitCode!=0||!String.IsNullOrWhiteSpace(result.Error)) throw new IOException(result.Error);
                File.WriteAllLines(Path.Combine(output,name+".validate.args.txt"),readArgs,new UTF8Encoding(false));
                File.WriteAllBytes(Path.Combine(output,name+".validate.xml"),result.Output);
                var d=Xml(result.Output); var w=Nodes(d).Where(n=>n.Prefix=="ExifTool"&&n.LocalName=="Warning").Select(n=>n.InnerText).ToArray();
                int errorCount=Nodes(d).Count(n=>n.Prefix=="ExifTool"&&n.LocalName=="Error");
                for(int i=0;i<w.Length;i++) {
                    Match m=Regex.Match(w[i],@"Tag ID (0x[0-9a-fA-F]+).* in (\S+)$"), a=Regex.Match(w[i],@"\[x(\d+)\]");
                    warnings.Add(Row(name,gps,(i+1).ToString(),w[i],m.Success?m.Groups[1].Value:"",m.Success?m.Groups[2].Value:"SubIFD",w.Count(s=>s==w[i]).ToString(),a.Success?a.Groups[1].Value:""));
                }
                var verboseArgs=new[]{"-v3","-validate","--",file};
                var verbose=runner.Run(verboseArgs); if(verbose.ExitCode!=0||!String.IsNullOrWhiteSpace(verbose.Error)) throw new IOException(verbose.Error);
                File.WriteAllBytes(Path.Combine(output,name+".v3.txt"),verbose.Output);
                File.WriteAllLines(Path.Combine(output,name+".v3.args.txt"),verboseArgs,new UTF8Encoding(false));
                // ExifTool's directory rendering is used for encrypted/private SR2
                // schemas. This harness does not decrypt or rebuild those blocks.
                VerboseSchema(name,verbose.Output,verboseSchemas);
                using(var t=new Tiff(file)) {
                    uint next; var ifd0=t.Directory(t.First,out next); DirectoryRows(t,name,"IFD0",t.First,ifd0,entriesCsv,schemas); SpanRows(t,name,"IFD0",ifd0,0x201,0x202,spans);
                    uint chain=next; int chainIndex=1;
                    while(chain!=0) { if(chainIndex>4) throw new IOException("Unexpected IFD chain"); var table=t.Directory(chain,out next); string g="IFD"+chainIndex++; DirectoryRows(t,name,g,chain,table,entriesCsv,schemas); SpanRows(t,name,g,table,0x201,0x202,spans); chain=next; }
                    foreach(int id in new[]{0x14a,0x8769,0x8825}) {
                        var pointer=ifd0.FirstOrDefault(e=>e.Tag==id); if(pointer==null)continue;
                        foreach(uint offset in t.Numbers(pointer)) {
                            string g=id==0x14a?"SubIFD":id==0x8769?"ExifIFD":"GPS";
                            var table=t.Directory(offset,out next); DirectoryRows(t,name,g,offset,table,entriesCsv,schemas);
                            if(id==0x14a) { SpanRows(t,name,g,table,0x111,0x117,spans); SpanRows(t,name,g,table,0x144,0x145,spans); }
                            if(id==0x8769) {
                                var maker=table.FirstOrDefault(e=>e.Tag==0x927c);
                                if(maker!=null) { var makerTable=t.Directory(maker.Data,out next); if(makerTable.Any(e=>e.Position+12>maker.Data+maker.Bytes || (!e.Inline&&(e.Data<maker.Data||e.Data+e.Bytes>maker.Data+maker.Bytes)))) throw new IOException("MakerNotes entry outside original block"); DirectoryRows(t,name,"MakerNotes",maker.Data,makerTable,entriesCsv,schemas); }
                            }
                        }
                    }
                }
                string after=GpsFileSafety.Hash(file).Replace("-",""); if(before!=after)throw new IOException("Baseline changed");
                files.Add(Row(name,gps,before,after,V(d,"ExifTool","Validate"),errorCount.ToString(),w.Length.ToString(),Digest(String.Join("\n",w)),String.Join(" | ",w),V(original,"IFD0","Software"),"PASS"));
                index++; if(index%10==0||index==photos.Length)Console.WriteLine("Audited "+index+"/"+photos.Length+"; "+name+"; actual warnings="+w.Length);
                File.WriteAllLines(Path.Combine(output,"files.csv"),files,new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(output,"warnings.csv"),warnings,new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(output,"ifd-entries.csv"),entriesCsv,new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(output,"directory-schemas.csv"),schemas,new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(output,"image-spans.csv"),spans,new UTF8Encoding(true));
                File.WriteAllLines(Path.Combine(output,"exiftool-directory-schemas.csv"),verboseSchemas,new UTF8Encoding(true));
            }
            Console.WriteLine("Completed "+photos.Length+" read-only ARW audits; all hashes unchanged."); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
}
