using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
// Release evidence only. Calls real production readers, never writes metadata.
internal static class FreezeReleaseBaseline {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=128*1024*1024};
 static string Hash(string p){return GpsFileSafety.Hash(p).Replace("-","");}
 static void Save(string p,object x){if(File.Exists(p))throw new IOException("Evidence exists");File.WriteAllText(p,Json.Serialize(x),new UTF8Encoding(false));}
 static object Structure(GpsSnapshot s){var j=s.Jpeg;return new{validate="0 Error / 0 Warning",j.Decode,s.ImageHash,s.BinaryHashes,exifBase=j.ExifBase,exifEnd=j.ExifEnd,makerOffset=j.MakerOffset,makerLength=j.MakerRaw.Length,hidden=j.Hidden,mpImage=j.MPImage,blocks=j.Blocks,hasGpsIfd=j.Dirs.ContainsKey("GPS"),hasGpsInfo=j.Dirs["IFD0"].Entries.Any(e=>e.Id==0x8825),relocation=j.Relocation};}
 [STAThread]static int Main(string[] args){try{
  string old=GpsTestFixture.Baseline(args[0]),pairs=args[1],root=GpsTestFixture.Working(args[2]);
  if(Directory.Exists(root))throw new IOException("Never replace a frozen baseline");
  var inputs=new List<Tuple<string,string,string>>();
  foreach(var kv in GpsTestFixture.HistoricalHashes)inputs.Add(Tuple.Create(Path.Combine(old,kv.Key.Replace('/',Path.DirectorySeparatorChar)),"existing/"+kv.Key,kv.Key.EndsWith("ARW")?"paired-RAW-no-write":kv.Key.Contains("01265")?"GPS-source":kv.Key.Contains("01267")?"ExistingGpsIfd-overwrite":"ExistingGpsIfd-missing-GPS"));
  string[] lines=File.ReadAllLines(pairs);if(lines.Length!=9)throw new IOException("Nine pairs required");
  for(int i=0;i<lines.Length;i++){string[] p=lines[i].Split('|');if(p.Length!=3)throw new IOException("Pair schema");foreach(string v in p)GpsTestFixture.Working(v);
   string name=Path.GetFileName(p[0]);inputs.Add(Tuple.Create(p[0],"create/before/"+name,"CreateGpsIfd-no-GPS-IFD"));inputs.Add(Tuple.Create(p[1],"create/after/"+name,"CreateGpsIfd-positive-evidence-and-memory-mutations"));
  }
  string source=lines[0].Split('|')[2];inputs.Add(Tuple.Create(source,"create/source/SOURCE.JPG","GPS-source-for-CreateGpsIfd"));
  var locks=new List<FileStream>();var rows=new List<object>();
  try{
   foreach(var p in inputs){GpsFileSafety.CheckPath(p.Item1);locks.Add(new FileStream(p.Item1,FileMode.Open,FileAccess.Read,FileShare.Read));}
   Directory.CreateDirectory(root);
   using(var adapter=new ExifToolAdapter(args[3]))foreach(var input in inputs){
    string sha=Hash(input.Item1),dest=Path.Combine(root,input.Item2.Replace('/',Path.DirectorySeparatorChar));object gps,structure;
    if(input.Item1.EndsWith(".JPG",StringComparison.OrdinalIgnoreCase)){var s=adapter.Read(input.Item1);gps=new{coordinates=s.Gps==null?null:s.Gps.Display,status=s.Status,s.HasCoordinateFields};structure=Structure(s);
     if(input.Item3=="CreateGpsIfd-no-GPS-IFD"&&(s.Jpeg.Dirs.ContainsKey("GPS")||s.Jpeg.Dirs["IFD0"].Entries.Any(e=>e.Id==0x8825)))throw new IOException("Not a no-GPS-IFD fixture");
    }else{gps=new{status="V",coordinates=(string)null};structure=new{role="unchanged paired Sony ARW; no GPS writer",baselineSha=sha};}
    Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(input.Item1,dest,false);if(Hash(dest)!=sha||Hash(input.Item1)!=sha)throw new IOException("Copy identity changed");
    rows.Add(new{relativePath=input.Item2,size=new FileInfo(dest).Length,sha256=sha,role=input.Item3,gpsState=gps,relevantStructure=structure,provenance=input.Item1});Console.WriteLine("Frozen: "+input.Item2);
   }
   string pairPath=Path.Combine(root,"create-pairs.txt");File.WriteAllLines(pairPath,lines.Select(line=>{string name=Path.GetFileName(line.Split('|')[0]);return Path.Combine(root,"create","before",name)+"|"+Path.Combine(root,"create","after",name)+"|"+Path.Combine(root,"create","source","SOURCE.JPG");}).ToArray(),new UTF8Encoding(false));
   rows.Add(new{relativePath="create-pairs.txt",size=new FileInfo(pairPath).Length,sha256=Hash(pairPath),role="explicit evidence and mutation input mapping",gpsState="not-applicable",relevantStructure="nine before/after/source triples"});
   Save(Path.Combine(root,"release-baseline-1.5-manifest.json"),new{status="CURRENT RELEASE PROTECTION BASELINE / FROZEN",version="1.5",toolVersion="13.59",toolManifest="94C298C2C60460507FFA7F3098F26B69E56490A08269AFB2AFE639F84048E750",fixtures=rows,mutationPolicy="Mutation tests operate in memory or new working copies, never these frozen inputs"});
   Console.WriteLine("PASS frozen current release baseline: "+rows.Count+" files");return 0;
  }finally{foreach(var s in locks)s.Dispose();}
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
