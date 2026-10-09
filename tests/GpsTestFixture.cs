using System;
using System.IO;
using System.Collections.Generic;

// Test-only historical fixture identity. No discovery or writes to baseline inputs.
internal static class GpsTestFixture {
 internal static readonly Dictionary<string,string> HistoricalHashes=new Dictionary<string,string> {
  {"jpg/DSC01265.JPG","38803BB9FB5B4BB8797CF2F0D4A765600EE063A9A91D213862227DB80B004AE5"},
  {"jpg/DSC01267.JPG","A33EEF858A42D9F4B7E03ED0098CE1EC3137372ED74B72555CA58A89AF38FA31"},
  {"jpg/DSC01270.JPG","A3D0ED1D95ECEB5859FF137C2374DEC698ADD93AC06E92F6DB6C3B8CF6F35C31"},
  {"arw/DSC01270.ARW","A1A611B4178008F0FCE8E1E3DC9FAF46F407F3030F9E4821206007B2AFBAFFAE"}
 };
 internal static string Baseline(string root) {
  root=Path.GetFullPath(root);Working(root);
  foreach(var item in HistoricalHashes) {
   string file=Path.Combine(root,item.Key.Replace('/',Path.DirectorySeparatorChar));
   if(!File.Exists(file)||GpsFileSafety.Hash(file).Replace("-","")!=item.Value)
    throw new IOException("Historical frozen baseline missing/changed: "+file);
  }
  return root;
 }
 internal static string Working(string root) {
  root=Path.GetFullPath(root);
  if(!root.StartsWith("C:\\rawmate\\test-output\\",StringComparison.OrdinalIgnoreCase))
   throw new IOException("Test mutation/output must stay within test-output: "+root);
  GpsFileSafety.CheckPath(root);
  return root;
 }
}
