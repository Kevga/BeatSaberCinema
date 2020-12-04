using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BeatSaberCinema
{
	public static class Util
	{
		public static bool IsFileLocked(FileInfo file)
		{
			try
			{
				FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
				stream.Close();
			}
			catch (IOException)
			{
				return true;
			}
			return false;
		}

		public static string FilterEmoji(string text)
		{
			//This is required because the game will crash with emojis in truncated text fields
			//Related issue: https://github.com/monkeymanboy/BeatSaberMarkupLanguage/issues/68
			//See https://stackoverflow.com/a/28025891 for an explanation of what this does
			return Regex.Replace(text, @"\p{Cs}", "");
		}

		public static string SecondsToString(int seconds)
		{
			var timeSpan = TimeSpan.FromSeconds(seconds);

			if (seconds > 60 * 60)
			{
				return timeSpan.Hours + ":" + $"{timeSpan.Minutes:00}" + ":" + $"{timeSpan.Seconds:00}";
			}

			return timeSpan.Minutes + ":" + $"{timeSpan.Seconds:00}";
		}

		public static string ReplaceIllegalFilesystemChars(string s)
		{
			string regexSearch = new string(Path.GetInvalidFileNameChars());
			Regex regex = new Regex($"[{Regex.Escape(regexSearch)}]");
			var result = regex.Replace(s, "_");
			return result;
		}
	}
}