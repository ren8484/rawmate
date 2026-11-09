using System;using System.Linq;using System.Reflection;using System.Runtime.Serialization;using System.Collections;using System.Collections.Generic;using System.Windows;using System.Windows.Controls;using System.Windows.Media;
internal static class GridHeaderLayoutTests {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Set(object s,string n,object v){typeof(MainWindow).GetField(n,F).SetValue(s,v);}
 static object Get(object s,string n){return typeof(MainWindow).GetField(n,F).GetValue(s);}
 static MainWindow State(){var s=(MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));foreach(var f in typeof(MainWindow).GetFields(F|BindingFlags.DeclaredOnly)){
  var t=f.FieldType;if(t==typeof(object))f.SetValue(s,new object());else if(!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null&&(typeof(DependencyObject).IsAssignableFrom(t)||(t.IsGenericType&&typeof(IEnumerable).IsAssignableFrom(t))))f.SetValue(s,Activator.CreateInstance(t));
 }Set(s,"gpsSession",new GpsSourceSession());Set(s,"activeFilter","all");Set(s,"gallerySortMode","capture_asc");Set(s,"gridTileWidth",196d);Set(s,"thumbnailLoadQueued",true);Set(s,"filmThumbnailLoadQueued",true);Set(s,"navigatorUpdateQueued",true);return s;}
 static IEnumerable<FrameworkElement> Walk(DependencyObject p){for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);var f=c as FrameworkElement;if(f!=null)yield return f;foreach(var x in Walk(c))yield return x;}}
 static Rect Bounds(FrameworkElement x,FrameworkElement r){return x.TransformToAncestor(r).TransformBounds(new Rect(0,0,x.ActualWidth,x.ActualHeight));}
 [STAThread]static int Main(){try{
  // Root client widths: includes physical min-window client width (987 DIP)
  // and the original offline 984/1000 failures. These are not DPI tests.
  var widths=Enumerable.Range(984,118).Select(x=>(double)x).Concat(new[]{1093d,1280d,1380d,1707d,1920d}).Distinct().ToArray();
  var captions=new[]{"筛选：全部照片 · 3/3 张 · 单击选择，双击单张浏览","筛选：全部照片 · 12345/12345 张 · 单击选择，双击单张浏览",new String('宽',2048)};
  int cases=0;foreach(var text in captions){var s=State();var root=(FrameworkElement)typeof(MainWindow).GetMethod("BuildUi",F).Invoke(s,null);var cap=(TextBlock)Get(s,"galleryCaption");cap.Text=text;
   var actions=Walk(root).OfType<StackPanel>().Single(x=>x.Children.OfType<Button>().Any(b=>Convert.ToString(b.ToolTip)=="选择网格缩略图大小"));var buttons=actions.Children.OfType<Button>().ToArray();
   Check(buttons.Length==5,"all five original actions retained");Check(buttons.Select(b=>Convert.ToString(b.ToolTip)).SequenceEqual(new[]{"筛选挑片标记","选择图库排序方式","选择网格缩略图大小","网格视图","单张视图"}),"original button order/tooltips");
   foreach(double width in widths){cases++;root.Measure(new Size(width,604.5));root.Arrange(new Rect(0,0,width,604.5));root.UpdateLayout();string at=" width="+width+" captionLength="+text.Length;
    Check(cap.Text==text,"caption data unchanged"+at);Check(cap.TextTrimming==TextTrimming.CharacterEllipsis&&cap.TextWrapping==TextWrapping.NoWrap,"single-line ellipsis"+at);
    var cr=Bounds(cap,root);Check(!Double.IsInfinity(cap.ActualWidth)&&cr.Width>=0,"finite caption"+at);Check(cr.Right<=Bounds(buttons[0],root).Left+.5,"summary cannot overlap actions"+at);
    var label=Walk(root).OfType<TextBlock>().Single(x=>x.Text=="图库");Check(cr.Left>=Bounds(label,root).Right,"summary cannot overlap title"+at);
    for(int i=0;i<buttons.Length;i++){var b=buttons[i];var r=Bounds(b,root);Check(r.Left>=0&&r.Right<=width+.5&&r.Top>=0&&r.Bottom<=604.5,"button entirely inside root "+i+at);Check(Math.Abs(b.ActualHeight-28)<.5,"button height retained"+at);if(i>=3)Check(Math.Abs(b.ActualWidth-32)<.5,"view button width retained"+at);if(i>0)Check(r.Left>=Bounds(buttons[i-1],root).Right,"buttons separate"+at);}
   }
  }Console.WriteLine("PASS: "+checks+" assertions; "+cases+" caption/width boundary models; no HWND or physical DPI claim.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
