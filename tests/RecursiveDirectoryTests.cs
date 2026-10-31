using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class RecursiveDirectoryTests
{
    static int count;static string working;
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
    static void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
    static object Field(object o,string n){return o.GetType().GetField(n,Flags).GetValue(o);}
    static void Set(object o,string n,object v){o.GetType().GetField(n,Flags).SetValue(o,v);}
    static object Call(object o,string n,params object[] a){return o.GetType().GetMethods(Flags).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(o,a);}
    static string FileAt(string root,string relative,bool gps=false){string p=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(p));if(GpsFileSafety.IsJpg(p))GpsDirectoryRefreshTests.ExternalGps(p,gps);else File.WriteAllText(p,"synthetic RAW unchanged");return p;}
    static PhotoCollection Scan(string root){return PhotoCollection.Scan(root,CancellationToken.None);}
    static void Pump(Func<bool> done){var t=Stopwatch.StartNew();while(!done()){
        if(t.ElapsedMilliseconds>30000)throw new Exception("Dispatcher timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);Thread.Sleep(1);
    }}
    static void Refresh(MainWindow w){Call(w,"RefreshCurrentFolder");Pump(delegate{return !(bool)Field(w,"collectionScanning")&&!(bool)Field(w,"gpsDirectoryRefreshing");});}
    static MainWindow State(string root){var w=new MainWindow();Set(w,"settingsPath",Path.Combine(working,Guid.NewGuid()+"-settings.txt"));Set(w,"cullMarksPath",Path.Combine(working,Guid.NewGuid()+"-marks.txt"));Set(w,"gpsToolPath",null);Set(w,"singleViewMode",false);Set(w,"activeFilter","all");Set(w,"gallerySortMode","name_asc");
        foreach(string n in new[]{"pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles","retainedRawFiles","selectedFiles"})((HashSet<string>)Field(w,n)).Clear();Call(w,"ResetUiControls",root);return w;}
    static string[] Core(string root){return new[]{FileAt(root,"day1\\A.JPG",true),FileAt(root,"day1\\A.ARW"),FileAt(root,"day2\\jpg\\A.JPG"),FileAt(root,"day2\\arw\\A.ARW"),FileAt(root,"day3\\B.JPG"),FileAt(root,"day3\\B.ARW"),FileAt(root,"day4\\C.JPG")};}
    static void Model(){
        string root=Path.Combine(working,"model");string[] paths=Core(root);var hashes=paths.ToDictionary(p=>p,GpsFileSafety.Hash);var s=Scan(root);
        Check(s.Photos.Count(p=>p.IsJpg)==4,"recursive JPG=4");Check(s.Photos.Count(p=>!p.IsJpg)==3,"recursive ARW=3");Check(s.PendingCount==5,"recursive unclassified count includes nested loose photos");
        Check(s.JpgPairs.Count==4&&s.RawPairs.Count==3,"same filename across days retained independently");Check(s.JpgPairs["day1\\A"][0].AbsolutePath==paths[0],"same-directory pair key");Check(s.RawPairs["day1\\A"][0].AbsolutePath==paths[1],"same-directory RAW partner");
        Check(s.JpgPairs["day2\\A"][0].AbsolutePath==paths[2]&&s.RawPairs["day2\\A"][0].AbsolutePath==paths[3],"sibling jpg/arw pair key");Check(!s.RawPairs.ContainsKey("day4\\C"),"C is JPG-only");
        var photo=s.JpgPairs["day2\\A"][0];Check(photo.RelativePath=="day2\\jpg\\A.JPG"&&photo.RelativeDirectory=="day2\\jpg"&&photo.PairKey=="day2\\A"&&photo.AbsolutePath==Path.GetFullPath(paths[2]),"PhotoItem retains four path identities");
        Check(paths.All(f=>GpsFileSafety.Hash(f)==hashes[f]),"scan never moves, overwrites or edits photos");
        FileAt(root,"day5\\JPEG\\D.jPeG");FileAt(root,"day5\\RAW\\D.aRw");s=Scan(root);Check(s.JpgPairs["day5\\D"].Count==1&&s.RawPairs["day5\\D"].Count==1,"case-insensitive JPEG/RAW aliases");
        FileAt(root,"day6\\jpg\\E.JPG");FileAt(root,"day7\\arw\\E.ARW");s=Scan(root);Check(!s.RawPairs.ContainsKey("day6\\E")&&!s.JpgPairs.ContainsKey("day7\\E"),"no cross-day filename pairing");
        FileAt(root,"day2\\A.jpeg");s=Scan(root);Check(s.JpgPairs["day2\\A"].Count==2,"scope duplicate is ambiguity, not overwrite");
        FileAt(root,"deep\\jpg\\other\\F.JPG");s=Scan(root);Check(s.JpgPairs.ContainsKey("deep\\jpg\\other\\F"),"only immediate type layer is removed");
        foreach(string rel in new[]{"day1\\.rawmate-gps-failed.JPG","test-output\\hidden.JPG",".git\\hidden.JPG","day8\\test-output\\hidden.JPG"})FileAt(root,rel);
        foreach(string rel in new[]{"backup\\KEEP.JPG","recovery\\KEEP.JPG","rawmaterial\\KEEP.JPG"})FileAt(root,rel);
        s=Scan(root);Check(!s.Photos.Any(x=>x.AbsolutePath.Contains("hidden.JPG")||Path.GetFileName(x.AbsolutePath).StartsWith(".rawmate-gps-")),"own recovery/temp and development directories excluded");
        Check(new[]{"backup\\KEEP","recovery\\KEEP","rawmaterial\\KEEP"}.All(k=>s.JpgPairs.ContainsKey(k)),"ordinary user directory names not excluded");
        Check(!PhotoCollection.ContainsPath(root,Path.Combine(root+"-outside","A.JPG")),"separator-aware prefix boundary");Check(!PhotoCollection.ContainsPath(root,Path.Combine(root,"..","outside.JPG")),"normalized traversal rejected");
        Check(!PhotoCollection.ContainsPath(root,Path.Combine(root,"test-output","A.JPG")),"GPS excludes fixture directories too");
        var token=new CancellationTokenSource();token.Cancel();bool cancelled=false;try{PhotoCollection.Scan(root,token.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"scan cancellation is observed");
        string denied=Path.Combine(root,"denied");FileAt(root,"denied\\PRIVATE.JPG");var acl=Directory.GetAccessControl(denied);
        try {var block=new DirectorySecurity();block.SetSecurityDescriptorSddlForm(acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access),AccessControlSections.Access);block.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.ListDirectory,AccessControlType.Deny));Directory.SetAccessControl(denied,block);
            var partial=Scan(root);Check(partial.Diagnostics.Any(d=>d.Contains("denied"))&&!partial.Photos.Any(x=>x.RelativeDirectory=="denied"),"access-denied directory skipped with diagnostic");Check(partial.JpgPairs.ContainsKey("day1\\A"),"inaccessible subtree does not fail collection");
        }finally{Directory.SetAccessControl(denied,acl);}
        string outside=Path.Combine(working,"junction-target");FileAt(outside,"KEEP.JPG");string junction=Path.Combine(root,"linked");
        using(var process=Process.Start(new ProcessStartInfo("cmd.exe","/c mklink /J \""+junction+"\" \""+outside+"\""){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})) {process.StandardOutput.ReadToEnd();string err=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Fixture junction creation failed: "+err);}
        s=Scan(root);Check(!s.Photos.Any(x=>x.RelativeDirectory=="linked")&&s.Diagnostics.Any(d=>d.Contains("linked")),"junction not followed");
        var w=State(root);Call(w,"SetDecision",paths[0],"X");Check(((ICollection)Field(Call(w,"BuildCleanupPlan",root),"Items")).Count==0,"incomplete/link scan fails cleanup closed");Call(w,"ClearGpsSource");
    }
    static void Cleanup(){
        string root=Path.Combine(working,"cleanup");string[] f=Core(root);var w=State(root);
        Call(w,"SetDecision",f[0],"P");Call(w,"SetDecision",f[2],"R");Call(w,"SetDecision",f[4],"X");Call(w,"SetDecision",f[6],"J");
        Check((string)Call(w,"DecisionOf",f[0])=="P"&&(string)Call(w,"DecisionOf",f[2])=="R"&&(string)Call(w,"DecisionOf",f[4])=="X"&&(string)Call(w,"DecisionOf",f[6])=="J","four path-scoped independent P/J/R/X decisions");
        Check((bool)Call(w,"SaveCullMarks"),"persist original v2 full-path format");var load=State(root);Set(load,"cullMarksPath",Field(w,"cullMarksPath"));Call(load,"LoadCullMarks");Check((string)Call(load,"DecisionOf",f[0])=="P"&&(string)Call(load,"DecisionOf",f[2])=="R","same-name marks survive v2 reload without migration");
        Call(w,"SetDecision",f[0],"J");var plan=Call(w,"BuildCleanupPlan",root);var items=((IEnumerable)Field(plan,"Items")).Cast<object>().ToList();Check(items.Count==3,"J/R/X three safe scoped groups");
        foreach(var item in items){string jpg=(string)Field(item,"Jpg"),raw=(string)Field(item,"Raw");Check(raw==f[Array.IndexOf(f,jpg)+1],"cleanup actual scanned pair path: "+jpg);}
        string bin=Path.Combine(working,"simulated-recycle");Directory.CreateDirectory(bin);var touched=new List<string>();
        Func<string,bool> recycle=delegate(string path){touched.Add(path);File.Move(path,Path.Combine(bin,Guid.NewGuid()+"-"+Path.GetFileName(path)));return true;};
        var result=Call(w,"ExecuteCleanupPlan",plan,recycle,new Func<bool>(delegate{return (bool)Call(w,"SaveCullMarks");}));
        Check(((ICollection)Field(result,"SuccessPaths")).Count==4,"exact four simulated recycle outcomes");Check(File.Exists(f[0])&&!File.Exists(f[1]),"J retains day1 JPG, recycles correct day1 RAW");Check(!File.Exists(f[2])&&File.Exists(f[3]),"R retains day2 RAW, recycles correct day2 JPG");Check(!File.Exists(f[4])&&!File.Exists(f[5]),"X recycles only day3 pair");Check(File.Exists(f[6]),"unpaired J JPG protected");
        Check(touched.IndexOf(f[5])<touched.IndexOf(f[4]),"X RAW-first order retained");Set(w,"collection",Scan(root));Check(((List<string>)Call(w,"CompletedRawOnlyInRoot",root)).SequenceEqual(new[]{f[3]}),"completed RAW-only count scoped recursively");
        var report=Call(w,"InspectCorrespondence",root);Check((int)Field(report,"RawOnly")==1&&(int)Field(report,"JpgOnly")==2,"recursive post-cleanup correspondence");
        var fresh=State(root);Set(fresh,"cullMarksPath",Field(w,"cullMarksPath"));Call(fresh,"LoadCullMarks");Check(((HashSet<string>)Field(fresh,"retainedRawFiles")).Contains(f[3]),"R survivor intent persists in nested folder");
        string badRoot=Path.Combine(working,"ambiguous");string[] b=Core(badRoot);FileAt(badRoot,"day2\\A.jpeg");var bad=State(badRoot);Call(bad,"SetDecision",b[2],"X");Check(((ICollection)Field(Call(bad,"BuildCleanupPlan",badRoot),"Items")).Count==0,"ambiguous same-scope JPG cannot clean RAW");
        var changed=State(badRoot);Call(changed,"SetDecision",b[0],"X");var confirmed=Call(changed,"BuildCleanupPlan",badRoot);FileAt(badRoot,"day1\\raw\\A.ARW");int before=touched.Count;Call(changed,"ExecuteCleanupPlan",confirmed,recycle,new Func<bool>(delegate{return true;}));Check(touched.Count==before&&File.Exists(b[0]),"late duplicate RAW invalidates confirmed plan");
    }
    static void UiRefresh(){
        string root=Path.Combine(working,"ui");string[] f=Core(root);var w=State(root);Call(w,"RebuildUi");Refresh(w);
        Check(((List<string>)Field(w,"galleryFiles")).Count==4&&((IDictionary)Field(w,"tileFiles")).Count==4,"flat Grid shows all four recursive JPG");Check(((TextBlock)Field(w,"jpgCount")).Text=="4 张"&&((TextBlock)Field(w,"rawCount")).Text=="3 张","recursive sidebar counts");
        Call(w,"SetGpsSource",f[0]);Check(((GpsSourceSession)Field(w,"gpsSession")).Source!=null,"nested GPS source selected");Check((bool)Call(w,"IsLegalGpsCandidate",f[6]),"cross-day target allowed by UI");
        var selection=(HashSet<string>)Field(w,"selectedFiles");selection.Add(f[0]);selection.Add(f[6]);Set(w,"currentSingleFile",f[6]);Set(w,"activeFilter","picked");Set(w,"gallerySortMode","name_desc");Set(w,"advanceToUndecided",false);Set(w,"singleZoom",1.75);
        Call(w,"SetDecision",f[0],"P");Call(w,"SetDecision",f[2],"J");Call(w,"SetDecision",f[4],"R");Call(w,"SetDecision",f[6],"X");
        FileAt(root,"day5\\nested\\NEW.JPG");FileAt(root,"day5\\nested\\NEW.ARW");Refresh(w);
        Check(((List<string>)Field(w,"galleryFiles")).Count==5,"refresh discovers added date/subdirectory");Check(selection.SetEquals(new[]{f[0],f[6]})&&(string)Field(w,"currentSingleFile")==f[6],"selection and current path retained");
        Check((string)Field(w,"activeFilter")=="picked"&&(string)Field(w,"gallerySortMode")=="name_desc"&&!(bool)Field(w,"advanceToUndecided")&&(double)Field(w,"singleZoom")==1.75,"filter/sort/browse/zoom retained");
        Check((string)Call(w,"DecisionOf",f[0])=="P"&&(string)Call(w,"DecisionOf",f[2])=="J"&&(string)Call(w,"DecisionOf",f[4])=="R"&&(string)Call(w,"DecisionOf",f[6])=="X","refresh preserves all marks independently");
        Directory.Move(Path.Combine(root,"day3"),Path.Combine(working,"ui-removed-day3"));Refresh(w);Check(!((List<string>)Field(w,"galleryFiles")).Contains(f[4])&&((TextBlock)Field(w,"rawCount")).Text=="3 张","removed entire date disappears");
        GpsDirectoryRefreshTests.ExternalGps(f[0],true,40.5,25.6);Refresh(w);Check(Math.Abs(((GpsSourceSession)Field(w,"gpsSession")).Source.Latitude-40.5)<1e-7,"nested source coordinates refresh despite preserved mtime");
        GpsDirectoryRefreshTests.ExternalGps(f[6],true);Refresh(w);Check(Call(w,"GetGpsDisplay",f[6])!=null,"nested external added GPS refresh");GpsDirectoryRefreshTests.ExternalGps(f[6],false);Refresh(w);Check(Call(w,"GetGpsDisplay",f[6])==null,"nested external GPS removal refresh");
        Directory.Move(Path.Combine(root,"day1"),Path.Combine(working,"ui-removed-source"));Refresh(w);Check(((GpsSourceSession)Field(w,"gpsSession")).Source==null,"deleted nested source clears after refresh");
        Check(!selection.Contains(f[0])&&selection.Contains(f[6]),"deleted selection removed, surviving path retained");
        Set(w,"activeFilter","all");selection.Add(f[2]);Call(w,"RefreshCurrentFolder");Call(w,"RefreshCurrentFolder");Pump(delegate{return !(bool)Field(w,"gpsDirectoryRefreshing");});Check(((PhotoCollection)Field(w,"collection")).Root==root,"latest recursive generation wins");Check(selection.SetEquals(new[]{f[2],f[6]}),"visible current photo must not collapse multi-selection on refresh");Call(w,"ClearGpsSource");
    }
    static ClassificationResult Classify(MainWindow w,bool undo,Func<bool> persist=null){
        string root=(string)Call(w,"RootFolder",false);Call(w,"BeginClassificationContext");
        var plan=ClassificationPlan.Build(root,undo,CancellationToken.None);
        var task=Task.Factory.StartNew(delegate{return ClassificationExecutor.Execute(plan,CancellationToken.None,delegate(string a,string b){return (bool)w.Dispatcher.Invoke(new Func<bool>(delegate{return (bool)Call(w,"CommitClassificationMove",a,b,persist??new Func<bool>(delegate{return (bool)Call(w,"SaveCullMarks");}));}));});});
        Pump(delegate{return task.IsCompleted;});var result=task.Result;Call(w,"EndClassificationContext",result);Pump(delegate{return !(bool)Field(w,"collectionScanning")&&!(bool)Field(w,"gpsDirectoryRefreshing");});return result;
    }
    static void Classification(){
        string root=Path.Combine(working,"classification");string[] f={FileAt(root,"day1\\A.JPG",true),FileAt(root,"day1\\A.ARW"),FileAt(root,"day2\\B.JPG"),FileAt(root,"day2\\B.ARW"),FileAt(root,"day3\\jpg\\C.JPG"),FileAt(root,"day3\\arw\\C.ARW"),FileAt(root,"day4\\D.JPG")};
        var hash=f.ToDictionary(p=>p,GpsFileSafety.Hash);var plan=ClassificationPlan.Build(root,false,CancellationToken.None);
        Check(plan.ReadyCount==5&&plan.ConflictCount==0,"classification exact 4 JPG / 3 RAW / 5 pending fixture");
        Check(plan.Entries.Select(e=>Path.GetDirectoryName(Path.GetDirectoryName(e.Destination))).Distinct().Count()==3,"classification stays in each of three actual parent directories");
        var w=State(root);Call(w,"RebuildUi");Refresh(w);Check(((Button)Field(w,"organizeButton")).IsEnabled,"create classification enabled from recursive pending count");
        string[] decisions={"P","J","R","X"};string[] jpg={f[0],f[2],f[4],f[6]};for(int i=0;i<4;i++)Call(w,"SetDecision",jpg[i],decisions[i]);
        ((HashSet<string>)Field(w,"selectedFiles")).UnionWith(new[]{f[0],f[2]});Set(w,"currentSingleFile",f[2]);Set(w,"activeFilter","all");Set(w,"gallerySortMode","name_desc");Set(w,"advanceToUndecided",false);Set(w,"singleZoom",1.65);Call(w,"SetGpsSource",f[0]);
        var beforeSource=((GpsSourceSession)Field(w,"gpsSession")).Source;Check(beforeSource!=null,"classification fixture source valid before movement");
        string[] dest=f.Select(p=>ClassificationPlan.Destination(new PhotoItem(root,p),false)).ToArray();dest[4]=f[4];dest[5]=f[5];
        var result=Classify(w,false);Check(result.Moved.Count==5&&result.Failed.Count==0&&result.Conflicts.Count==0,"five recursive classification moves committed");
        for(int i=0;i<f.Length;i++)Check(File.Exists(dest[i])&&GpsFileSafety.Hash(dest[i])==hash[f[i]],"classification exact payload retained: "+i);
        Check(!Directory.Exists(Path.Combine(root,"jpg"))&&!Directory.Exists(Path.Combine(root,"arw")),"no flattened collection-root aggregation");
        Check(Scan(root).PendingCount==0&&Scan(root).Photos.Count==7,"pending becomes zero without photo loss");
        Check(!((Button)Field(w,"organizeButton")).IsEnabled,"classification button disabled when recursive pending zero");
        Check(ClassificationPlan.Build(root,false,CancellationToken.None).Entries.Count==0,"already classified never nests jpg/jpg or arw/arw");
        for(int i=0;i<4;i++)Check((string)Call(w,"DecisionOf",dest[Array.IndexOf(f,jpg[i])])==decisions[i],"classification preserves "+decisions[i]);
        Check(((HashSet<string>)Field(w,"selectedFiles")).SetEquals(new[]{dest[0],dest[2]})&&(string)Field(w,"currentSingleFile")==dest[2],"classification maps selection and current photo");
        Check((string)Field(w,"gallerySortMode")=="name_desc"&&!(bool)Field(w,"advanceToUndecided")&&(double)Field(w,"singleZoom")==1.65,"classification retains sort/browse/zoom");
        var movedSource=((GpsSourceSession)Field(w,"gpsSession")).Source;Check(movedSource!=null&&movedSource.SourcePath==dest[0]&&movedSource.SameCoordinates(beforeSource),"source moves, revalidates identity and retains exact GPS");
        Check((bool)Call(w,"IsLegalGpsCandidate",dest[2]),"apply-source target stays available after movement");
        Check(((PhotoCollection)Field(w,"collection")).RawPairs["day1\\A"][0].AbsolutePath==dest[1],"PairKey and actual RAW path refreshed");
        var reloaded=State(root);Set(reloaded,"cullMarksPath",Field(w,"cullMarksPath"));Call(reloaded,"LoadCullMarks");Check((string)Call(reloaded,"DecisionOf",dest[0])=="P"&&(string)Call(reloaded,"DecisionOf",dest[2])=="J","migrated marks persist under new full paths");
        Set(w,"activeFilter","picked");Set(w,"singleViewMode",true);Set(w,"fitSingleImage",false);
        var undone=Classify(w,true);Check(undone.Moved.Count==7&&undone.Failed.Count==0,"undo recursively restores seven type-folder files one layer");
        string[] final=f.Select(p=>Path.Combine(PhotoCollection.IsTypeDirectory(Path.GetFileName(Path.GetDirectoryName(p)))?Path.GetDirectoryName(Path.GetDirectoryName(p)):Path.GetDirectoryName(p),Path.GetFileName(p))).ToArray();
        for(int i=0;i<f.Length;i++)Check(File.Exists(final[i])&&GpsFileSafety.Hash(final[i])==hash[f[i]],"undo retains date parent and payload: "+i);
        for(int i=0;i<4;i++)Check((string)Call(w,"DecisionOf",final[Array.IndexOf(f,jpg[i])])==decisions[i],"undo preserves "+decisions[i]);
        Check(Scan(root).PendingCount==7&&((HashSet<string>)Field(w,"selectedFiles")).SetEquals(new[]{final[0],final[2]})&&(string)Field(w,"currentSingleFile")==final[2],"undo refreshes pending, selection and current photo");
        Check((string)Field(w,"activeFilter")=="picked"&&(bool)Field(w,"singleViewMode")&&(string)Field(w,"gallerySortMode")=="name_desc"&&!(bool)Field(w,"advanceToUndecided")&&(double)Field(w,"singleZoom")==1.65,"non-default filter and single-photo mode/zoom survive undo");
        Check(((GpsSourceSession)Field(w,"gpsSession")).Source.SourcePath==final[0]&&((GpsSourceSession)Field(w,"gpsSession")).Source.SameCoordinates(beforeSource),"undo maps source back and re-reads GPS");
        Check(Directory.Exists(Path.Combine(root,"day3","jpg"))&&Directory.Exists(Path.Combine(root,"day3","arw")),"preexisting empty type directories are preserved");Call(w,"ClearGpsSource");
        string rawRoot=Path.Combine(working,"classification-raw-survivor"),raw=FileAt(rawRoot,"day1\\KEEP.ARW");var rawWindow=State(rawRoot);((HashSet<string>)Field(rawWindow,"retainedRawFiles")).Add(raw);Classify(rawWindow,false);
        string rawDest=Path.Combine(rawRoot,"day1","arw","KEEP.ARW");Check(((HashSet<string>)Field(rawWindow,"retainedRawFiles")).Contains(rawDest),"completed RAW-only intent migrates on classify");Classify(rawWindow,true);Check(((HashSet<string>)Field(rawWindow,"retainedRawFiles")).Contains(raw),"completed RAW-only intent migrates on undo");
    }
    static void ClassificationFailures(){
        string root=Path.Combine(working,"classification-conflicts"),source=FileAt(root,"day1\\A.JPG"),target=FileAt(root,"day1\\jpg\\A.JPG",true),other=FileAt(root,"day2\\B.JPG");string sh=GpsFileSafety.Hash(source),th=GpsFileSafety.Hash(target);
        var plan=ClassificationPlan.Build(root,false,CancellationToken.None);var result=ClassificationExecutor.Execute(plan,CancellationToken.None,delegate{return true;});
        Check(result.Conflicts.Count==1&&result.Moved.Count==1&&result.Failed.Count==0,"create conflict does not stop unrelated safe parent");Check(GpsFileSafety.Hash(source)==sh&&GpsFileSafety.Hash(target)==th&&Scan(root).PendingCount==1,"existing destination and conflicting source unchanged; pending retained");
        var undo=ClassificationExecutor.Execute(ClassificationPlan.Build(root,true,CancellationToken.None),CancellationToken.None,delegate{return true;});Check(undo.Conflicts.Count==1&&GpsFileSafety.Hash(source)==sh&&GpsFileSafety.Hash(target)==th,"undo conflict keeps both copies without overwrite");
        string aliasRoot=Path.Combine(working,"classification-alias"),aliasA=FileAt(aliasRoot,"day1\\jpg\\SAME.JPG"),aliasB=FileAt(aliasRoot,"day1\\jpeg\\SAME.JPG");var alias=ClassificationPlan.Build(aliasRoot,true,CancellationToken.None);Check(alias.ReadyCount==0&&alias.ConflictCount==2,"all same-destination aliases conflict; no first wins");
        Check(ClassificationExecutor.Execute(alias,CancellationToken.None,delegate{return true;}).Conflicts.Count==2&&File.Exists(aliasA)&&File.Exists(aliasB),"duplicate undo sources stay at both original paths");
        string typeRoot=Path.Combine(working,"named-type","jpg"),typeFile=FileAt(typeRoot,"HERE.JPG");Check(Scan(typeRoot).PendingCount==0,"selected root named jpg uses actual direct parent for pending");var typePlan=ClassificationPlan.Build(typeRoot,true,CancellationToken.None);Check(typePlan.ReadyCount==0&&typePlan.Entries.All(e=>e.Disposition=="skipped"),"undo never leaves selected root named jpg");
        string lateRoot=Path.Combine(working,"classification-late"),late=FileAt(lateRoot,"day1\\L.JPG");var latePlan=ClassificationPlan.Build(lateRoot,false,CancellationToken.None);string lateDest=latePlan.Entries[0].Destination;
        var lateResult=ClassificationExecutor.Execute(latePlan,CancellationToken.None,delegate{return true;},null,delegate{File.Copy(late,lateDest);});Check(lateResult.Conflicts.Count==1&&lateResult.Moved.Count==0&&File.Exists(late)&&File.Exists(lateDest),"late destination appears before rename: no overwrite");
        string changedRoot=Path.Combine(working,"classification-changed"),changed=FileAt(changedRoot,"day1\\M.JPG");var changedPlan=ClassificationPlan.Build(changedRoot,false,CancellationToken.None);File.SetLastWriteTimeUtc(changed,File.GetLastWriteTimeUtc(changed).AddSeconds(3));var changedResult=ClassificationExecutor.Execute(changedPlan,CancellationToken.None,delegate{return true;});Check(changedResult.Failed.Count==1&&File.Exists(changed),"source identity change after confirmation blocks move");
        string lockedRoot=Path.Combine(working,"classification-ancestor-lock"),locked=FileAt(lockedRoot,"day1\\N.JPG");bool blocked=false;var lockedResult=ClassificationExecutor.Execute(ClassificationPlan.Build(lockedRoot,false,CancellationToken.None),CancellationToken.None,delegate{return true;},null,delegate{try{Directory.Move(Path.GetDirectoryName(locked),Path.Combine(lockedRoot,"swapped"));}catch(IOException){blocked=true;}});Check(blocked&&lockedResult.Moved.Count==1,"ancestor locks prevent directory/junction swap through rename");
        string failRoot=Path.Combine(working,"classification-state-failure"),fail=FileAt(failRoot,"day1\\A.JPG",true),next=FileAt(failRoot,"day2\\B.JPG");var w=State(failRoot);Call(w,"SetDecision",fail,"P");string failedDest=ClassificationPlan.Destination(new PhotoItem(failRoot,fail),false);((HashSet<string>)Field(w,"rejectedFiles")).Add(failedDest);((HashSet<string>)Field(w,"selectedFiles")).Add(fail);Set(w,"currentSingleFile",fail);Call(w,"SetGpsSource",fail);string before=GpsFileSafety.Hash(fail);
        var failed=Classify(w,false,delegate{return false;});Check(failed.Failed.Count==1&&failed.Moved.Count==0&&File.Exists(fail)&&!File.Exists(failedDest)&&File.Exists(next),"state persistence failure rolls back file and stops remaining moves");
        Check(GpsFileSafety.Hash(fail)==before&&(string)Call(w,"DecisionOf",fail)=="P"&&((HashSet<string>)Field(w,"rejectedFiles")).Contains(failedDest),"failed checkpoint restores exact source and stale destination marks");Check(((HashSet<string>)Field(w,"selectedFiles")).Contains(fail)&&(string)Field(w,"currentSingleFile")==fail&&((GpsSourceSession)Field(w,"gpsSession")).Source.SourcePath==fail,"failed move retains source/selection/current identity");Call(w,"ClearGpsSource");
        string cancelRoot=Path.Combine(working,"classification-cancel");FileAt(cancelRoot,"day1\\ONE.JPG");FileAt(cancelRoot,"day2\\TWO.JPG");var cancel=new CancellationTokenSource();var cancelPlan=ClassificationPlan.Build(cancelRoot,false,CancellationToken.None);cancel.Cancel();var cancelled=ClassificationExecutor.Execute(cancelPlan,cancel.Token,delegate{return true;});Check(cancelled.Cancelled&&cancelled.Moved.Count==0&&Scan(cancelRoot).PendingCount==2,"cancel before first move leaves every photo untouched");cancel.Dispose();cancel=new CancellationTokenSource();var partial=ClassificationExecutor.Execute(cancelPlan,cancel.Token,delegate{cancel.Cancel();return true;});Check(partial.Cancelled&&partial.Moved.Count==1&&Scan(cancelRoot).PendingCount==1,"cancel remaining keeps exactly the committed file and untouched remainder");cancel.Dispose();
        bool boundary=false;try{ClassificationPlan.ValidatePaths(root,source,Path.Combine(root+"-outside","X.JPG"));}catch(IOException){boundary=true;}Check(boundary,"classification shares normalized collection boundary gate");
        foreach(string unsafePath in new[]{source+":stream",root+"\\bad\nname.JPG"}){bool rejected=false;try{ClassificationPlan.ValidatePaths(root,unsafePath,target);}catch(IOException){rejected=true;}catch(ArgumentException){rejected=true;}Check(rejected,"classification preserves ADS/newline CheckPath rejection");}
    }
    static void ClassificationScale(){
        string root=Path.Combine(working,"classification-scale");for(int i=0;i<500;i++)FileAt(root,"day"+(i%20)+"\\N"+i+".JPG");var window=State(root);bool beat=false;var watch=Stopwatch.StartNew();Call(window,"BeginClassificationContext");
        var task=Task.Factory.StartNew(delegate{var plan=ClassificationPlan.Build(root,false,CancellationToken.None);return ClassificationExecutor.Execute(plan,CancellationToken.None,delegate(string a,string b){return (bool)window.Dispatcher.Invoke(new Func<bool>(delegate{return (bool)Call(window,"CommitClassificationMove",a,b,new Func<bool>(delegate{return true;}));}));});});
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send,new Action(delegate{beat=!task.IsCompleted;}));Pump(delegate{return task.IsCompleted;});var result=task.Result;Call(window,"EndClassificationContext",result);Pump(delegate{return !(bool)Field(window,"collectionScanning")&&!(bool)Field(window,"gpsDirectoryRefreshing");});
        Check(beat&&result.Moved.Count==500&&result.Failed.Count==0,"500-file background classification keeps Dispatcher responsive");Check(Scan(root).PendingCount==0&&Scan(root).Photos.Count==500,"500-file parent-local classification has no loss or pending remainder");Console.WriteLine("CLASSIFICATION SCALE 500 files="+watch.ElapsedMilliseconds+" ms including refresh");
    }
    static void Scale(){
        string root=Path.Combine(working,"scale");for(int i=0;i<1000;i++){string scope="day"+(i%10)+"\\level"+(i%4)+"\\";FileAt(root,scope+"jpg\\N"+i+".JPG");FileAt(root,scope+"arw\\N"+i+".ARW");}
        var w=State(root);bool beat=false;var timer=Stopwatch.StartNew();Call(w,"RefreshCurrentFolder");long enqueue=timer.ElapsedMilliseconds;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send,new Action(delegate{beat=(bool)Field(w,"collectionScanning");}));
        Pump(delegate{return !(bool)Field(w,"collectionScanning")&&!(bool)Field(w,"gpsDirectoryRefreshing");});var snapshot=(PhotoCollection)Field(w,"collection");
        Check(enqueue<1000&&beat,"2000-file recursive scan leaves Dispatcher responsive");Check(snapshot.Photos.Count==2000&&snapshot.JpgPairs.Count==1000&&snapshot.RawPairs.Count==1000,"1000 JPG + 1000 ARW complete with unique scoped pairs");
        Console.WriteLine("SCALE enqueue="+enqueue+" ms; enumeration + Grid + GPS="+timer.ElapsedMilliseconds+" ms");
        string other=Path.Combine(working,"last-root");FileAt(other,"dayLast\\LAST.JPG");Call(w,"RefreshCurrentFolder");Call(w,"ResetUiControls",other);Call(w,"RefreshCurrentFolder");Pump(delegate{return !(bool)Field(w,"collectionScanning")&&!(bool)Field(w,"gpsDirectoryRefreshing");});
        Check(((PhotoCollection)Field(w,"collection")).Root==other&&((List<string>)Field(w,"galleryFiles")).Count==1,"cancelled old large root cannot overwrite newer root");Call(w,"ClearGpsSource");
    }
    static void CrossDayGps(string tool,string baseline){
        string root=Path.Combine(working,"real-gps");string source=Path.Combine(root,"day1","SOURCE.JPG"),target=Path.Combine(root,"day4","deep","TARGET.JPG");Directory.CreateDirectory(Path.GetDirectoryName(source));Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(Path.Combine(baseline,"existing","jpg","DSC01265.JPG"),source);File.Copy(Path.Combine(baseline,"existing","jpg","DSC01270.JPG"),target);string sourceHash=GpsFileSafety.Hash(source);
        string raw=FileAt(root,"day4\\deep\\TARGET.ARW");string rawHash=GpsFileSafety.Hash(raw);
        using(var adapter=new ExifToolAdapter(tool)) {var session=new GpsSourceSession();session.SelectRoot(root);session.SetSource(source,adapter);var plan=session.CreatePlan(target,adapter);Check(plan.Targets.Count==1&&plan.JpgPath==target,"real cross-day GPS plan includes only selected JPG");
            var result=new GpsFileTransaction(adapter,GpsFileSafety.ValidateParsing).Execute(plan,plan.Targets[0],false);Check(result.Success,"real cross-day working-copy GPS transaction: "+result.Message);
            var after=adapter.Read(target);Check(after.Gps!=null&&after.Gps.SameCoordinates(session.Source),"cross-day GPS exact readback");
            var batch=new GpsBatchPlan(root,source,new[]{source,target},adapter);Check(batch.Count(GpsSelectionKind.Source)==1&&batch.Count(GpsSelectionKind.Existing)==1,"recursive batch source/overwrite classification retained");
        }
        Check(GpsFileSafety.Hash(source)==sourceHash&&GpsFileSafety.Hash(raw)==rawHash,"cross-day source and RAW unchanged");Check(!Directory.GetFiles(root,"*.xmp",SearchOption.AllDirectories).Any()&&!Directory.GetFiles(root,".rawmate-gps-*",SearchOption.AllDirectories).Any(),"no XMP or successful transaction residues");
        foreach(string file in new[]{FileAt(root,".rawmate-gps-recovery.JPG"),FileAt(root,"test-output\\EXCLUDED.JPG"),FileAt(root+"-outside","OUTSIDE.JPG")}){bool rejected=false;try{GpsFileSafety.ValidateJpg(root,file);}catch(IOException){rejected=true;}Check(rejected,"GPS membership rejects non-photo/outside target");}
    }
    [STAThread]static int Main(string[] args){try{working=Path.GetFullPath(args[0]);if(Directory.Exists(working)||!working.StartsWith("C:\\rawmate\\test-output\\",StringComparison.OrdinalIgnoreCase))throw new Exception("Fresh test-output only");Directory.CreateDirectory(working);Model();Cleanup();UiRefresh();Classification();ClassificationFailures();ClassificationScale();Scale();CrossDayGps(args[1],args[2]);Console.WriteLine("PASS "+count+" recursive collection assertions; simulated recycle only; real GPS writes working copies only; "+working);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
