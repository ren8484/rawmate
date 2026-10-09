using System;
using System.IO;
using System.Text;
internal static class ReleaseUiEvidence {
 [STAThread]static int Main(string[] a){try{
  string root=GpsTestFixture.Working(a[0]),baseline=GpsTestFixture.Baseline(a[1]),mode=a[3];
  if(mode=="verify")using(var adapter=new ExifToolAdapter(a[2])){
   var source=adapter.Read(Path.Combine(baseline,"jpg","DSC01265.JPG")).Gps;
   foreach(string name in new[]{"DSC01270.JPG","DSC01652.JPG","DSC01656.JPG"}){
    string b=name=="DSC01270.JPG"?Path.Combine(baseline,"jpg",name):Path.Combine(Path.GetDirectoryName(baseline),"create","before",name);
    ExifToolAdapter.Verify(adapter.Read(b),adapter.Read(Path.Combine(root,"jpg",name)),source);Console.WriteLine("PASS actual UI write full integrity: "+name);
   }
   foreach(string name in new[]{"DSC01265.JPG","DSC01267.JPG"})if(GpsFileSafety.Hash(Path.Combine(root,"jpg",name))!=GpsFileSafety.Hash(Path.Combine(baseline,"jpg",name)))throw new IOException("Default skip changed "+name);
   if(GpsFileSafety.Hash(Path.Combine(root,"arw","DSC01270.ARW"))!=GpsFileSafety.Hash(Path.Combine(baseline,"arw","DSC01270.ARW")))throw new IOException("RAW changed");
   if(Directory.GetFiles(root,"*.xmp",SearchOption.AllDirectories).Length!=0)throw new IOException("XMP created");
   if(Directory.GetFiles(Path.Combine(root,"jpg"),".rawmate-gps-*").Length!=0)throw new IOException("Unexpected transaction residue");
   Console.WriteLine("PASS UI skip/source/RAW/XMP boundaries");
  }else{
   // Intentional EXTERNAL metadata edits, confined to fresh UI copies.
   string target=Path.Combine(root,"jpg",a[4]);DateTime stamp=File.GetLastWriteTimeUtc(target);
   using(var tool=new ApprovedExifToolRunner(a[2])){
    string[] tags=mode=="source-change"?new[]{"-GPSLatitude=38.123456","-GPSLatitudeRef=N","-GPSLongitude=24.654321","-GPSLongitudeRef=E","-GPSStatus#=A"}:new[]{"-GPSLatitude=","-GPSLatitudeRef=","-GPSLongitude=","-GPSLongitudeRef=","-GPSStatus#=V"};
    var args=new System.Collections.Generic.List<string>{"-overwrite_original"};args.AddRange(tags);args.Add("--");args.Add(target);var r=tool.Run(args);if(r.ExitCode!=0||!String.IsNullOrWhiteSpace(r.Error))throw new IOException(r.Error);
   }
   File.SetLastWriteTimeUtc(target,stamp);Console.WriteLine("EXTERNAL working-copy edit complete, mtime preserved: "+mode);
  }
  return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
