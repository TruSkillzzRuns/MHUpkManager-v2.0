using System;
using Assimp;
class P {
  static void Main() {
    using var ctx = new AssimpContext();
    foreach (var f in ctx.GetSupportedExportFormats()) {
      Console.WriteLine($"{f.FormatId}|{f.Description}|{f.FileExtension}");
    }
  }
}
