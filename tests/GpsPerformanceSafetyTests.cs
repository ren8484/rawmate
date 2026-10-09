using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
internal static class GpsPerformanceSafetyTests {
 static int assertions;
 static void Check(bool pass,string label){assertions++;if(!pass)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Reject(Action action,string label){bool failed=false;try{action();}catch{failed=true;}Check(failed,label);}
 static void CopyTree(string source,string target){Directory.CreateDirectory(target);foreach(var dir in Directory.GetDirectories(source,"*",SearchOption.AllDirectories))Directory.CreateDirectory(target+dir.Substring(source.Length));foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))File.Copy(file,target+file.Substring(source.Length));}
 static void BinaryEquivalence(string file,ExifToolAdapter adapter,IGpsToolRunner tool) {
  var snapshot=adapter.Read(file);
  foreach(var tag in new[]{"MakerNotes","ThumbnailImage","PreviewImage","JpgFromRaw","ICC_Profile"}) {
   var raw=tool.Run(new[]{"-b","-"+tag,"--",file});if(raw.ExitCode!=0||!String.IsNullOrWhiteSpace(raw.Error))throw new IOException(raw.Error);
   string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=raw.Output.Length==0?"absent":BitConverter.ToString(sha.ComputeHash(raw.Output));
   if(snapshot.BinaryHashes[tag]!=hash)Console.WriteLine("DIFF "+tag+" merged="+snapshot.BinaryHashes[tag]+" raw="+hash+" rawLength="+raw.Output.Length+" rawTail="+BitConverter.ToString(raw.Output.Skip(Math.Max(0,raw.Output.Length-32)).ToArray()));
   Check(snapshot.BinaryHashes[tag]==hash,"merged XML == independent -b extraction: "+Path.GetFileName(file)+" / "+tag);
  }
 }
 [STAThread] static int Main(string[] args){try{
  string baseline=GpsTestFixture.Baseline(args[2]);string root=GpsTestFixture.Working(args[0]);Directory.CreateDirectory(Path.Combine(root,"jpg"));
  string original=Path.Combine(baseline,"jpg","DSC01270.JPG"),file=Path.Combine(root,"jpg","CACHE.JPG");File.Copy(original,file,false);
  using(var tool=new ApprovedExifToolRunner(args[1])) using(var adapter=new ExifToolAdapter(tool)) {
   var first=adapter.ReadBaseline(file);var perf=new GpsPerformance();GpsSnapshot second;
   using(perf.Activate())second=adapter.ReadBaseline(file);
   Check(Object.ReferenceEquals(first,second)&&perf.CacheHits==1&&perf.ProcessCalls==0&&perf.MainDecodes==0,"same fingerprint cache hit without process/decode");
   DateTime time=File.GetLastWriteTimeUtc(file);var bytes=File.ReadAllBytes(file);bytes[48]^=1;File.WriteAllBytes(file,bytes);File.SetLastWriteTimeUtc(file,time);
   bool rejected=false;GpsSnapshot changed=null;try{changed=adapter.ReadBaseline(file);}catch{rejected=true;}
   Check(rejected||!Object.ReferenceEquals(first,changed),"same length and restored timestamp content mutation cannot hit old baseline");
   File.Copy(original,file,true);first=adapter.ReadBaseline(file);
   string moved=file+".old";File.Move(file,moved);File.Copy(original,file);File.SetLastWriteTimeUtc(file,File.GetLastWriteTimeUtc(moved));second=adapter.ReadBaseline(file);
   Check(!Object.ReferenceEquals(first,second),"same bytes/path/time with new Windows file ID invalidates baseline");
   using(var writer=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))Reject(delegate{adapter.ReadBaseline(file);},"active external writer prevents baseline lock/cache reuse");
   BinaryEquivalence(file,adapter,tool);
   BinaryEquivalence(Path.Combine(baseline,"jpg","DSC01265.JPG"),adapter,tool);
   adapter.Dispose();Reject(delegate{adapter.ReadBaseline(file);},"disposed adapter/cache cannot be reused");
  }
  string cloned=Path.Combine(root,"cloned-tool");CopyTree(Path.GetDirectoryName(args[1]),cloned);string exe=Path.Combine(cloned,"exiftool(-k).exe");
  using(var tool=new ApprovedExifToolRunner(exe)) {
   Check(tool.IdentityPerformance.ManifestPasses==1&&tool.IdentityPerformance.ManifestFiles==510,"one complete session manifest / 510 SHA256 files");
   Reject(delegate{using(var write=new FileStream(exe,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){}},"retained dependency lock blocks executable writes");
   var deps=(List<FileStream>)typeof(ApprovedExifToolRunner).GetField("locks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tool);
   deps[0].Dispose();Reject(delegate{tool.Run(new[]{"-ver"});},"lost dependency lock blocks process before execution");
  }
  using(var tool=new ApprovedExifToolRunner(exe)) {
   Check(tool.IdentityPerformance.ManifestPasses==1,"new session fully revalidates manifest");
   File.WriteAllText(Path.Combine(cloned,"unexpected.txt"),"isolated test");Reject(delegate{tool.Run(new[]{"-ver"});},"unexpected dependency directory member blocks process");
  }
  string source=Path.Combine(root,"jpg","SOURCE.JPG"),target=Path.Combine(root,"jpg","TEMP-CHANGE.JPG");File.Copy(Path.Combine(baseline,"jpg","DSC01265.JPG"),source);File.Copy(original,target);
  using(var adapter=new ExifToolAdapter(args[1])) {
   var plan=new GpsEditPlan(root,source,target,adapter);string hash=GpsFileSafety.Hash(target);
   var result=new GpsFileTransaction(adapter,delegate(string temporary){File.AppendAllText(temporary,"external mutation after verified decode");}).Execute(plan,plan.Targets[0],false);
   Check(!result.Success&&GpsFileSafety.Hash(target)==hash&&File.Exists(result.RecoveryPath),"temporary modified after verification cannot commit; original unchanged/evidence retained");
  }
  if(args.Length>3)using(var tool=new ApprovedExifToolRunner(args[1]))using(var adapter=new ExifToolAdapter(tool))
    foreach(string name in new[]{"DSC01267.JPG","DSC01270.JPG"})BinaryEquivalence(Path.Combine(args[3],name),adapter,tool);
  Console.WriteLine("PASS "+assertions+" additional performance safety assertions; "+root);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
