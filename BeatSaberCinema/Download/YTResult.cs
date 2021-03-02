using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BeatSaberCinema
{
	// ReSharper disable once InconsistentNaming
	public class YTResult
	{
		public readonly string ID;
		public readonly string Title;
		public readonly string Author;
		public readonly int Duration;
		public readonly IEnumerable<YTFormat> Formats;
		public readonly YTFormat? HighestFormat;

		public YTResult(JObject result)
		{
			ID = result["id"]!.ToString();
			Title = result["title"]?.ToString() ?? "Untitled Video";
			Author = result["uploader"]?.ToString() ?? "Unknown Author";
			var duration = double.Parse(result["duration"]?.ToString() ?? "0");
			Duration = Convert.ToInt32(duration);
			Formats = ParseFormats(result["formats"]);
			HighestFormat = ParseFormats(result["requested_formats"]).FirstOrDefault(format => format.VideoCodec != null);
		}

		public bool QualityAvailable(string quality)
		{
			return Formats.Any(format => (format.Quality != null && format.Quality.Contains(quality)) ||
			                             format.Height == int.Parse(Regex.Match(quality, @"\d+").Value, NumberFormatInfo.InvariantInfo));
		}

		public string? GetHighestQuality()
		{
			return HighestFormat?.Quality;
		}

		public bool IsStillImage()
		{
			var format = HighestFormat;
			if (format == null)
			{
				return false;
			}

			return format.FramesPerSecond < 10 || format.Height == format.Width || Title.Contains(" - Topic");
		}

		public string? GetQualityString()
		{
			if (HighestFormat?.Quality == null)
			{
				return null;
			}

			var height = HighestFormat.Height;
			var resolution = HighestFormat.Quality;
			var match = Regex.Match(resolution, @"\d+").Value;
			if (!string.IsNullOrEmpty(match))
			{
				var parsedHeight = int.Parse(match, NumberFormatInfo.InvariantInfo);
				if (parsedHeight > 0)
				{
					height = parsedHeight;
				}
			}

			return height + "p " + HighestFormat?.FramesPerSecond + "fps";
		}

		private static IEnumerable<YTFormat> ParseFormats(JToken? formats)
		{
			var resultList = new List<YTFormat>();
			if (formats?.HasValues != true)
			{
				return resultList;
			}

			try
			{
				resultList.AddRange(formats.Select(format => new YTFormat(format)));
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}
			return resultList;
		}

		public new string ToString()
		{
			return $"[{ID}] {Title} by {Author} ({Duration})";
		}
	}
}