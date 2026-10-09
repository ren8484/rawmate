using System;
using System.IO;
using System.Text;
using System.Linq;
internal static class JpgGpsEvidenceTests {
 static void Rejected(Action action,string label,StringBuilder report){bool rejected=false;try{action();}catch{rejected=true;}if(!rejected)throw new Exception("Gate bypass: "+label);report.AppendLine("PASS negative gate: "+label);}
 [STAThread] static int Main(string[] args){try{
 var adapter=new ExifToolAdapter(args[0]);var report=new StringBuilder("ExifTool "+adapter.Version+"; current production JPG gates; READ ONLY\n");
 string baseline=Path.Combine(GpsTestFixture.Baseline(args[3]),"jpg"),working=GpsTestFixture.Working(args[1]);
 var source=adapter.Read(Path.Combine(baseline,"DSC01265.JPG")).Gps;
 foreach(string name in new[]{"DSC01267.JPG","DSC01270.JPG"}){
 var before=adapter.Read(Path.Combine(baseline,name));var after=adapter.Read(Path.Combine(working,name));ExifToolAdapter.Verify(before,after,source);
 report.AppendLine("PASS "+name+"; GPS before="+(before.Gps==null?"absent":before.Gps.Display)+"; after="+after.Gps.Display+"; GPSStatus="+after.Status+"; Validate 0/0/0; ImageDataHash="+after.ImageHash+"; "+after.Jpeg.Decode);
 var proved=JpgGpsIntegrity.Verify(before.Jpeg,after.Jpeg);report.AppendLine("PROVED: "+String.Join(", ",proved));
 foreach(string key in before.Jpeg.Blocks.Keys){var b=before.Jpeg.Blocks[key];var a=after.Jpeg.Blocks[key];report.AppendLine("BLOCK "+key+"; before="+b.Offset+"+"+b.Length+"; after="+a.Offset+"+"+a.Length+"; beforeSHA="+b.Hash+"; afterSHA="+a.Hash);}
 foreach(string key in before.OtherMetadata.Keys.Union(after.OtherMetadata.Keys)){string b,a;before.OtherMetadata.TryGetValue(key,out b);after.OtherMetadata.TryGetValue(key,out a);if(b!=a)report.AppendLine("SEMANTIC "+key+"; "+b+" -> "+a+"; dynamicProof="+proved.Contains(key));}
 foreach(var p in before.BinaryHashes)report.AppendLine("EXTRACTED "+p.Key+"; before="+p.Value+"; after="+after.BinaryHashes[p.Key]);
 if(after.Jpeg.Hidden!=null){report.AppendLine("HIDDEN pointerDelta="+(after.Jpeg.Hidden.Stored-before.Jpeg.Hidden.Stored)+"; actualDelta="+(after.Jpeg.Hidden.Actual-before.Jpeg.Hidden.Actual)+"; payload="+after.Jpeg.Hidden.Length+"; "+after.Jpeg.Hidden.Hash);long saved=after.Jpeg.Hidden.Stored;after.Jpeg.Hidden.Stored++;Rejected(delegate{JpgGpsIntegrity.Verify(before.Jpeg,after.Jpeg);},"HiddenData pointer mismatch",report);after.Jpeg.Hidden.Stored=saved;}
 if(after.Jpeg.MakerRaw.Length>0){byte old=after.Jpeg.MakerRaw[0];after.Jpeg.MakerRaw[0]^=1;Rejected(delegate{JpgGpsIntegrity.Verify(before.Jpeg,after.Jpeg);},"extra MakerNotes byte",report);after.Jpeg.MakerRaw[0]=old;}
 if(after.Jpeg.MPImage!=null){long saved=after.Jpeg.MPImage.Length;after.Jpeg.MPImage.Length++;Rejected(delegate{JpgGpsIntegrity.Verify(before.Jpeg,after.Jpeg);},"MPImage2 length changed",report);after.Jpeg.MPImage.Length=saved;}
 }
 File.WriteAllText(args[2],report.ToString(),new UTF8Encoding(false));Console.WriteLine("PASS UI JPG post-verification + semantic/layout/payload evidence + private negative gates: "+args[2]);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
