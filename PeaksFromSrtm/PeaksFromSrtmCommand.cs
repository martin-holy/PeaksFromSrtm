using System;
using System.Collections.Generic;
using Brejc.Common.Console;
using Brejc.DemLibrary;
using System.IO;
using System.Web;
using Brejc.Geometry;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;

namespace PeaksFromSrtm {
  public enum PeaksFromSrtmCommandOption {
    Bounds1,
    Bounds2,
    Bounds3,
    OutputFile,
    SrtmCachePath,
    RegenerateIndexFile,
    CorrectionXY,
    HowMany,
    SrtmSource
  }

  public class PeaksFromSrtmCommand : IConsoleApplicationCommand {
    #region IConsoleApplicationCommand Members

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public void Execute() {
      var activityLogger = new ConsoleActivityLogger {LogLevel = ActivityLogLevel.Verbose};

      // Use all available encryption protocols supported in the .NET Framework 4.0.
      // TLS versions > 1.0 are supported and available via the extensions.
      // see https://blogs.perficient.com/microsoft/2016/04/tsl-1-2-and-net-support/
      // This is a global setting for all HTTP requests.
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolTypeExtensions.Tls11 |
                                             SecurityProtocolTypeExtensions.Tls12 | SecurityProtocolType.Ssl3;

      // first make sure that the SRTM directory exists
      if (!Directory.Exists(_srtmDir))
        Directory.CreateDirectory(_srtmDir);

      var srtmIndexFilename = Path.Combine(_srtmDir, "SrtmIndex.dat");
      SrtmIndex srtmIndex = null;
      SrtmIndex.SrtmSource = _srtmSource;

      try {
        srtmIndex = SrtmIndex.Load(srtmIndexFilename);
      }
      catch (Exception) {
        // in case of exception, regenerate the index
        _generateIndex = true;
      }

      if (_generateIndex) {
        srtmIndex = new SrtmIndex {ActivityLogger = activityLogger};
        srtmIndex.Generate();
        srtmIndex.Save(srtmIndexFilename);

        srtmIndex = SrtmIndex.Load(srtmIndexFilename);
      }

      Srtm3Storage.SrtmSource = _srtmSource;
      var storage = new Srtm3Storage(Path.Combine(_srtmDir, "SrtmCache"), srtmIndex) {ActivityLogger = activityLogger};
      var peaks = new List<PointOfInterest>();

      foreach (var bound in _bounds) {
        var corrBounds = new Bounds2(bound.MinX - _corrX, bound.MinY - _corrY,
          bound.MaxX - _corrX, bound.MaxY - _corrY);

        activityLogger.LogFormat(ActivityLogLevel.Normal, "Calculating contour data for bound {0}...", corrBounds);

        var dem = (IRasterDigitalElevationModel) storage?.LoadDemForArea(corrBounds);
        peaks.AddRange(ElevationAnalyzer.FindPeaks(dem, _howMany));

        // clear up some memory used in storage object
        if (_bounds.Count == 1) {
          storage = null;
          GC.Collect();
        }

        var statistics = dem?.CalculateStatistics();

        activityLogger.Log(ActivityLogLevel.Normal, string.Format(CultureInfo.InvariantCulture,
          "DEM data points count: {0}", dem?.DataPointsCount));
        activityLogger.Log(ActivityLogLevel.Normal, string.Format(CultureInfo.InvariantCulture,
          "DEM minimum elevation: {0}", statistics?.MinElevation));
        activityLogger.Log(ActivityLogLevel.Normal, string.Format(CultureInfo.InvariantCulture,
          "DEM maximum elevation: {0}", statistics?.MaxElevation));
        activityLogger.Log(ActivityLogLevel.Normal, string.Format(CultureInfo.InvariantCulture,
          "DEM has missing points: {0}", statistics?.HasMissingPoints));
      }


      activityLogger.Log(ActivityLogLevel.Normal, "Saving Peaks to file...");
      using (var fs = new FileStream(_outputKmlFile, FileMode.Create, FileAccess.Write))
      using (var sw = new StreamWriter(fs)) {
        sw.WriteLine(
          "<?xml version=\"1.0\" encoding=\"utf-8\"?> <kml xmlns=\"http://earth.google.com/kml/2.2\" ><Document>");
        foreach (var peak in peaks) {
          sw.WriteLine("<Placemark><name>{0}</name><Point><coordinates>{1},{2}</coordinates></Point></Placemark>",
            peak.Text, peak.Position.Longitude.ToString(CultureInfo.InvariantCulture).Replace(',', '.'),
            peak.Position.Latitude.ToString(CultureInfo.InvariantCulture).Replace(',', '.'));
        }

        sw.WriteLine("</Document></kml>");
      }


      activityLogger.Log(ActivityLogLevel.Normal, "Done.");
    }

    public int ParseArgs(string[] args, int startFrom) {
      if (args == null)
        throw new ArgumentNullException(nameof(args));

      var options = new SupportedOptions();
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.Bounds1, "bounds1", 4));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.Bounds2, "bounds2", 3));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.Bounds3, "bounds3", 1));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.OutputFile, "o", 1));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.SrtmCachePath, "d", 1));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.RegenerateIndexFile, "i"));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.CorrectionXY, "corrxy", 2));
      options.AddOption(new ConsoleApplicationOption((int) PeaksFromSrtmCommandOption.SrtmSource, "source", 1));
      options.AddOption(new ConsoleApplicationOption((int)PeaksFromSrtmCommandOption.HowMany, "howmany", 1));

      startFrom = options.ParseArgs(args, startFrom);
      var invariantCulture = CultureInfo.InvariantCulture;

      foreach (var option in options.UsedOptions) {
        switch ((PeaksFromSrtmCommandOption) option.OptionId) {
          case PeaksFromSrtmCommandOption.CorrectionXY: {
            _corrX = double.Parse(option.Parameters[0], invariantCulture);
            _corrY = double.Parse(option.Parameters[1], invariantCulture);
            continue;
          }

          case PeaksFromSrtmCommandOption.SrtmSource: {
            Uri uri;

            try {
              uri = new Uri(option.Parameters[0]);
            }
            catch (UriFormatException) {
              throw new ArgumentException("The source URL is not valid.");
            }

            // Check if the prefix is supported. Unfortunately I couldn't find a method to check which
            // prefixes are registered without calling WebRequest.Create(), which I didn't want here.
            if (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ftp") {
              var error = string.Format(invariantCulture, "The source's scheme ('{0}') is not supported.",
                uri.Scheme);
              throw new ArgumentException(error);
            }

            _srtmSource = uri.AbsoluteUri;

            continue;
          }

          case PeaksFromSrtmCommandOption.Bounds1: {
            var minLat = double.Parse(option.Parameters[0], invariantCulture);
            var minLng = double.Parse(option.Parameters[1], invariantCulture);
            var maxLat = double.Parse(option.Parameters[2], invariantCulture);
            var maxLng = double.Parse(option.Parameters[3], invariantCulture);

            if (minLat == maxLat)
              throw new ArgumentException("Minimum and maximum latitude should not have the same value.");

            if (minLng == maxLng)
              throw new ArgumentException("Minimum and maximum longitude should not have the same value.");

            if (minLat > maxLat) {
              var sw = minLat;
              minLat = maxLat;
              maxLat = sw;
            }

            if (minLng > maxLng) {
              var sw = minLng;
              minLng = maxLng;
              maxLng = sw;
            }

            if (minLat <= -90 || maxLat > 90)
              throw new ArgumentException("Latitude is out of range.");

            if (minLng <= -180 || maxLng > 180)
              throw new ArgumentException("Longitude is out of range.");

            _bounds.Add(new Bounds2(minLng, minLat, maxLng, maxLat));
            continue;
          }

          case PeaksFromSrtmCommandOption.Bounds2: {
            var lat = double.Parse(option.Parameters[0], invariantCulture);
            var lng = double.Parse(option.Parameters[1], invariantCulture);
            var boxSizeInKilometers = double.Parse(option.Parameters[2], invariantCulture);

            _bounds.Add(CalculateBounds(lat, lng, boxSizeInKilometers));
            continue;
          }

          case PeaksFromSrtmCommandOption.Bounds3: {
            var slippyMapUrl = new Uri(option.Parameters[0]);
            double lat;
            double lng;
            int zoomLevel;

            if (slippyMapUrl.Fragment != string.Empty) {
              // map=18/50.07499/10.21574
              const string pattern = @"map=(\d+)/([-\.\d]+)/([-\.\d]+)";
              var match = Regex.Match(slippyMapUrl.Fragment, pattern);

              if (match.Success) {
                try {
                  zoomLevel = int.Parse(match.Groups[1].Value, invariantCulture);
                  lat = double.Parse(match.Groups[2].Value, invariantCulture);
                  lng = double.Parse(match.Groups[3].Value, invariantCulture);
                }
                catch (FormatException fex) {
                  throw new ArgumentException("Invalid slippymap URL.", fex);
                }

                _bounds.Add(CalculateBounds(lat, lng, zoomLevel));
              }
              else
                throw new ArgumentException("Invalid slippymap URL.");
            }
            else if (slippyMapUrl.Query != string.Empty) {
              var queryPart = slippyMapUrl.Query;
              var queryParameters = HttpUtility.ParseQueryString(queryPart);

              if (queryParameters["lat"] != null
                  && queryParameters["lon"] != null
                  && queryParameters["zoom"] != null) {
                try {
                  lat = double.Parse(queryParameters["lat"], invariantCulture);
                  lng = double.Parse(queryParameters["lon"], invariantCulture);
                  zoomLevel = int.Parse(queryParameters["zoom"], invariantCulture);
                }
                catch (FormatException fex) {
                  throw new ArgumentException("Invalid slippymap URL.", fex);
                }

                _bounds.Add(CalculateBounds(lat, lng, zoomLevel));
              }
              else if (queryParameters["bbox"] != null)
                _bounds.Add(CalculateBounds(queryParameters["bbox"]));
              else
                throw new ArgumentException("Invalid slippymap URL.");
            }
            else
              throw new ArgumentException("Invalid slippymap URL.");

            continue;
          }

          case PeaksFromSrtmCommandOption.OutputFile:
            _outputKmlFile = option.Parameters[0];
            continue;

          case PeaksFromSrtmCommandOption.SrtmCachePath:
            _srtmDir = option.Parameters[0];
            continue;

          case PeaksFromSrtmCommandOption.RegenerateIndexFile:
            _generateIndex = true;
            continue;
          case PeaksFromSrtmCommandOption.HowMany:
            _howMany = int.Parse(option.Parameters[0], invariantCulture);
            continue;
        }
      }

      // Check if bounds were specified
      if (_bounds.Count == 0)
        throw new ArgumentException("No bounds specified.");

      return startFrom;
    }

    #endregion

    private static Bounds2 CalculateBounds(double lat, double lng, int zoomLevel) {
      if (zoomLevel < 2 || zoomLevel >= ZoomLevels.Length)
        throw new ArgumentException("Zoom level is out of range.");

      // 30 is the width of the screen in centimeters
      var boxSizeInKilometers = ZoomLevels[zoomLevel] * 30.0 / 100 / 1000;

      return CalculateBounds(lat, lng, boxSizeInKilometers);
    }

    private static Bounds2 CalculateBounds(double lat, double lng, double boxSizeInKilometers) {
      if (boxSizeInKilometers <= 0)
        throw new ArgumentException("Box size must be a positive number.");

      // calculate deltas for the given kilometers
      const int earthRadius = 6360000;
      const double earthCircumference = earthRadius * 2 * Math.PI;
      var latDelta = boxSizeInKilometers / 2 * 1000 / earthCircumference * 360;
      var lngDelta = latDelta / Math.Cos(lat * Math.PI / 180.0);

      var minLng = lng - lngDelta / 2;
      var minLat = lat - latDelta / 2;
      var maxLng = lng + lngDelta / 2;
      var maxLat = lat + latDelta / 2;

      if (minLat <= -90 || maxLat > 90)
        throw new ArgumentException("Latitude is out of range.");

      if (minLng <= -180 || maxLng > 180)
        throw new ArgumentException("Longitude is out of range.");

      return new Bounds2(minLng, minLat, maxLng, maxLat);
    }

    private static Bounds2 CalculateBounds(string bbox) {
      if (string.IsNullOrEmpty(bbox))
        throw new ArgumentException("String is NULL or empty.", nameof(bbox));

      var parts = bbox.Split(',');

      if (parts.Length != 4)
        throw new ArgumentException("Bounding box has not exactly four parts.", nameof(bbox));

      double minLat, maxLat, minLng, maxLng;

      try {
        minLng = double.Parse(parts[0], CultureInfo.InvariantCulture);
        minLat = double.Parse(parts[1], CultureInfo.InvariantCulture);
        maxLng = double.Parse(parts[2], CultureInfo.InvariantCulture);
        maxLat = double.Parse(parts[3], CultureInfo.InvariantCulture);
      }
      catch (FormatException fex) {
        throw new ArgumentException("Bounding box was not parseable.", fex);
      }

      if (minLat <= -90 || maxLat > 90)
        throw new ArgumentException("Latitude is out of range.");

      if (minLng <= -180 || maxLng > 180)
        throw new ArgumentException("Longitude is out of range.");

      return new Bounds2(minLng, minLat, maxLng, maxLat);
    }

    private readonly List<Bounds2> _bounds = new List<Bounds2>();
    private double _corrX, _corrY;
    private bool _generateIndex;
    private string _srtmDir = "srtm";
    private string _outputKmlFile = "peaks.kml";
    private string _srtmSource = "";
    private int _howMany = int.MaxValue;

    private static readonly int[] ZoomLevels = {
      0, 0, 111000000, 55000000, 28000000, 14000000, 7000000, 3000000, 2000000, 867000,
      433000, 217000, 108000, 54000, 27000, 14000, 6771, 3385, 1693
    };
  }
}
