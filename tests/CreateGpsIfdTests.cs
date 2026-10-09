using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;
internal static class CreateGpsIfdTests {
 static readonly BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
 static int checks;static void Check(bool v,string why){checks++;if(!v)throw new Exception(why);}
 static void U32(byte[] b,long p,long v){Array.Copy(BitConverter.GetBytes(checked((uint)v)),0,b,checked((int)p),4);}
 static void U16(byte[] b,long p,int v){Array.Copy(BitConverter.GetBytes(checked((ushort)v)),0,b,checked((int)p),2);}
 sealed class Mutation {internal string Name;internal string[] Categories;internal Action<byte[]> Edit;internal Mutation(string name,string categories,Action<byte[]> edit){Name=name;Categories=categories.Split('|');Edit=edit;}}
 static JpgGpsIntegrity.Snapshot Structure(byte[] bytes) {
  var s=new JpgGpsIntegrity.Snapshot();
  try{typeof(JpgGpsIntegrity).GetMethod("Jpeg",flags).Invoke(null,new object[]{s,bytes});typeof(JpgGpsIntegrity).GetMethod("JpgStructure",flags).Invoke(null,new object[]{s,bytes});}
  catch(TargetInvocationException e){
   var io=e.InnerException as IOException;if(io==null)throw;
   // Accept ONLY these named production capture gates, never any exception.
   string c=io.Message.StartsWith("Unknown TIFF type")||io.Message.StartsWith("TIFF data range invalid")?"CAPTURE_TIFF":
    io.Message.StartsWith("Sony:HiddenDataOffset does not point exactly")?"HIDDEN_REFERENCE":
    io.Message.StartsWith("HiddenInfo is not two LONGs")?"HIDDEN_SCHEMA":null;
   if(c==null)throw;throw new JpgGpsIntegrity.RelocationFailure(c,io.Message);
  }
  return s;
 }
 static List<Mutation> Mutations(JpgGpsIntegrity.Snapshot a,byte[] bytes) {
  var d=a.Dirs["SonyMaker"];var ex=d.Entries.Where(e=>e.Bytes>4&&e.Id!=0x2044).ToArray();var first=ex[0];var second=ex[1];var big=ex.OrderByDescending(e=>e.Bytes).First();var h=d.Get(0x2044);
  Action<byte[],long> flip=(x,p)=>x[checked((int)p)]^=1;long tableEnd=d.Position+2+12L*d.Entries.Count;
  var inline=d.Entries.First(e=>e.Bytes==2);var root=a.Dirs["IFD0"];var ei=a.Dirs["ExifIFD"];
  var m=new List<Mutation>{
   new Mutation("01_PRIVATE_PAYLOAD_BYTE","PRIVATE_PAYLOAD",x=>flip(x,big.Position+31)),
   new Mutation("02_OFFSET_DELTA_OFF_BY_ONE","POOL_LAYOUT",x=>U32(x,first.EntryPosition+8,first.Field+1)),
   new Mutation("03_LEGAL_RANGE_WRONG_PAYLOAD","POOL_LAYOUT",x=>U32(x,first.EntryPosition+8,big.Position-d.Base)),
   new Mutation("04_TYPE_CHANGED","SONY_SCHEMA",x=>U16(x,first.EntryPosition+2,first.Type==7?2:7)),
   new Mutation("05_COUNT_CHANGED","SONY_SCHEMA",x=>U32(x,first.EntryPosition+4,first.Count-1)),
   new Mutation("06_ENTRY_REORDER","SONY_SCHEMA",x=>{long p=d.Position+2;byte[] q=x.Skip((int)p).Take(24).ToArray();Array.Copy(q,12,x,p,12);Array.Copy(q,0,x,p+12,12);}),
   new Mutation("07_NEW_SONY_TAG","SONY_SCHEMA|CAPTURE_TIFF",x=>{U16(x,d.Position,d.Entries.Count+1);U16(x,tableEnd,0xeeee);U16(x,tableEnd+2,3);U32(x,tableEnd+4,1);U32(x,tableEnd+8,7);}),
   new Mutation("08_SONY_TAG_DELETED","SONY_SCHEMA",x=>{U16(x,d.Position,d.Entries.Count-1);Array.Copy(x,d.Position+14,x,d.Position+2,12L*(d.Entries.Count-1));}),
   new Mutation("09_EXTRA_POOL_GAP","POOL_LAYOUT",x=>{long p=first.Position+first.Bytes,end=a.MakerOffset+a.MakerRaw.Length;Array.Copy(x,p,x,p+1,end-p-1);x[(int)p]=0;foreach(var e in ex.Concat(new[]{h}))if(e.Position>=p)U32(x,e.EntryPosition+8,e.Field+1);}),
   new Mutation("10_POOL_OVERLAP","POOL_LAYOUT",x=>U32(x,second.EntryPosition+8,first.Field+2)),
   new Mutation("11_HIDDENINFO_LENGTH_LONG_CHANGED","HIDDEN_PAYLOAD",x=>U32(x,h.Position+4,a.Hidden.Length+1)),
   new Mutation("12_HIDDENDATA_BYTE","HIDDEN_PAYLOAD",x=>flip(x,a.Hidden.Actual+36)),
   new Mutation("13_HIDDEN_OFFSET_CORRECT_PAYLOAD_HASH_CHANGED","HIDDEN_PAYLOAD",x=>flip(x,a.Hidden.Actual+64)),
   new Mutation("14_EXTRA_MAKER_INLINE_PADDING_BYTE","MAKER_BYTES",x=>flip(x,inline.EntryPosition+10)),
   new Mutation("15_UNAUTHORIZED_IFD0_SCHEMA","PUBLIC_SCHEMA",x=>U16(x,root.Get(0x0110).EntryPosition,0xeeef)),
   new Mutation("16_UNAUTHORIZED_EXIFIFD_SCHEMA","PUBLIC_SCHEMA",x=>U16(x,ei.Get(0x829a).EntryPosition,0xeeed)),
   new Mutation("17_PUBLIC_INLINE_PADDING_BYTE","PUBLIC_PADDING",x=>flip(x,root.Entries.First(e=>e.Bytes==2).EntryPosition+10)),
   new Mutation("18_GPS_NEXT_REFERENCE","GPS_REFERENCE",x=>U32(x,a.Dirs["GPS"].Position+2+12L*a.Dirs["GPS"].Entries.Count,a.Dirs["IFD1"].Position-a.ExifBase)),
   new Mutation("19_UNKNOWN_IFD_POINTER_TYPE","DIRECTORY_REFERENCE",x=>U16(x,root.Get(0x8769).EntryPosition+2,13)),
   new Mutation("20_EXIF_ALIGNMENT_PADDING_BYTE","EXIF_PADDING",x=>flip(x,root.Get(0x010f).Position+root.Get(0x010f).Bytes)),
   new Mutation("21_UNAUTHORIZED_NEW_GPS_TAG","GPS_SCHEMA",x=>U16(x,a.Dirs["GPS"].Get(9).EntryPosition,11)),
   new Mutation("22_HIDDEN_POINTER_OFF_BY_ONE","HIDDEN_REFERENCE",x=>U32(x,h.Position,a.Hidden.Stored+1)),
   new Mutation("23_SONY_HEADER_BYTE","SONY_HEADER",x=>flip(x,a.MakerOffset+9))};
  foreach(var e in ex){var entry=e;m.Add(new Mutation("PRIVATE_PAYLOAD_"+e.Key,"PRIVATE_PAYLOAD",x=>flip(x,entry.Position)));}
  foreach(var e in ex){var entry=e;m.Add(new Mutation("OFFSET_"+e.Key,"POOL_LAYOUT",x=>U32(x,entry.EntryPosition+8,entry.Field+1)));}
  return m;
 }
 static void ReadOnly(string pairs,string tool,string output,bool mutations=true) {
  string text=File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(pairs)),"..","RAWMate.cs")).Replace("\r\n","\n");
  int start=text.IndexOf("        bool rawSame=b.MakerRaw.SequenceEqual(a.MakerRaw);");string existing=text.Substring(start,text.IndexOf("\n    }",start)-start).Trim();
  using(var h=System.Security.Cryptography.SHA256.Create())Check(BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(existing))).Replace("-","")=="CEA1E21399E5C6684EFDBBD6A61ACD6F97F1D74DF64F883AD881CEB5E711FB25","ExistingGpsIfdPath frozen approved body");
  var rows=new List<object>();int negative=0;var negativeRows=new List<object>();
  using(var adapter=new ExifToolAdapter(tool))foreach(string line in File.ReadAllLines(pairs)) {
   string[] p=line.Split('|');var b=adapter.Read(p[0]);var a=adapter.Read(p[1]);var gps=adapter.ReadBaseline(p[2]).Gps;
   var sw=Stopwatch.StartNew();ExifToolAdapter.Verify(b,a,gps);sw.Stop();var proof=a.Jpeg.Relocation;
   Check(proof!=null&&proof.Nested==1&&proof.UnexplainedBytes==0,"new path missing byte closure");
   rows.Add(new{file=Path.GetFileName(p[0]),production="PASS",proof,verifyMs=sw.Elapsed.TotalMilliseconds,beforeSHA=GpsFileSafety.Hash(p[0]),afterSHA=GpsFileSafety.Hash(p[1]),beforeDecode=b.Jpeg.Decode,afterDecode=a.Jpeg.Decode,
    beforeLength=new FileInfo(p[0]).Length,afterLength=new FileInfo(p[1]).Length,beforeGps=b.Gps==null?null:b.Gps.Display,afterGps=a.Gps.Display,afterStatus=a.Status,
    beforeImageDataHash=b.ImageHash,afterImageDataHash=a.ImageHash,strictValidate="PASS: both full reads rejected any Warning/Error or non-OK Validate",
    // Evidence only: actual disk spans, not rebased ExifTool MakerNotes exports.
    blocks=b.Jpeg.Blocks.Keys.Select(k=>new{key=k,beforeOffset=b.Jpeg.Blocks[k].Offset,afterOffset=a.Jpeg.Blocks[k].Offset,beforeLength=b.Jpeg.Blocks[k].Length,afterLength=a.Jpeg.Blocks[k].Length,beforeHash=b.Jpeg.Blocks[k].Hash,afterHash=a.Jpeg.Blocks[k].Hash}).ToArray(),
    semanticDiff=b.OtherMetadata.Keys.Union(a.OtherMetadata.Keys).Where(k=>!b.OtherMetadata.ContainsKey(k)||!a.OtherMetadata.ContainsKey(k)||b.OtherMetadata[k]!=a.OtherMetadata[k]).Select(k=>new{key=k,before=b.OtherMetadata.ContainsKey(k)?b.OtherMetadata[k]:null,after=a.OtherMetadata.ContainsKey(k)?a.OtherMetadata[k]:null}).ToArray(),
    extractedBefore=b.BinaryHashes,extractedAfter=a.BinaryHashes});
   if(mutations) {
   byte[] before=File.ReadAllBytes(p[0]),after=File.ReadAllBytes(p[1]);var bs=Structure(before);
   foreach(var mutation in Mutations(a.Jpeg,after)) {
    byte[] edited=(byte[])after.Clone();mutation.Edit(edited);Check(!after.SequenceEqual(edited),"empty mutation "+mutation.Name);
    string category=null,reason=null;
    try{JpgGpsIntegrity.Verify(bs,Structure(edited),gps);}
    catch(JpgGpsIntegrity.RelocationFailure error){category=error.Category;reason=error.Message;}
    Check(category!=null&&mutation.Categories.Contains(category),"unexpected/missing gate "+Path.GetFileName(p[0])+" "+mutation.Name+": "+category+" "+reason);
    negative++;negativeRows.Add(new{file=Path.GetFileName(p[0]),test=mutation.Name,expected=mutation.Categories,actual=category,reason});
   }
   // Both-sided controls retain every structural guard without ID/model allowlists.
   byte[] x=(byte[])before.Clone(),y=(byte[])after.Clone();var entry=bs.Dirs["SonyMaker"].Entries.First(e=>e.Bytes>4&&e.Id!=0x2044);U16(x,entry.EntryPosition,0xe123);U16(y,a.Jpeg.Dirs["SonyMaker"].Get(entry.Id).EntryPosition,0xe123);
   Check(JpgGpsIntegrity.Verify(Structure(x),Structure(y),gps).Contains("BLOCK:Raw/MakerNotes"),"ordinary ID whitelist");
   x=(byte[])before.Clone();y=(byte[])after.Clone();x[(int)bs.Dirs["IFD0"].Get(0x0110).Position]=(byte)'X';y[(int)a.Jpeg.Dirs["IFD0"].Get(0x0110).Position]=(byte)'X';
   Check(JpgGpsIntegrity.Verify(Structure(x),Structure(y),gps).Contains("NEW_GPS_IFD"),"camera model whitelist");
   }
   Console.WriteLine("PASS production pair "+(mutations?"+ categorized negative gates: ":"full independent evidence: ")+Path.GetFileName(p[0]));
  }
  var json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024};File.WriteAllText(output,json.Serialize(new{checks,negative,rows,negativeRows}),new UTF8Encoding(false));Console.WriteLine("PASS "+negative+" categorized negative-equivalent tests; "+checks+" assertions");
 }
 static void Transactions(string pairs,string tool,string output) {
  string root=Path.GetFullPath(output);Check(root.StartsWith("C:\\rawmate\\test-output\\",StringComparison.OrdinalIgnoreCase),"writes outside test-output");Check(!Directory.Exists(root),"fresh working copies required");Directory.CreateDirectory(root);
  var rows=new List<object>();string[] lines=File.ReadAllLines(pairs);var required=new[]{"DSC02571.JPG","DSC02574.JPG","DSC02576.JPG","DSC02581.JPG","DSC02543.JPG"};
  using(var adapter=new ExifToolAdapter(tool)) {
   foreach(string name in required) {
    string[] p=lines.First(x=>Path.GetFileName(x.Split('|')[0])==name).Split('|');string one=Path.Combine(root,Path.GetFileNameWithoutExtension(name)),jpg=Path.Combine(one,"jpg"),arw=Path.Combine(one,"arw");Directory.CreateDirectory(jpg);Directory.CreateDirectory(arw);
    string source=Path.Combine(jpg,Path.GetFileName(p[2])),target=Path.Combine(jpg,name);File.Copy(p[0],target,false);File.Copy(p[2],source,false);
    string rawSource=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(p[0])),"arw",Path.GetFileNameWithoutExtension(name)+".ARW"),raw=Path.Combine(arw,Path.GetFileName(rawSource));File.Copy(rawSource,raw,false);string rawHash=GpsFileSafety.Hash(raw);
    var watch=Stopwatch.StartNew();var plan=new GpsEditPlan(one,source,target,adapter);var before=plan.Targets[0].Before;
    var result=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(plan,plan.Targets[0],false);watch.Stop();Check(result.Success,result.Message);
    var after=adapter.Read(target);ExifToolAdapter.Verify(before,after,plan.Source);Check(after.Status=="A"&&plan.Source.SameCoordinates(after.Gps),"GPS plan mismatch");
    Check(GpsFileSafety.Hash(raw)==rawHash,"ARW changed");Check(Directory.GetFiles(one,"*.xmp",SearchOption.AllDirectories).Length==0,"XMP created");Check(Directory.GetFiles(jpg).Length==2,"normal transaction temp/journal/backup not cleaned");
    rows.Add(new{file=name,root=one,totalMs=watch.Elapsed.TotalMilliseconds,result.Message,result.Diagnostics,proof=after.Jpeg.Relocation,decode=after.Jpeg.Decode,rawSHA=rawHash,finalSHA=GpsFileSafety.Hash(target)});
    Console.WriteLine("PASS real new-copy transaction "+name+" ms="+watch.ElapsedMilliseconds);
   }
   string batch=Path.Combine(root,"grid-batch"),bj=Path.Combine(batch,"jpg");Directory.CreateDirectory(bj);Directory.CreateDirectory(Path.Combine(batch,"arw"));string src=Path.Combine(bj,"DSC02564.JPG");File.Copy(lines[0].Split('|')[2],src,false);
   var targets=new List<string>();foreach(string line in lines){string p=line.Split('|')[0],to=Path.Combine(bj,Path.GetFileName(p));File.Copy(p,to,false);targets.Add(to);}
   var batchPlan=new GpsBatchPlan(batch,src,targets.Concat(new[]{src}),adapter);var timer=Stopwatch.StartNew();
   var resultBatch=GpsBatchExecutor.Execute(batchPlan,false,null,null,e=>new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(e.Plan,e.Plan.Targets[0],false));timer.Stop();
   Check(resultBatch.Success.Count==9&&resultBatch.Failed.Count==0&&resultBatch.Skipped.Count==1,"real grid executor summary");
   foreach(var e in batchPlan.Entries.Where(e=>e.Plan!=null)){var after=adapter.Read(e.Path);ExifToolAdapter.Verify(e.Plan.Targets[0].Before,after,batchPlan.Source);}
   Check(Directory.GetFiles(bj).Length==10&&Directory.GetFiles(batch,"*.xmp",SearchOption.AllDirectories).Length==0,"batch cleanup/XMP");
   rows.Add(new{kind="grid executor real copies",root=batch,success=9,skipped=1,failed=0,totalMs=timer.Elapsed.TotalMilliseconds,diagnostics=resultBatch.Success.Select(x=>x.Diagnostics).ToArray()});
  }
  // Corrupt only a fresh transaction's GUID temp after the real tool writes it.
  // The final target must remain exact before bytes and the next item must finish.
  string failureRoot=Path.Combine(root,"grid-private-injection"),fj=Path.Combine(failureRoot,"jpg");Directory.CreateDirectory(fj);Directory.CreateDirectory(Path.Combine(failureRoot,"arw"));
  string fs=Path.Combine(fj,"SOURCE.JPG"),bad=Path.Combine(fj,"NEG.JPG"),good=Path.Combine(fj,"GOOD.JPG");File.Copy(lines[0].Split('|')[2],fs,false);File.Copy(lines[0].Split('|')[0],bad,false);File.Copy(lines[0].Split('|')[0],good,false);string originalBad=GpsFileSafety.Hash(bad);
  using(var approved=new ApprovedExifToolRunner(tool))using(var adapter=new ExifToolAdapter(new PrivateInjection(approved))) {
   var plan=new GpsBatchPlan(failureRoot,fs,new[]{bad,good},adapter);var injection=(PrivateInjection)typeof(ExifToolAdapter).GetField("runner",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(adapter);
   var result=GpsBatchExecutor.Execute(plan,false,null,null,e=>{injection.Inject=e.Path==bad;return new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(e.Plan,e.Plan.Targets[0],false);});
   Check(result.Success.Count==1&&result.Failed.Count==1&&result.Skipped.Count==0,"private injection batch summary");
   Check(result.Failed[0].Message.Contains("CreateGpsIfd/PRIVATE_PAYLOAD"),"private injection did not fail at expected production gate: "+result.Failed[0].Message);
   Check(GpsFileSafety.Hash(bad)==originalBad&&File.Exists(result.Failed[0].RecoveryPath),"failed original/recovery preservation");
   var after=adapter.Read(good);ExifToolAdapter.Verify(plan.Entries[1].Plan.Targets[0].Before,after,plan.Source);
   rows.Add(new{kind="private mutation real batch",success=1,failed=1,originalUnchanged=true,recovery=result.Failed[0].RecoveryPath,reason=result.Failed[0].Message});
  }
  File.WriteAllText(Path.Combine(root,"transactions.json"),new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(rows),new UTF8Encoding(false));
  Console.WriteLine("PASS new working-copy transactions + real grid batch executor");
 }
 sealed class PrivateInjection:IGpsToolRunner {
  readonly IGpsToolRunner inner;internal bool Inject;internal PrivateInjection(IGpsToolRunner runner){inner=runner;}
  public GpsToolResult Run(IList<string> args) {
   var result=inner.Run(args);
   if(Inject&&args[0]=="-overwrite_original"&&result.ExitCode==0) {
    string path=args.Last();Check(path.StartsWith("C:\\rawmate\\test-output\\",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(path).StartsWith(".rawmate-gps-"),"injection outside GUID working temp");
    byte[] bytes=File.ReadAllBytes(path);var s=Structure(bytes);var e=s.Dirs["SonyMaker"].Entries.Where(x=>x.Bytes>4&&x.Id!=0x2044).OrderByDescending(x=>x.Bytes).First();bytes[(int)e.Position+31]^=1;File.WriteAllBytes(path,bytes);
   }
   return result;
  }
 }
 [STAThread] static int Main(string[] args){try{if(args[0]=="readonly")ReadOnly(args[1],args[2],args[3]);else if(args[0]=="evidence")ReadOnly(args[1],args[2],args[3],false);else if(args[0]=="transactions")Transactions(args[1],args[2],args[3]);else throw new ArgumentException("mode");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
