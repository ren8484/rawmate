using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
internal static class JpgGpsIntegrationTests {
 [STAThread] static int Main(string[] args) { try {
  string baseline=GpsTestFixture.Baseline(args[2]);
  string root=GpsTestFixture.Working(args[0]); Directory.CreateDirectory(Path.Combine(root,"jpg"));Directory.CreateDirectory(Path.Combine(root,"arw"));
  foreach(string name in new[]{"DSC01265.JPG","DSC01270.JPG"}) File.Copy(Path.Combine(baseline,"jpg",name),Path.Combine(root,"jpg",name),false);
  File.Copy(Path.Combine(baseline,"arw","DSC01270.ARW"),Path.Combine(root,"arw","DSC01270.ARW"),false);
  var adapter=new ExifToolAdapter(args[1]);string source=Path.Combine(root,"jpg","DSC01265.JPG"),target=Path.Combine(root,"jpg","DSC01270.JPG"),raw=Path.Combine(root,"arw","DSC01270.ARW");string rh=GpsFileSafety.Hash(raw);
  var before=adapter.Read(target);var plan=new GpsEditPlan(root,source,target,adapter);
  var result=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(plan,plan.Targets[0],false);if(!result.Success)throw new Exception(result.Message);
  var after=adapter.Read(target);ExifToolAdapter.Verify(before,after,plan.Source);
  if(rh!=GpsFileSafety.Hash(raw)||Directory.GetFiles(root,"*.xmp",SearchOption.AllDirectories).Length>0)throw new Exception("RAW/XMP boundary");
  var overwrite=new GpsEditPlan(root,source,target,adapter);string prior=GpsFileSafety.Hash(target);
  if(new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(overwrite,overwrite.Targets[0],false).Success || prior!=GpsFileSafety.Hash(target))throw new Exception("overwrite confirmation bypass");
  result=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(overwrite,overwrite.Targets[0],true);if(!result.Success)throw new Exception(result.Message);
  string different=Path.Combine(root,"jpg","DSC01267.JPG");File.Copy(Path.Combine(baseline,"jpg","DSC01267.JPG"),different,false);
  var differentPlan=new GpsEditPlan(root,source,different,adapter);string dh=GpsFileSafety.Hash(different);
  if(!differentPlan.Targets[0].Before.HasCoordinateFields||plan.Source.SameCoordinates(differentPlan.Targets[0].Before.Gps))throw new Exception("different GPS fixture");
  if(new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(differentPlan,differentPlan.Targets[0],false).Success||GpsFileSafety.Hash(different)!=dh)throw new Exception("different GPS confirmation bypass");
  result=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(differentPlan,differentPlan.Targets[0],true);if(!result.Success)throw new Exception(result.Message);
  ExifToolAdapter.Verify(differentPlan.Targets[0].Before,adapter.Read(different),plan.Source);
  File.Copy(Path.Combine(baseline,"jpg","DSC01270.JPG"),Path.Combine(root,"jpg","FAIL.JPG"),false);
  var fail=new GpsEditPlan(root,source,Path.Combine(root,"jpg","FAIL.JPG"),adapter);string hash=GpsFileSafety.Hash(fail.JpgPath);
  result=new GpsFileTransaction(adapter,delegate(string p){throw new IOException("injected precommit verification failure");}).Execute(fail,fail.Targets[0],false);
  if(result.Success||hash!=GpsFileSafety.Hash(fail.JpgPath)||!File.Exists(result.RecoveryPath))throw new Exception("failure preservation");
  // New material is explicitly supplied; never discover inputs by filename.
  string[] pairs=File.ReadAllLines(args[3]);if(pairs.Length!=9)throw new Exception("Nine frozen CreateGpsIfd pairs required");
  foreach(string line in pairs) {
   string[] p=line.Split('|');GpsTestFixture.Working(p[0]);GpsTestFixture.Working(p[2]);
   string one=Path.Combine(root,"create-ifd",Path.GetFileNameWithoutExtension(p[0])),jpg=Path.Combine(one,"jpg");Directory.CreateDirectory(jpg);
   string t=Path.Combine(jpg,Path.GetFileName(p[0])),s=Path.Combine(jpg,"SOURCE.JPG");File.Copy(p[0],t,false);File.Copy(p[2],s,false);
   var create=new GpsEditPlan(one,s,t,adapter);var b=create.Targets[0].Before;
   if(b.Jpeg.Dirs.ContainsKey("GPS")||b.Jpeg.Dirs["IFD0"].Entries.Any(e=>e.Id==0x8825))throw new Exception("CreateGpsIfd input already has GPS IFD");
   var r=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(create,create.Targets[0],false);if(!r.Success)throw new Exception(r.Message);
   var a=adapter.Read(t);ExifToolAdapter.Verify(b,a,create.Source);
   if(a.Jpeg.Relocation==null||a.Jpeg.Relocation.UnexplainedBytes!=0||a.Status!="A")throw new Exception("CreateGpsIfd proof missing");
   Console.WriteLine("PASS fresh CreateGpsIfd integration: "+Path.GetFileName(t));
  }
  if(rh!=GpsFileSafety.Hash(raw)||Directory.GetFiles(root,"*.xmp",SearchOption.AllDirectories).Length>0)throw new Exception("Final RAW/XMP boundary");
  Console.WriteLine("PASS real JPG new/overwrite, full integrity, RAW unchanged, precommit failure preserved; "+root);return 0;
 } catch(Exception e){Console.Error.WriteLine(e);return 1;} }
}
