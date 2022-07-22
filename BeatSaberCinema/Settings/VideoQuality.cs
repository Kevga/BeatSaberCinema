using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BeatSaberCinema
{
	public static class VideoQuality
	{
		public enum Mode {
			/*Q2160P = 2160,
			Q1440P = 1440,*/
			Q1080P = 1080,
			Q720P  = 720,
			Q480P  = 480
		}

		public static string ToName(Mode mode)
		{
			return (int) mode + "p";
		}

		public static string ToYoutubeDLFormat(VideoConfig config, Mode quality)
		{
			string? qualityString;
			if (config.videoUrl == null || config.videoUrl.StartsWith("https://www.youtube.com/watch"))
			{
				qualityString = $"bestvideo[height<={(int) quality}][vcodec*=avc1]+bestaudio[acodec*=mp4]";
			}
			else if (config.videoUrl.StartsWith("https://vimeo.com/"))
			{
				qualityString = $"bestvideo[height<={(int) quality}][vcodec*=avc1]+bestaudio[acodec*=mp4]";
			}
			else if (config.videoUrl.StartsWith("https://www.bilibili.com"))
			{
				qualityString = $"bestvideo[height<={(int) quality}][vcodec*=avc1]+bestaudio[acodec*=mp4]";
			}
			else if (config.videoUrl.StartsWith("https://www.facebook.com"))
			{
				qualityString = "mp4";
			}
			else
			{
				qualityString = $"best[height<={(int) quality}][vcodec*=avc1]";
			}

			return qualityString;
		}

		public static Mode FromName(string mode)
		{
			var match = Regex.Match(mode, "^([0-9]{3,4})p.*");
			if (!match.Success)
			{
				throw new ArgumentException("Video config is missing the video URL");
			}
			var verticalResString = match.Groups[1].Value;
			if (verticalResString == null)
			{
				throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
			}

			var verticalResInt = int.Parse(verticalResString);
			return (Mode) verticalResInt;
		}

		public static List<object> GetModeList()
		{
			var enumArray = Enum.GetValues(typeof(Mode));
			var enumArrayFormatted = new object[enumArray.Length];
			for (var i=0; i<enumArray.Length; i++)
			{
				enumArrayFormatted[i] = ToName((Mode) enumArray.GetValue(i));
			}

			//Sort by descending quality
			var list = enumArrayFormatted.ToList();
			list.Reverse();
			return list;
		}
	}
}