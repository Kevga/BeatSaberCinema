using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeatSaberCinema
{
	public static class Util
	{
		public static bool IsFileLocked(FileInfo file)
		{
			try
			{
				using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
				return !stream.CanRead;
			}
			catch (IOException)
			{
				return true;
			}
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

		public static string FormatFloat(float f)
		{
			return $"{f:0.00}";
		}

		public static string ReplaceIllegalFilesystemChars(string s)
		{
			string regexSearch = new string(Path.GetInvalidFileNameChars()) + ".";
			Regex regex = new Regex($"[{Regex.Escape(regexSearch)}]");
			var result = regex.Replace(s, "_");
			return result;
		}

		public static string ShortenFilename(string path, string s)
		{
			//Max length is 260, the nul byte will take up one, file extension will take up four and ytdl file parts take up at least an additional five characters
			//https://docs.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation
			var allowedLength = 259 - path.Length - ".mp4".Length - ".fxxxx".Length;

			if (allowedLength >= s.Length)
			{
				return s;
			}

			if (allowedLength > 0)
			{
				return s.Substring(0, allowedLength);
			}

			Log.Warn("Video path length might be too long!");
			return s;
		}

		public static bool IsModInstalled(string modName, string? minimumVersion = null)
		{
			return IPA.Loader.PluginManager.EnabledPlugins.Any(x => x.Id == modName && (minimumVersion == null || x.HVersion >= new Hive.Versioning.Version(minimumVersion)));
		}

		public static Texture? LoadPNGFromResources(string resourcePath)
		{
			byte[] fileData = BeatSaberMarkupLanguage.Utilities.GetResource(Assembly.GetExecutingAssembly(), resourcePath);
			if (fileData.Length <= 0)
			{
				return null;
			}

			var tex = new Texture2D(2, 2);
			tex.LoadImage(fileData);
			return tex;
		}

		public static bool IsMultiplayer()
		{
			return MultiplayerPatch.IsMultiplayer;
		}

		public static string GetEnvironmentName()
		{
			var environmentName = "MainMenu";
			if (SceneManager.GetActiveScene().name != "GameCore")
			{
				return environmentName;
			}

			var sceneCount = SceneManager.sceneCount;
			for (var i = 0; i < sceneCount; i++)
			{
				var sceneName = SceneManager.GetSceneAt(i).name;
				if (!sceneName.EndsWith("Environment"))
				{
					continue;
				}

				environmentName = sceneName;
				break;
			}

			return environmentName;
		}
	}
}