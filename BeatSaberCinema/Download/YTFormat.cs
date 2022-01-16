using System;
using Newtonsoft.Json.Linq;

namespace BeatSaberCinema
{
	// ReSharper disable once InconsistentNaming
	public class YTFormat
	{
		public string? Quality;
		public int? Height;
		public int? Width;
		public string? AudioCodec;
		public string? VideoCodec;
		public string? FileExtension;
		public string? URL;
		public float? FramesPerSecond;
		public long? FileSize;

		public YTFormat(JToken jToken)
		{
			if (jToken["acodec"] == null && jToken["vcodec"] == null)
			{
				throw new ArgumentException("Invalid format");
			}

			Quality = jToken["format_note"]?.Value<string?>();
			FileSize = jToken["filesize"]?.Value<long?>();
			Width = jToken["width"]?.Value<int?>();
			Height = jToken["height"]?.Value<int?>();
			AudioCodec = jToken["acodec"]?.Value<string?>();
			VideoCodec = jToken["vcodec"]?.Value<string?>();
			FileExtension = jToken["ext"]?.Value<string?>();
			URL = jToken["url"]?.Value<string?>();
			FramesPerSecond = jToken["fps"]?.Value<float?>();
		}
	}
}