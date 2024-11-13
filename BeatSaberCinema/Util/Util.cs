using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BeatSaberCinema.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

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
			var regexSearch = new string(Path.GetInvalidFileNameChars()) + ".";
			var regex = new Regex($"[{Regex.Escape(regexSearch)}]");
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

		public static Texture? LoadPNGFromResources(string resourcePath)
		{
			var fileData = BeatSaberMarkupLanguage.Utilities.GetResource(Assembly.GetExecutingAssembly(), resourcePath);
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

		public static bool IsInEditor()
		{
			var isInEditor = false;
			for (var i = 0; i < SceneManager.sceneCount; i++)
			{
				var sceneName = SceneManager.GetSceneAt(i).name;
				if (sceneName != "BeatmapLevelEditorWorldUi" && sceneName != "BeatmapEditor3D")
				{
					continue;
				}

				isInEditor = true;
				break;
			}

			return isInEditor;
		}

		public static string GetEnvironmentName()
		{
			var environmentName = "MainMenu";
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

		public static string GetHardwareInfo()
		{
			var info = new List<string>();
			try
			{
				info.Add("OS: "+SystemInfo.operatingSystem);
				info.Add("Graphics device: "+SystemInfo.graphicsDeviceName);
				info.Add("Processor: "+SystemInfo.processorType);
				info.Add("Device type: "+Enum.GetName(typeof(DeviceType), SystemInfo.deviceType));
				info.Add("VR device: "+XRSettings.loadedDeviceName);
				info.Add("VR active: "+XRSettings.isDeviceActive);
				info.Add("System Language: "+(Application.systemLanguage == SystemLanguage.English ? "English" : "Other"));
			}
			catch (Exception e)
			{
				Log.Error(e);
			}

			return string.Join("\n", info);
		}
	}
}