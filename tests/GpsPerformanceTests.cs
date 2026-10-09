using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
internal static class GpsPerformanceTests {
 sealed class Counter : IGpsToolRunner {
  public readonly IGpsToolRunner Inner; public int Calls; public readonly List<string> Log=new List<string>();
  public Counter(IGpsToolRunner inner){Inner=inner;}
  public GpsToolResult Run(IList<string> args){var sw=Stopwatch.StartNew();Calls++;try{return Inner.Run(args);}finally{Log.Add(args[0]+" "+(args.Count>1?args[1]:"")+" "+Path.GetFileName(args.Last())+" ms="+sw.ElapsedMilliseconds);}}
 }
 [STAThread] static int Main(string[] args){try{
  string baseline=Path.Combine(GpsTestFixture.Baseline(args[3]),"jpg");
  string root=GpsTestFixture.Working(args[0]);Directory.CreateDirectory(Path.Combine(root,"jpg"));
  var init=Stopwatch.StartNew();var approved=new ApprovedExifToolRunner(args[1]);var runner=new Counter(approved);var adapter=new ExifToolAdapter(runner);
  Console.WriteLine("SESSION_INIT_MS="+init.ElapsedMilliseconds);
  var identityPerf=typeof(ApprovedExifToolRunner).GetField("IdentityPerformance",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);if(identityPerf!=null)Console.WriteLine("SESSION_IDENTITY "+identityPerf.GetValue(approved));
  if(args.Length>2&&args[2]=="identity") {var method=typeof(ApprovedExifToolRunner).GetMethod("CheckIdentity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var watch=Stopwatch.StartNew();for(int i=0;i<57;i++)method.Invoke(approved,null);Console.WriteLine("MANIFEST_ONLY_PASSES=57 SHA256_FILES=29070 MANIFEST_ONLY_MS="+watch.ElapsedMilliseconds);return 0;}
  if(args.Length>2&&args[2]=="probe") {var result=runner.Run(new[]{"-X","-G1","-a","-n","-struct","-u","-e","-b","-api","ImageHashType=SHA256","-all","-MakerNotes","-ThumbnailImage","-PreviewImage","-JpgFromRaw","-ICC_Profile","-ImageDataHash","-validate","-warning","-error","--",Path.Combine(baseline,"DSC01270.JPG")});File.WriteAllBytes(Path.Combine(root,"probe.xml"),result.Output);Console.WriteLine("PROBE "+result.Error);return 0;}
  for(int i=0;i<(args.Length>2?Int32.Parse(args[2]):1);i++) {
   string source=Path.Combine(root,"jpg","DSC01265.JPG"),target=Path.Combine(root,"jpg","TARGET"+i+".JPG");if(!File.Exists(source))File.Copy(Path.Combine(baseline,"DSC01265.JPG"),source,false);File.Copy(Path.Combine(baseline,"DSC01270.JPG"),target,false);
   int calls=runner.Calls;var total=Stopwatch.StartNew();var plan=new GpsEditPlan(root,source,target,adapter);long acquire=total.ElapsedMilliseconds;
   var result=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(plan,plan.Targets[0],false);total.Stop();
   Console.WriteLine("RUN="+i+" BASELINE_MS="+acquire+" TOTAL_MS="+total.ElapsedMilliseconds+" PROCESS_CALLS="+(runner.Calls-calls)+" SUCCESS="+result.Success+" "+result.Message);
   if(!result.Success)throw new Exception(result.Message);
   var perf=typeof(GpsFileResult).GetField("Diagnostics");if(perf!=null)Console.WriteLine(perf.GetValue(result));
  }
  foreach(var line in runner.Log)Console.WriteLine("CALL "+line);
  var disposable=(object)approved as IDisposable;if(disposable!=null)disposable.Dispose();return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
