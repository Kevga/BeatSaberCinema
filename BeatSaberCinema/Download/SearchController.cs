using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using IPA.Utilities.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatSaberCinema
{
	public class SearchController : YoutubeDLController
	{
		public readonly List<YTResult> SearchResults = new List<YTResult>();
		private Coroutine? _searchCoroutine;
		private Process? _searchProcess;

		public event Action<YTResult>? SearchProgress;
		public event Action? SearchFinished;

		public void Search(string query)
		{
			if (_searchCoroutine != null)
			{
				CoroutineStarter.Instance.StopCoroutine(_searchCoroutine);
			}

			_searchCoroutine = CoroutineStarter.Instance.StartCoroutine(SearchCoroutine(query));
		}

		private IEnumerator SearchCoroutine(string query, int expectedResultCount = 20)
		{
			if (IsProcessRunning(_searchProcess))
			{
				DisposeProcess(_searchProcess);
			}

			SearchResults.Clear();
			Log.Debug($"Starting search with query {query}");

			var searchProcessArguments = $"\"ytsearch{expectedResultCount}:{query}\"" +
			                             " -j" + //Instructs yt-dl to return json data without downloading anything
			                             " -i"; //Ignore errors

			_searchProcess = CreateProcess(searchProcessArguments);

			_searchProcess.OutputDataReceived += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { SearchProcessDataReceived(e); });

			_searchProcess.ErrorDataReceived += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { SearchProcessErrorDataReceived(e); });

			_searchProcess.Exited += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { SearchProcessExited(((Process) sender).ExitCode); });

			Log.Info($"Starting youtube-dl process with arguments: \"{_searchProcess.StartInfo.FileName}\" {_searchProcess.StartInfo.Arguments}");
			StartProcessThreaded(_searchProcess);
			var startProcessTimeout = new Timeout(10);
			yield return new WaitUntil(() => IsProcessRunning(_searchProcess) || startProcessTimeout.HasTimedOut);
			startProcessTimeout.Stop();

			var timeout = new Timeout(45);
			yield return new WaitUntil(() => !IsProcessRunning(_searchProcess) || timeout.HasTimedOut);
			timeout.Stop();

			SearchFinished?.Invoke();
			DisposeProcess(_searchProcess);
		}

		private static void SearchProcessErrorDataReceived(DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				return;
			}

			Log.Error("youtube-dl process error:");
			Log.Error(e.Data);
		}

		private void SearchProcessDataReceived(DataReceivedEventArgs e)
		{
			var output = e.Data.Trim();
			if (string.IsNullOrWhiteSpace(output))
			{
				return;
			}

			if (output.Contains("yt command exited"))
			{
				Log.Debug("Done with Youtube Search, Processing...");
				return;
			}

			if (output.Contains("yt command"))
			{
				Log.Debug($"Running with {output}");
				return;
			}

			var trimmedLine = output;
			var ytResult = ParseSearchResult(trimmedLine);
			if (ytResult == null)
			{
				return;
			}

			SearchResults.Add(ytResult);
			SearchProgress?.Invoke(ytResult);
		}

		private static YTResult? ParseSearchResult(string searchResultJson)
		{
			if (!(JsonConvert.DeserializeObject(searchResultJson) is JObject result))
			{
				Log.Error("Failed to deserialize " + searchResultJson);
				return null;
			}

			if (result["id"] == null)
			{
				Log.Warn("YT search result had no ID, skipping");
				return null;
			}

			var ytResult = new YTResult(result);
			return ytResult;
		}

		private void SearchProcessExited(int exitCode)
		{
			Log.Info($"Search process exited with exitcode {exitCode}");
			SearchFinished?.Invoke();
			DisposeProcess(_searchProcess);
			_searchProcess = null;
		}

		internal void StopSearch()
		{
			DisposeProcess(_searchProcess);
		}
	}
}