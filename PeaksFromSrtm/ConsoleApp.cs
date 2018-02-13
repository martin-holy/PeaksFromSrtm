using System;
using System.Collections.Generic;
using Brejc.Common.Console;

namespace PeaksFromSrtm {
  public class ConsoleApp : ConsoleApplicationBase {
    public ConsoleApp(string[] args) : base(args) { }

    public override IList<IConsoleApplicationCommand> ParseArguments() {
      if (Args.Length == 0)
        return null;

      var cmdList = new List<IConsoleApplicationCommand>();
      IConsoleApplicationCommand cmd = new PeaksFromSrtmCommand();

      cmd.ParseArgs(Args, 0);
      cmdList.Add(cmd);

      return cmdList;
    }

    public override void ShowBanner() {
      var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);
      Console.Out.WriteLine("PeaksFromSrtm v{0} by Martin Holy from Srtm2Osm by Igor Brejc and others", version.FileVersion);
      Console.Out.WriteLine();
      Console.Out.WriteLine("Uses SRTM data to find peaks");
      Console.Out.WriteLine();
    }

    public override void ShowHelp() {
      Console.Out.WriteLine();
      Console.Out.WriteLine("USAGE:");
      Console.Out.WriteLine("PeaksFromSrtm <bounds> <options>");
      Console.Out.WriteLine();
      Console.Out.WriteLine("BOUNDS (choose one):");
      Console.Out.WriteLine("-bounds1 <minLat> <minLng> <maxLat> <maxLng>: specifies the area to cover");
      Console.Out.WriteLine("-bounds2 <lat> <lng> <boxsize (km)>: specifies the area to cover");
      Console.Out.WriteLine("-bounds3 <slippymap link>: specifies the area to cover using the URL from a map");
      Console.Out.WriteLine("All bound parameters can be specified more than once.");
      Console.Out.WriteLine();
      Console.Out.WriteLine("OPTIONS:");
      Console.Out.WriteLine("-o <path>: specifies an output KML file (default: 'peaks.kml')");
      Console.Out.WriteLine("-d <path>: specifies a SRTM cache directory (default: 'Srtm')");
      Console.Out.WriteLine("-i: forces the regeneration of SRTM index file (default: no)");
      Console.Out.WriteLine("-corrxy <corrLng> <corrLat>: correction values to shift contours");
      Console.Out.WriteLine("-source <url>: base URL used for download");
      Console.Out.WriteLine("       (default 'http://dds.cr.usgs.gov/srtm/version2_1/SRTM3/')");
      Console.Out.WriteLine("-howmany <count>: how many peaks to return");
      Console.Out.WriteLine();
    }
  }
}
