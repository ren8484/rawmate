using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.Web.Script.Serialization;
// Deliberately basic read-only sanity, not a historical forensic comparison.
internal static class ReleaseArwSanityTests {
 [STAThread]static int Main(string[] a){try{
  string root=GpsTestFixture.Working(a[1]);Directory.CreateDirectory(root);var rows=new List<object>();
  using(var tool=new ApprovedExifToolRunner(a[0]))foreach(string name in new[]{"DSC01407.ARW","DSC01410.ARW","DSC01430.ARW","DSC01432.ARW","DSC01435.ARW"}){
   string p=Path.Combine("F:\\希腊旅游\\11560924\\arw",name);GpsFileSafety.CheckPath(p);
   using(var guard=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read)){
    var f=new FileInfo(p);if(f.Length<=0)throw new IOException("Empty RAW");string sha=GpsFileSafety.Hash(p).Replace("-","");
    var result=tool.Run(new[]{"-X","-G1","-n","-Model","-Make","-Software","-ImageWidth","-ImageHeight","-BitsPerSample","-Compression","--",p});
    if(result.ExitCode!=0||!String.IsNullOrWhiteSpace(result.Error))throw new IOException(result.Error);
    var doc=new XmlDocument();doc.LoadXml(Encoding.UTF8.GetString(result.Output));var model=doc.SelectSingleNode("//*[local-name()='Model']");var make=doc.SelectSingleNode("//*[local-name()='Make']");
    if(model==null||model.InnerText!="ILCE-6700"||make==null||!make.InnerText.ToUpperInvariant().Contains("SONY")||doc.SelectSingleNode("//*[local-name()='Error']")!=null)throw new IOException("Basic Sony metadata unavailable");
    File.WriteAllBytes(Path.Combine(root,name+".read.xml"),result.Output);
    if(GpsFileSafety.Hash(p).Replace("-","")!=sha)throw new IOException("RAW changed during read");
    rows.Add(new{path=p,size=f.Length,sha256=sha,model=model.InnerText,make=make.InnerText,readSuccess=true,writePerformed=false});Console.WriteLine("PASS read-only RAW sanity: "+name);
   }
  }
  File.WriteAllText(Path.Combine(root,"arw-sanity.json"),new JavaScriptSerializer().Serialize(rows),new UTF8Encoding(false));return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
