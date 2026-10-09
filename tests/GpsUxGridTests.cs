using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
internal static class GpsUxGridTests {
 static int count;static string root;static BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
 static object Field(object obj,string name){return obj.GetType().GetField(name,fields).GetValue(obj);}
 static void Set(object obj,string name,object value){obj.GetType().GetField(name,fields).SetValue(obj,value);}
 static object Call(object obj,string name,params object[] args){return obj.GetType().GetMethod(name,fields).Invoke(obj,args);}
 static void Reject(Action action,string why){bool failed=false;try{action();}catch{failed=true;}Check(failed,why);}
 static readonly GpsMetadata gps=new GpsMetadata(37.969397,23.740217,null,null,"source");
 static string Make(string name,string folder="jpg") {return (string)typeof(GpsModelTests).GetMethod("MakeFile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root,folder,name});}
 static IGpsToolRunner Fake(string source,out object fake) {fake=Activator.CreateInstance(typeof(GpsModelTests).GetNestedType("FakeRunner",BindingFlags.NonPublic),true);((Dictionary<string,GpsMetadata>)Field(fake,"Gps"))[source]=gps;return (IGpsToolRunner)fake;}
 static MainWindow CreateBaseState() {
  var state=(MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
  foreach(string n in new[]{"gpsDisplayCache","gpsBadges","gpsDisplayPending","selectedFiles","galleryFiles","tileFiles","imagePathEpochs","thumbnailStamps","pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles"}) {var f=typeof(MainWindow).GetField(n,fields);f.SetValue(state,Activator.CreateInstance(f.FieldType));}
  foreach(string n in new[]{"photoInfo","gpsInfo","gpsSourceText","gpsSingleCurrentSource","gpsSingleSourceCoordinates"})Set(state,n,new TextBlock());
  foreach(string n in new[]{"gpsSetButton","gpsApplyButton","gpsClearButton","gpsGridSetButton","gpsGridApplyButton"})Set(state,n,new Button());
  Set(state,"gpsSourceBar",new StackPanel());Set(state,"gpsGridActions",new Grid());Set(state,"gpsSingleSourceDivider",new System.Windows.Controls.Border());Set(state,"gpsSession",new GpsSourceSession());
  return state;
 }
 static MainWindow CreateSinglePhotoFixture() {
  var state=CreateBaseState();Set(state,"singleViewMode",true);
  Call(state,"BuildPhotoInfoCard");return state;
 }
 static MainWindow CreateGridFixture() {
  var state=CreateBaseState();Set(state,"singleViewMode",false);
  Call(state,"BuildPhotoInfoCard");return state;
 }
 static void CacheGps(MainWindow state,string path,GpsMetadata value) {
  var display=Activator.CreateInstance(typeof(MainWindow).GetNestedType("GpsDisplay",BindingFlags.NonPublic),true);
  Set(display,"Gps",value);
  Set(display,"Stamp",typeof(MainWindow).GetMethod("GpsDisplayStamp",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{path}));
  ((System.Collections.IDictionary)Field(state,"gpsDisplayCache"))[path]=display;
 }
 static void IndependentLayouts(MainWindow business,ExifToolAdapter adapter,string source,string b,string c) {
  // Each real production footer is built once, with entirely fresh controls.
  // State changes use UpdateGpsLine, never reparenting or repeated construction.
  var single=CreateSinglePhotoFixture();var singleSession=(GpsSourceSession)Field(single,"gpsSession");
  singleSession.SelectRoot(root);singleSession.SetSource(source,adapter);
  Set(single,"currentSingleFile",source);((TextBlock)Field(single,"gpsInfo")).Tag=gps;Call(single,"UpdateGpsLine");
  Check(((TextBlock)Field(single,"gpsSingleCurrentSource")).Text=="当前来源"&&((TextBlock)Field(single,"gpsSingleCurrentSource")).Visibility==Visibility.Visible&&((Button)Field(single,"gpsSetButton")).Visibility==Visibility.Collapsed&&((Button)Field(single,"gpsApplyButton")).Visibility==Visibility.Collapsed,"independent single source status with no set/apply");
  Check(((Button)Field(single,"gpsClearButton")).Visibility==Visibility.Visible&&((Button)Field(single,"gpsClearButton")).IsEnabled,"source itself only clear action available");
  var singleActions=(Grid)((Button)Field(single,"gpsClearButton")).Parent;
  Check(singleActions.Children.Cast<UIElement>().SequenceEqual(new UIElement[]{(Button)Field(single,"gpsClearButton"),(Button)Field(single,"gpsSetButton"),(Button)Field(single,"gpsApplyButton")})&&Grid.GetColumn((Button)Field(single,"gpsClearButton"))==0&&Grid.GetColumn((Button)Field(single,"gpsSetButton"))==1&&Grid.GetColumn((Button)Field(single,"gpsApplyButton"))==2,"single action order clear / set / apply");
  Set(single,"currentSingleFile",b);((TextBlock)Field(single,"gpsInfo")).Tag=null;Call(single,"UpdateGpsLine");
  Check(((TextBlock)Field(single,"gpsInfo")).Text=="GPS  —"&&((Button)Field(single,"gpsSetButton")).Visibility==Visibility.Visible&&!((Button)Field(single,"gpsSetButton")).IsEnabled&&((Button)Field(single,"gpsApplyButton")).Visibility==Visibility.Visible&&((Button)Field(single,"gpsApplyButton")).IsEnabled,"missing GPS displayed and source can apply");
  ((TextBlock)Field(single,"gpsInfo")).Tag=gps;Call(single,"UpdateGpsLine");
  Check(((Button)Field(single,"gpsSetButton")).IsEnabled&&((Button)Field(single,"gpsApplyButton")).Content.ToString()=="应用来源"&&((StackPanel)Field(single,"gpsSourceBar")).Visibility==Visibility.Visible,"independent single existing GPS and final apply label");
  singleSession.Clear();Call(single,"UpdateGpsLine");Check(((Button)Field(single,"gpsSetButton")).Visibility==Visibility.Visible&&((Button)Field(single,"gpsSetButton")).IsEnabled&&((Button)Field(single,"gpsApplyButton")).Visibility==Visibility.Collapsed,"single without source only set action");
  Check(((StackPanel)Field(single,"gpsSourceBar")).Visibility==Visibility.Collapsed&&((System.Windows.Controls.Border)Field(single,"gpsSingleSourceDivider")).Visibility==Visibility.Collapsed&&((Button)Field(single,"gpsClearButton")).Visibility==Visibility.Collapsed,"single no source collapses source / divider / clear");
  var grid=CreateGridFixture();var gridSession=(GpsSourceSession)Field(grid,"gpsSession");
  gridSession.SelectRoot(root);gridSession.SetSource(source,adapter);
  var selected=(HashSet<string>)Field(grid,"selectedFiles");selected.Add(b);selected.Add(c);
  ((List<string>)Field(grid,"galleryFiles")).AddRange(new[]{source,b,c});Call(grid,"UpdateGpsLine");
  var gridActions=(Grid)Field(grid,"gpsGridActions");
  Check(gridActions.Children.Cast<UIElement>().SequenceEqual(new UIElement[]{(Button)Field(grid,"gpsClearButton"),(Button)Field(grid,"gpsGridSetButton"),(Button)Field(grid,"gpsGridApplyButton")})&&Grid.GetColumn((Button)Field(grid,"gpsClearButton"))==0&&Grid.GetColumn((Button)Field(grid,"gpsGridSetButton"))==1&&Grid.GetColumn((Button)Field(grid,"gpsGridApplyButton"))==2,"grid action order clear / set / apply");
  Check(((Button)Field(grid,"gpsGridSetButton")).Visibility==Visibility.Visible&&!((Button)Field(grid,"gpsGridSetButton")).IsEnabled&&((Button)Field(grid,"gpsGridApplyButton")).Content.ToString()=="应用来源"&&((Button)Field(grid,"gpsGridApplyButton")).IsEnabled,"grid multi-selection keeps set disabled and final apply label");
  Check(((StackPanel)Field(grid,"gpsSourceBar")).Visibility==Visibility.Visible&&((TextBlock)Field(grid,"gpsInfo")).Text=="GPS  已选择 2 张"&&selected.SetEquals(new[]{b,c}),"independent grid source row and selection preserved");
  gridSession.Clear();Call(grid,"UpdateGpsLine");
  Check(((StackPanel)Field(grid,"gpsSourceBar")).Visibility==Visibility.Collapsed&&((System.Windows.Controls.Border)Field(grid,"gpsSingleSourceDivider")).Visibility==Visibility.Collapsed&&((Button)Field(grid,"gpsClearButton")).Visibility==Visibility.Collapsed&&((Button)Field(grid,"gpsGridApplyButton")).Visibility==Visibility.Collapsed,"no source collapses source / divider / clear / apply");
  gridSession.SetSource(source,adapter);selected.Clear();selected.Add(source);CacheGps(grid,source,gps);Call(grid,"UpdateGpsLine");
  Check(((TextBlock)Field(grid,"gpsSingleCurrentSource")).Visibility==Visibility.Visible&&((Button)Field(grid,"gpsGridSetButton")).Visibility==Visibility.Collapsed&&((Button)Field(grid,"gpsGridApplyButton")).Visibility==Visibility.Collapsed&&((Button)Field(grid,"gpsClearButton")).Visibility==Visibility.Visible,"grid selected source only clear action");
  selected.Clear();selected.Add(b);CacheGps(grid,b,null);Call(grid,"UpdateGpsLine");
  Check(((TextBlock)Field(grid,"gpsInfo")).Text=="GPS  —"&&((Button)Field(grid,"gpsGridSetButton")).Visibility==Visibility.Visible&&!((Button)Field(grid,"gpsGridSetButton")).IsEnabled&&((Button)Field(grid,"gpsGridApplyButton")).IsEnabled,"grid missing GPS keeps set disabled / apply enabled");
  Check(new[]{"photoInfo","gpsInfo","gpsSourceBar","gpsGridActions","gpsSourceText","gpsSingleCurrentSource","gpsSingleSourceDivider","gpsSingleSourceCoordinates","gpsClearButton","gpsSetButton","gpsApplyButton","gpsGridSetButton","gpsGridApplyButton"}.All(n=>!Object.ReferenceEquals(Field(business,n),Field(single,n))&&!Object.ReferenceEquals(Field(single,n),Field(grid,n))&&!Object.ReferenceEquals(Field(business,n),Field(grid,n))),"business / single / grid never share FrameworkElement instances");
 }
 static void Run() {
  string source=Make("SOURCE.JPG"),a=Make("A.JPG"),b=Make("B.JPG"),c=Make("C.JPG"),existing=Make("EXISTING.JPG"),readOnly=Make("READONLY.JPG"),raw=Make("PAIR.ARW","arw");
  object fake;var adapter=new ExifToolAdapter(Fake(source,out fake));((Dictionary<string,GpsMetadata>)Field(fake,"Gps"))[existing]=new GpsMetadata(31,121,null,null,existing);File.SetAttributes(readOnly,File.GetAttributes(readOnly)|FileAttributes.ReadOnly);
  var plan=new GpsBatchPlan(root,source,new[]{source,a,b,c,existing,readOnly,raw,Path.Combine(root,"MISSING.JPG")},adapter);
  Check(plan.Entries.Count==8,"all distinct selected entries classified");Check(plan.Count(GpsSelectionKind.Source)==1,"source excluded");Check(plan.Count(GpsSelectionKind.Missing)==3,"three missing GPS JPG");Check(plan.Count(GpsSelectionKind.Existing)==1,"existing GPS classified");Check(plan.Count(GpsSelectionKind.Invalid)==3,"readonly / ARW / outside/missing classified invalid");
  Check(plan.Entries.Where(e=>plan.Includes(e,false)).Count()==3,"default missing only");Check(plan.Entries.Where(e=>plan.Includes(e,true)).Count()==4,"explicit overwrite includes existing only");Check(!plan.Includes(plan.Entries.First(e=>e.Kind==GpsSelectionKind.Source),true),"source skipped even overwrite");Check(plan.Entries.Where(e=>e.Plan!=null).All(e=>e.Plan.Targets.Count==1&&GpsFileSafety.IsJpg(e.Plan.Targets[0].Path)),"every child plan one JPG, no RAW");
  var invoked=new List<string>();var mixed=GpsBatchExecutor.Execute(plan,false,null,null,delegate(GpsSelectionEntry e){invoked.Add(e.Path);return new GpsFileResult{Path=e.Path,Success=e.Path!=b,Message="fake outcome"};});
  Check(mixed.Success.Count==2&&mixed.Failed.Count==4&&mixed.Skipped.Count==2,"partial result precise 2/2/4");Check(invoked.SequenceEqual(new[]{a,b,c}),"single failure continues later target; existing/source never execute");
  Check(mixed.Success.Count+mixed.Failed.Count+mixed.Skipped.Count==8,"each selected counted exactly once");
  bool cancel=false;var cancelPlan=new GpsBatchPlan(root,source,new[]{a,b,c},adapter);string hb=GpsFileSafety.Hash(b),hc=GpsFileSafety.Hash(c);
  Set(fake,"DuringWrite",new Action<string>(delegate{cancel=true;}));
  var cancelled=GpsBatchExecutor.Execute(cancelPlan,false,delegate{return cancel;},null,delegate(GpsSelectionEntry e){return new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(e.Plan,e.Plan.Targets[0],false);});
  Check(cancelled.Success.Count==1&&cancelled.Skipped.Count==2&&cancelled.Failed.Count==0,"cancel during current fake transaction waits safe commit then skips remaining");
  Check(GpsFileSafety.Hash(b)==hb&&GpsFileSafety.Hash(c)==hc,"cancelled not-started files unchanged");Check(!File.Exists(a+".rawmate-gps-transaction")&&!File.Exists(a+".rawmate-gps-backup"),"current transaction complete and verified cleanup");Set(fake,"DuringWrite",null);
  var overwriteResult=GpsBatchExecutor.Execute(plan,true,null,null,delegate(GpsSelectionEntry e){return new GpsFileResult{Path=e.Path,Success=true};});Check(overwriteResult.Success.Count==4&&overwriteResult.Skipped.Count==1,"overwrite authorization only expands eligible existing");
  Check(Directory.GetFiles(root,"*.xmp",SearchOption.AllDirectories).Length==0,"XMP never created");
  foreach(bool busy in new[]{false,true})foreach(bool current in new[]{false,true}) {Check(GpsUxState.CanSetSource(gps,current,busy)==(!busy&&!current),"set source context state");Check(GpsUxState.CanApply(true,current,busy)==(!busy&&!current),"apply context state");}
  Check(!GpsUxState.CanSetSource(null,false,false)&&!GpsUxState.CanApply(false,false,false),"no GPS/no source action disabled");
  Check(GpsUxState.Badge(gps,false)=="GPS ✓"&&GpsUxState.Badge(null,false)=="GPS —"&&GpsUxState.Badge(gps,true)=="GPS 来源","grid three badges");
  Check(GpsUxState.ToolStatus(true,false,source)=="可用"&&GpsUxState.ToolStatus(false,false,null)=="未配置"&&GpsUxState.ToolStatus(false,false,source)=="校验失败","settings three tool states");
  var state=CreateSinglePhotoFixture();var session=(GpsSourceSession)Field(state,"gpsSession");session.SelectRoot(root);session.SetSource(source,adapter);Check(!String.IsNullOrEmpty(session.SourceFileIdentity),"source records Windows file identity + SHA");
  Set(state,"singleViewMode",true);Set(state,"currentSingleFile",source);((TextBlock)Field(state,"gpsInfo")).Tag=gps;Call(state,"UpdateGpsLine");
  Check(((TextBlock)Field(state,"gpsSingleCurrentSource")).Text=="当前来源"&&((TextBlock)Field(state,"gpsSingleCurrentSource")).Visibility==Visibility.Visible&&((Button)Field(state,"gpsSetButton")).Visibility==Visibility.Collapsed&&!((Button)Field(state,"gpsSetButton")).IsEnabled,"source itself has separate status and no executable set action");Check(((Button)Field(state,"gpsApplyButton")).Visibility==Visibility.Collapsed,"source itself no apply");
  Set(state,"currentSingleFile",b);((TextBlock)Field(state,"gpsInfo")).Tag=null;Call(state,"UpdateGpsLine");Check(((Button)Field(state,"gpsSetButton")).Visibility==Visibility.Visible&&!((Button)Field(state,"gpsSetButton")).IsEnabled&&((Button)Field(state,"gpsApplyButton")).Content.ToString()=="应用来源","missing target keeps disabled set and primary apply action");
  ((TextBlock)Field(state,"gpsInfo")).Tag=gps;Call(state,"UpdateGpsLine");Check(((Button)Field(state,"gpsSetButton")).IsEnabled&&((Button)Field(state,"gpsApplyButton")).Content.ToString()=="应用来源","existing target both actions use final labels");
  var selected=(HashSet<string>)Field(state,"selectedFiles");selected.Add(b);selected.Add(c);((List<string>)Field(state,"galleryFiles")).AddRange(new[]{source,b,c});
  Set(state,"singleViewMode",false);Call(state,"UpdateGpsLine");Check(session.Source!=null&&((StackPanel)Field(state,"gpsSourceBar")).Visibility==Visibility.Visible,"single to grid preserves visible source");
  Check(((Button)Field(state,"gpsGridApplyButton")).IsEnabled,"grid multiple JPG primary action");Set(state,"singleViewMode",true);Call(state,"UpdateGpsLine");Check(session.Source!=null,"grid to single preserves source");
  IndependentLayouts(state,adapter,source,b,c);
  var menu=(ContextMenu)Call(state,"BuildTileMenu",b);var names=menu.Items.OfType<MenuItem>().Select(i=>i.Header.ToString()).ToArray();Check(!names.Any(n=>n.Contains("ExifTool")),"no tool configuration in photo menu");Check(names.Take(4).SequenceEqual(new[]{"标记为废片","保留双格式","保留 JPG","保留 RAW"}),"P/J/R/X menu untouched");Check(names.Skip(4).SequenceEqual(new[]{"设为 GPS 来源","应用 GPS 到当前照片"}),"secondary GPS entries only");
  Set(state,"gpsSettingsStatus",new TextBlock());Set(state,"gpsSettingsChoose",new Button());Set(state,"gpsToolNotice","未配置");Call(state,"UpdateGpsSettings");Check(((TextBlock)Field(state,"gpsSettingsStatus")).Text.Contains("状态：未配置"),"settings actual UI missing status");
  Set(state,"gpsAdapter",adapter);Set(state,"gpsToolPath",source);Call(state,"UpdateGpsSettings");var status=((TextBlock)Field(state,"gpsSettingsStatus")).Text;Check(status.Contains("状态：可用")&&status.Contains("版本：13.59")&&status.Contains(source),"settings actual UI version/path/available");
  Set(state,"gpsAdapter",null);Call(state,"UpdateGpsSettings");Check(((TextBlock)Field(state,"gpsSettingsStatus")).Text.Contains("状态：校验失败"),"settings actual UI validation failure");
  session.SelectRoot(root);Check(session.Source!=null&&session.IsValid(),"same-root refresh retains identity-valid source");session.SelectRoot(root+"-other");Check(session.Source==null,"different root clears source");
  session.SelectRoot(root);session.SetSource(source,adapter);File.SetLastWriteTimeUtc(source,File.GetLastWriteTimeUtc(source).AddSeconds(5));Check(!session.IsValid(),"source identity change invalidates even same bytes");
  session.SetSource(source,adapter);File.Delete(source);Check(!session.IsValid(),"source deletion invalidates");
  Check(selected.SetEquals(new[]{b,c}),"batch UI updates preserve current selected set");
  Set(state,"activeFilter","undecided");Set(state,"gallerySortMode","capture_desc");Set(state,"currentSingleFile",b);Set(state,"singleZoom",1.75);Set(state,"advanceToUndecided",false);
  foreach(string n in new[]{"pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles"})((HashSet<string>)Field(state,n)).Add(n+".JPG");
  Call(state,"UpdateGpsLine");
  foreach(string n in new[]{"pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles"})Check(((HashSet<string>)Field(state,n)).SetEquals(new[]{n+".JPG"}),"GPS UX never changes "+n);
  Check((string)Field(state,"activeFilter")=="undecided"&&(string)Field(state,"gallerySortMode")=="capture_desc","GPS UI keeps filter / sort");
  Check((string)Field(state,"currentSingleFile")==b&&(double)Field(state,"singleZoom")==1.75&&!(bool)Field(state,"advanceToUndecided"),"GPS UI keeps current / zoom / browse mode");
  string now=File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(root)),"..","RAWMate.cs")).Replace("\r\n","\n");
  // Six unchanged safety classes retain the pre-UX hashes. The user-authorized
  // no-GPS-IFD implementation changes only Adapter's expected-coordinate argument
  // and JpgGpsIntegrity; their new bindings are backed by categorized mutation
  // tests, real-copy transactions, and an independently frozen ExistingGps body.
  string[] approvedHashes={"C467BE1471AEEE107A7B24C677AFD66878A1E0F063F65138A5D18AC812E55773","430DB1A0A5E55C09D090C580860AD27E60ECBB22167821057842AA947232E78F","3924481A6E0D2716AA4978F883C86F9C5AFD29F834D5C55613EE62681ED9B040","7A57642BDA956CC04243715A96496CB0F4441BE27C90BD35E840669E6585698E","533A1CC228C343677D3A1BAF063E57E13BED69A1DB89C288461ABBC6EE9A1C8D","3574200F575807D7891048B9AEC99E80DB6946651232A9B3C2CA12EC59246100","DB5DFC1EAC6CAC71748654FF838D82DA2638F27F26A5B036493E195BEA535EC4","2A0F287E74A0EC832B6190678C59F461412E927295188276215FD066080EF0FA"};int approvedIndex=0;
  string committed=now.Substring(now.IndexOf("    private void GpsCommitted("));committed=committed.Substring(0,committed.IndexOf("    private void ShowGpsBatchResult("));
  Check(!new[]{"SetDecision(","ToggleTile(","RebuildUi(","RefreshCurrentFolder(","activeFilter=","gallerySortMode=","selectedFiles.Clear","singleZoom=","LoadSinglePhoto("}.Any(committed.Contains),"real commit callback has no cull/navigation/selection/filter/sort/zoom reset");
  Check(committed.Contains("RemoveImageCachesForPaths(new[]{file})")&&!committed.Contains("Clear("),"real commit evicts target only");
  foreach(string type in new[]{"ExifToolProcessRunner","ApprovedExifToolRunner","ExifToolAdapter","GpsFileSafety","GpsEditTarget","GpsEditPlan","GpsFileTransaction","JpgGpsIntegrity"}) {
   Func<string,string> block=delegate(string text){var m=System.Text.RegularExpressions.Regex.Match(text,"(?m)^internal (?:sealed |static )?class "+type+"\\b");if(!m.Success)throw new Exception(type);var next=System.Text.RegularExpressions.Regex.Match(text.Substring(m.Index+m.Length),"(?m)^internal (?:sealed |static )?(?:class|enum|interface) ");return text.Substring(m.Index,next.Success?m.Length+next.Index:text.Length-m.Index).Trim();};
   using(var sha=System.Security.Cryptography.SHA256.Create())Check(BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(block(now)))).Replace("-","")==approvedHashes[approvedIndex++],"approved safety class byte-equivalent: "+type);
  }
 }
 [STAThread] static int Main(string[] args){try{root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"jpg"));Directory.CreateDirectory(Path.Combine(root,"arw"));typeof(GpsModelTests).GetField("fixture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,root);Run();Console.WriteLine("PASS "+count+" GPS UX/Grid assertions (fake writes only); "+root);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
