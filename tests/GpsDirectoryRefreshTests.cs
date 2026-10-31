using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// External metadata mutations are synthetic, fixed-length JPEG fixtures only.
// Never calls ExifTool, a GPS transaction, or any production write/integrity path.
internal static class GpsDirectoryRefreshTests {
    static int count;static string root;static MainWindow window;
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
    static object Field(string name){return typeof(MainWindow).GetField(name,Flags).GetValue(window);}
    static void Set(string name,object value){typeof(MainWindow).GetField(name,Flags).SetValue(window,value);}
    static object Call(string name,params object[] args){return typeof(MainWindow).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(window,args);}
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void PumpUntil(Func<bool> complete){var watch=Stopwatch.StartNew();while(!complete()){
        if(watch.ElapsedMilliseconds>15000)throw new Exception("Dispatcher completion timeout");
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);Thread.Sleep(2);
    }}
    static void Refresh(){Call("RefreshCurrentFolder");PumpUntil(delegate{return !(bool)Field("gpsDirectoryRefreshing");});}
    static string Photo(string name){return Path.Combine(root,"jpg",name+".JPG");}
    // Same path, file ID, length and timestamp. Swap only the EXIF fixture bytes.
    internal static void ExternalGps(string path,bool valid,double lat=38.1,double lon=23.2) {
        DateTime stamp=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.UtcNow.AddDays(-1);
        var encoder=new JpegBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8,8,96,96,PixelFormats.Rgb24,null,new byte[192],24)));
        byte[] jpeg;using(var stream=new MemoryStream()){encoder.Save(stream);jpeg=stream.ToArray();}
        var tiff=new byte[256];using(var memory=new MemoryStream(tiff))using(var b=new BinaryWriter(memory)){
            b.Write((ushort)0x4949);b.Write((ushort)42);b.Write((uint)8);
            b.Write((ushort)(valid?1:0));b.Write((ushort)0x8825);b.Write((ushort)4);b.Write((uint)1);b.Write((uint)26);b.Write((uint)0);
            memory.Position=26;b.Write((ushort)5);
            Entry(b,1,2,2,(uint)'N');Entry(b,2,5,3,92);Entry(b,3,2,2,(uint)'E');Entry(b,4,5,3,116);Entry(b,9,2,2,(uint)'A');b.Write((uint)0);
            memory.Position=92;foreach(var r in GpsMetadata.ToDms(lat))b.Write(r);foreach(var r in GpsMetadata.ToDms(lon))b.Write(r);
        }
        using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read))using(var b=new BinaryWriter(stream)){
            b.Write(jpeg,0,2);b.Write((byte)255);b.Write((byte)225);int length=tiff.Length+8;b.Write((byte)(length>>8));b.Write((byte)length);
            b.Write(Encoding.ASCII.GetBytes("Exif\0\0"));b.Write(tiff);b.Write(jpeg,2,jpeg.Length-2);
        }
        File.SetLastWriteTimeUtc(path,stamp);
    }
    static void Entry(BinaryWriter b,ushort tag,ushort type,uint count,uint data){b.Write(tag);b.Write(type);b.Write(count);b.Write(data);}
    static GpsMetadata Display(string file){return (GpsMetadata)Call("GetGpsDisplay",file);}
    static string Badge(string file){return (string)((Dictionary<Border,string>)Field("gpsBadges")).First(e=>e.Value==file).Key.ToolTip;}
    static void Source(string file){Call("SetGpsSource",file);Check(((GpsSourceSession)Field("gpsSession")).Source!=null,"source established");}
    static void SeedCache(string file,GpsMetadata gps){var type=typeof(MainWindow).GetNestedType("GpsDisplay",BindingFlags.NonPublic);var value=Activator.CreateInstance(type,true);
        type.GetField("Stamp",Flags).SetValue(value,Call("GpsDisplayStamp",file));type.GetField("Gps",Flags).SetValue(value,gps);((IDictionary)Field("gpsDisplayCache"))[file]=value;
    }
    static void Run(){
        Directory.CreateDirectory(Path.Combine(root,"jpg"));Directory.CreateDirectory(Path.Combine(root,"arw"));
        string a=Photo("A"),b=Photo("B"),c=Photo("C"),d=Photo("D");ExternalGps(a,false);ExternalGps(b,true);ExternalGps(c,true);ExternalGps(d,true);
        window=new MainWindow();Set("settingsPath",Path.Combine(root,"isolated-settings.txt"));Set("cullMarksPath",Path.Combine(root,"isolated-marks.txt"));Set("gpsToolPath",null);
        Call("ResetUiControls",root);Set("singleViewMode",false);Set("activeFilter","all");Set("gallerySortMode","name_asc");Set("navigationRoot",root);
        foreach(string name in new[]{"pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles","retainedRawFiles","selectedFiles"})((HashSet<string>)Field(name)).Clear();
        Call("RebuildUi");Call("SelectGpsRoot",root);Refresh();
        Check(Display(a)==null&&Badge(a)=="无 GPS 信息","initial No GPS actual WIC and badge");
        string identity=(string)Call("GpsDisplayStamp",a);long length=new FileInfo(a).Length;
        ExternalGps(a,true);Check((string)Call("GpsDisplayStamp",a)==identity&&new FileInfo(a).Length==length,"external GPS edit preserves full stamp, length, path");
        Check(Display(a)==null,"stamp-only cache is stale before explicit refresh");Refresh();
        Check(Display(a)!=null&&Badge(a)=="已有 GPS 信息","No GPS to Has GPS after refresh without restart");
        ExternalGps(a,false);Refresh();Check(Display(a)==null&&Badge(a)=="无 GPS 信息","Has GPS to No GPS after external removal");
        Source(b);ExternalGps(b,true,39.5,24.6);Refresh();var session=(GpsSourceSession)Field("gpsSession");
        Check(session.Source!=null&&Math.Abs(session.Source.Latitude-39.5)<1e-7&&Math.Abs(session.Source.Longitude-24.6)<1e-7,"source retained with new disk coordinates");
        Check(Badge(b)=="当前 GPS 来源","source badge priority preserved");
        // Watcher can have cleared coordinates before the user clicks Refresh.
        ExternalGps(b,true,40.5,25.6);Call("InvalidateGpsSource",false);Refresh();session=(GpsSourceSession)Field("gpsSession");
        Check(session.Source!=null&&Math.Abs(session.Source.Latitude-40.5)<1e-7,"refresh rechecks chosen path after watcher invalidation");
        Call("ClearGpsSource");Refresh();Check(((GpsSourceSession)Field("gpsSession")).Source==null,"explicit clear never resurrects source");
        Source(b);ExternalGps(b,false);Refresh();Check(((GpsSourceSession)Field("gpsSession")).Source==null&&Badge(b)=="无 GPS 信息","removed source GPS clears source and badge");
        Source(c);File.Move(c,c+".test-deleted");Refresh();Check(((GpsSourceSession)Field("gpsSession")).Source==null,"deleted source clears source");
        Source(d);using(var locked=new FileStream(d,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Refresh();Check(((GpsSourceSession)Field("gpsSession")).Source==null,"unreadable source clears source");}
        var selection=(HashSet<string>)Field("selectedFiles");selection.Add(a);selection.Add(b);Set("currentSingleFile",b);Set("advanceToUndecided",false);Set("gridTileWidth",160.0);
        foreach(var n in new[]{"pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles"})((HashSet<string>)Field(n)).Add(Photo(n));
        Set("activeFilter","picked");Set("gallerySortMode","name_desc");Refresh();
        var image=BitmapSource.Create(8,8,96,96,PixelFormats.Rgb24,null,new byte[192],24);image.Freeze();
        ((IDictionary)Field("thumbnailCache"))[a]=Tuple.Create(512,image);((IDictionary)Field("thumbnailStamps"))[a]=Call("FileStamp",a);
        ((IDictionary)Field("fullImageCache"))[a]=image;
        string other=Path.Combine(root+"-outside","KEEP.JPG");Directory.CreateDirectory(Path.GetDirectoryName(other));ExternalGps(other,true);SeedCache(other,new GpsMetadata(1,2,null,null,other));
        Refresh();
        Check(selection.SetEquals(new[]{a,b})&&(string)Field("currentSingleFile")==b,"multi-selection and current photo retained");
        Check((string)Field("activeFilter")=="picked"&&(string)Field("gallerySortMode")=="name_desc"&&!(bool)Field("advanceToUndecided")&&(double)Field("gridTileWidth")==160,"filter sort browse thumbnail size retained");
        foreach(var n in new[]{"pickedFiles","jpgOnlyFiles","rawOnlyFiles","rejectedFiles"})Check(((HashSet<string>)Field(n)).SetEquals(new[]{Photo(n)}),"unchanged "+n);
        Check(((IDictionary)Field("thumbnailCache")).Contains(a)&&((IDictionary)Field("fullImageCache")).Contains(a),"unchanged image and thumbnail cache reused");
        Check(((IDictionary)Field("gpsDisplayCache")).Contains(other),"other directory GPS cache untouched");
        Set("gpsSession",new GpsSourceSession());Refresh();
        Check(selection.SetEquals(new[]{a,b})&&(string)Field("currentSingleFile")==b&&!(bool)Field("advanceToUndecided"),"first refresh after startup retains grid state before GPS session initialization");
        ExternalGps(b,true);Set("singleViewMode",true);Set("activeFilter","all");Call("RebuildUi");Set("singleZoom",1.75);Set("fitSingleImage",false);
        object viewer=Field("singleViewer"),navigator=Field("navigatorCanvas"),mainImage=Field("singleImage");Refresh();
        Check((double)Field("singleZoom")==1.75&&!(bool)Field("fitSingleImage"),"single zoom/fit preserved");
        Check(ReferenceEquals(viewer,Field("singleViewer"))&&ReferenceEquals(navigator,Field("navigatorCanvas"))&&ReferenceEquals(mainImage,Field("singleImage")),"unchanged single viewer navigator and image not rebuilt");
        Check(((TextBlock)Field("gpsInfo")).Tag!=null,"single GPS metadata refreshed");
        Set("gpsSession",new GpsSourceSession());Set("navigationRoot",null);Set("advanceToUndecided",false);Refresh();
        Check((string)Field("currentSingleFile")==b&&ReferenceEquals(viewer,Field("singleViewer"))&&(double)Field("singleZoom")==1.75&&!(bool)Field("advanceToUndecided"),"restored single view first refresh retains current photo zoom and browse mode before root initialization");
        // Deterministic old-completion injection plus real back-to-back refresh.
        int old=(int)Field("gpsDisplayGeneration");ExternalGps(a,true,41.1,26.2);Call("RefreshCurrentFolder");ExternalGps(a,false);Call("RefreshCurrentFolder");
        PumpUntil(delegate{return !(bool)Field("gpsDirectoryRefreshing");});
        var pending=(HashSet<string>)Field("gpsDisplayPending");pending.Add(a);
        var type=typeof(MainWindow).GetNestedType("GpsDisplay",BindingFlags.NonPublic);var stale=Activator.CreateInstance(type,true);type.GetField("Stamp",Flags).SetValue(stale,Call("GpsDisplayStamp",a));type.GetField("Gps",Flags).SetValue(stale,new GpsMetadata(1,2,null,null,a));
        Call("ApplyGpsDisplayResult",a,stale,old);Check(pending.Contains(a),"old completion cannot remove current pending token");pending.Remove(a);
        Check(Display(a)==null,"old GPS completion cannot overwrite newest refresh even equal identity");
        var map=(IDictionary)Field("gpsDisplayCache");Check(map.Contains(a),"latest generation stored explicit missing GPS result");
        File.SetLastWriteTimeUtc(a,File.GetLastWriteTimeUtc(a).AddSeconds(5));Refresh();
        Check(!((IDictionary)Field("thumbnailCache")).Contains(a)&&!((IDictionary)Field("fullImageCache")).Contains(a),"real identity change evicts only affected image caches");
        ExternalGps(b,true);Source(b);Call("SelectGpsRoot",root+"-other");Check(((GpsSourceSession)Field("gpsSession")).Source==null&&Field("gpsRefreshSourcePath")==null,"root switch forgets refresh source candidate");
        Call("ApplyGpsDisplayResult",a,stale,old);Check(Display(a)==null,"root switch rejects earlier metadata completion");
        Call("SelectGpsRoot",root);Refresh();
        selection.Add(a);selection.Add(b);File.Move(b,b+".removed-test-material");Refresh();
        Check((string)Field("currentSingleFile")==((List<string>)Field("galleryFiles")).First()&&(bool)Field("fitSingleImage"),"deleted current single photo follows original first-file/fit fallback");
        Check(!selection.Contains(b)&&selection.Contains(a),"deleted selection removed while surviving selection retained");
        // 500 metadata reads: enqueue must return without waiting for all reads,
        // and dispatcher heartbeat must run during the scan.
        for(int i=0;i<500;i++)ExternalGps(Photo("PERF"+i.ToString("D3")),i%2==0);
        bool heartbeat=false;var files=Directory.GetFiles(Path.Combine(root,"jpg"),"*.JPG").ToList();var timer=Stopwatch.StartNew();
        Call("BeginGpsDirectoryRefresh",root,files);long enqueue=timer.ElapsedMilliseconds;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal,new Action(delegate{heartbeat=(bool)Field("gpsDirectoryRefreshing");}));
        PumpUntil(delegate{return !(bool)Field("gpsDirectoryRefreshing");});
        Check(enqueue<1000&&heartbeat,"500 JPG scan is asynchronous; dispatcher heartbeat during refresh");
        Check(files.All(f=>map.Contains(f)),"all current JPG scanned, including non-visible files");
        Console.WriteLine("500 JPG metadata enqueue="+enqueue+" ms; total="+timer.ElapsedMilliseconds+" ms");
        Call("ClearGpsSource");Console.WriteLine("PASS "+count+" directory GPS refresh assertions");
    }
    [STAThread] static int Main(string[] args){try{root=Path.GetFullPath(args[0]);Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
