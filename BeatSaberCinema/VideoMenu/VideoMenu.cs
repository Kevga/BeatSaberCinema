using System;
using System.Collections;
using System.Collections.Generic;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Parser;
using BS_Utils.Utilities;
using HMUI;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// ReSharper disable ArrangeMethodOrOperatorBody
namespace BeatSaberCinema
{
	public class VideoMenu: PersistentSingleton<VideoMenu>
	{
		[UIObject("root-object")] private readonly GameObject _root = null!;
		[UIComponent("no-video-bg")] private readonly RectTransform _noVideoViewRect = null!;
		[UIComponent("video-details")] private readonly RectTransform _videoDetailsViewRect = null!;
		[UIComponent("video-search-results")] private readonly RectTransform _videoSearchResultsViewRect = null!;
		[UIComponent("video-list")] private readonly CustomListTableData _customListTableData = null!;
		[UIComponent("search-results-loading")] private readonly TextMeshProUGUI _searchResultsLoadingText = null!;
		[UIComponent("search-keyboard")] private readonly ModalKeyboard _searchKeyboard = null!;
		[UIComponent("video-title")] private readonly TextMeshProUGUI _videoTitleText = null!;
		[UIComponent("no-video-text")] private readonly TextMeshProUGUI _noVideoText = null!;
		[UIComponent("video-author")] private readonly TextMeshProUGUI _videoAuthorText = null!;
		[UIComponent("video-duration")] private readonly TextMeshProUGUI _videoDurationText = null!;
		[UIComponent("video-status")] private readonly TextMeshProUGUI _videoStatusText = null!;
		[UIComponent("video-offset")] private readonly TextMeshProUGUI _videoOffsetText = null!;
		[UIComponent("video-thumbnail")] private readonly Image _videoThumnnail = null!;
		[UIComponent("preview-button")] private readonly TextMeshProUGUI _previewButtonText = null!;
		[UIComponent("preview-button")] private readonly Button _previewButton = null!;
		[UIComponent("search-button")] private readonly Button _searchButton = null!;
		[UIComponent("delete-config-button")] private readonly Button _deleteButton = null!;
		[UIComponent("delete-video-button")] private readonly Button _deleteVideoButton = null!;
		[UIComponent("delete-video-button")] private readonly TextMeshProUGUI _deleteVideoButtonText = null!;
		[UIComponent("download-button")] private readonly Button _downloadButton = null!;
		[UIComponent("refine-button")] private readonly Button _refineButton = null!;

		[UIParams] private readonly BSMLParserParams _bsmlParserParams = null!;

		private VideoMenuStatus _menuStatus = null!;
		private bool _videoMenuInitialized;

		private IPreviewBeatmapLevel? _currentLevel;
		private VideoConfig? _currentVideo;
		private bool _videoMenuActive;
		private int _selectedCell;
		private readonly DownloadController _downloadController = new DownloadController();
		private readonly List<DownloadController.YTResult> _searchResults = new List<DownloadController.YTResult>();

		public void Init()
		{
			//This needs to be reinitialized every time a fresh menu scene load happens
			_menuStatus = _root.AddComponent<VideoMenuStatus>();
			_menuStatus.DidEnable += StatusViewerDidEnable;
			_menuStatus.DidDisable += StatusViewerDidDisable;

			_deleteButton.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

			if (_videoMenuInitialized)
			{
				return;
			}
			_videoMenuInitialized = true;
			_videoDetailsViewRect.gameObject.SetActive(false);
			_videoSearchResultsViewRect.gameObject.SetActive(false);

			BSEvents.levelSelected += HandleDidSelectLevel;

			_downloadController.SearchProgress += SearchProgress;
			_downloadController.DownloadProgress += UpdateStatusText;
			_downloadController.DownloadFinished += OnDownloadFinished;

			if (!_downloadController.LibrariesAvailable())
			{
				Plugin.Logger.Warn("One or more of the libraries are missing. Downloading will most likely not work.");
			}
		}

		public void AddTab()
		{
			Plugin.Logger.Debug("Adding tab");
			GameplaySetup.instance.AddTab("Cinema", "BeatSaberCinema.VideoMenu.Views.video-menu.bsml", this);
		}

		public void RemoveTab()
		{
			Plugin.Logger.Debug("Removing tab");
			GameplaySetup.instance.RemoveTab("Cinema");
		}

		public void ResetVideoMenu()
		{
			_noVideoViewRect.gameObject.SetActive(true);
			_videoDetailsViewRect.gameObject.SetActive(false);
			SetButtonState(false);

			if (!_downloadController.LibrariesAvailable())
			{
				_noVideoText.text = "Libraries not found. Please reinstall Cinema.";
				return;
			}

			if (_currentLevel == null)
			{
				_noVideoText.text = "No level selected";
				return;
			}

			if (_currentLevel.GetType() == typeof(PreviewBeatmapLevelSO))
			{
				_noVideoText.text = "DLC songs are currently not supported.";
				return;
			}

			_noVideoText.text = "No video configured";
		}

		private void SetButtonState(bool state)
		{
			_previewButton.interactable = state;
			_deleteButton.interactable = state;
			_deleteVideoButton.interactable = state;
			_searchButton.interactable = (_currentLevel != null &&
			                              _currentLevel.GetType() != typeof(PreviewBeatmapLevelSO) &&
			                              _downloadController.LibrariesAvailable());
			_previewButtonText.text = PlaybackController.Instance.IsPreviewPlaying ? "Stop preview" : "Preview";

			if (_currentVideo == null)
			{
				return;
			}

			switch (_currentVideo.DownloadState)
			{
				case DownloadState.Downloading:
					_deleteVideoButtonText.SetText("Cancel");
					_previewButton.interactable = false;
					_deleteVideoButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
					break;
				case DownloadState.NotDownloaded:
				case DownloadState.Cancelled:
					_deleteVideoButtonText.SetText("Download");
					_deleteVideoButton.interactable = false;
					var underlineColor = Color.clear;
					if (state && _downloadController.LibrariesAvailable())
					{
						underlineColor = Color.green;
						_deleteVideoButton.interactable = true;
					}
					_deleteVideoButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = underlineColor;
					_previewButton.interactable = false;
					break;
				default:
					_deleteVideoButtonText.SetText("Delete Video");
					_deleteVideoButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
					_previewButton.interactable = state;
					break;
			}
		}

		public void SetupVideoDetails()
		{
			_videoSearchResultsViewRect.gameObject.SetActive(false);

			if (_currentVideo == null || !_videoMenuActive)
			{
				ResetVideoMenu();
				return;
			}

			_noVideoViewRect.gameObject.SetActive(false);
			_videoDetailsViewRect.gameObject.SetActive(true);

			SetButtonState(true);

			_videoTitleText.text = Util.FilterEmoji(_currentVideo.title ?? "Untitled Video");
			_videoAuthorText.text = "Author: "+Util.FilterEmoji(_currentVideo.author ?? "Unknown Author");
			_videoDurationText.text = "Duration: "+Util.SecondsToString(_currentVideo.duration);

			_videoOffsetText.text = $"{_currentVideo.offset:n0}" + " ms";
			_videoThumnnail.SetImage($"https://i.ytimg.com/vi/{_currentVideo.videoID}/hqdefault.jpg");

			UpdateStatusText(_currentVideo);
		}

		public void UpdateStatusText(VideoConfig videoConfig)
		{
			if (videoConfig != _currentVideo || !_videoMenuActive)
			{
				return;
			}

			if (videoConfig.DownloadState == DownloadState.Downloaded)
			{
				_videoStatusText.text = "Downloaded";
				_videoStatusText.color = Color.green;
			}
			else if (videoConfig.DownloadState == DownloadState.Downloading)
			{
				_videoStatusText.text = $"Downloading ({Convert.ToInt32(videoConfig.DownloadProgress*100).ToString()}%)";
				_videoStatusText.color = Color.yellow;
				_previewButton.interactable = false;
			}
			else if (videoConfig.DownloadState == DownloadState.NotDownloaded)
			{
				_videoStatusText.text = "Not downloaded";
				_videoStatusText.color = Color.red;
				_previewButton.interactable = false;
			} else if (videoConfig.DownloadState == DownloadState.Cancelled)
			{
				_videoStatusText.text = "Download cancelled";
				_videoStatusText.color = Color.red;
				_previewButton.interactable = false;
			}
		}

		public void HandleDidSelectLevel(LevelCollectionViewController sender, IPreviewBeatmapLevel level)
		{
			PlaybackController.Instance.StopPreview(true);

			if (_currentVideo?.NeedsToSave == true)
			{
				VideoLoader.SaveVideoConfig(_currentVideo);
			}

			_currentLevel = level;
			_currentVideo = VideoLoader.GetConfigForLevel(level);
			PlaybackController.Instance.SetSelectedLevel(level, _currentVideo);
			SetupVideoDetails();
		}

		public void StatusViewerDidEnable(object sender, EventArgs e)
		{
			_videoMenuActive = true;
			PlaybackController.Instance.StopPreview(false);
			SetupVideoDetails();
		}

		public void StatusViewerDidDisable(object sender, EventArgs e)
		{
			_videoMenuActive = false;
			if (_currentVideo?.NeedsToSave == true)
			{
				VideoLoader.SaveVideoConfig(_currentVideo);
			}

			try
			{
				PlaybackController.Instance.StopPreview(true);
				PlaybackController.Instance.HideScreen();
			}
			catch (Exception exception)
			{
				//This can happen when closing the game
				Plugin.Logger.Debug(exception);
			}
		}

		private void ApplyOffset(int offset)
		{
			if (_currentVideo == null)
			{
				return;
			}
			_currentVideo.offset += offset;
			_videoOffsetText.text = $"{_currentVideo.offset:n0}" + " ms";
			_currentVideo.NeedsToSave = true;
			PlaybackController.Instance.ApplyOffset(offset);
		}

		[UIAction("on-search-action")]
		[UsedImplicitly]
		public void SearchAction()
		{
			if (_currentLevel == null)
			{
				Plugin.Logger.Warn("Selected level was null on search action");
				return;
			}
			OnQueryAction(_currentLevel.songName + " - " + _currentLevel.songAuthorName);
		}

		private IEnumerator UpdateSearchResults(DownloadController.YTResult result)
		{
			var title = $"[{Util.SecondsToString(result.Duration)}] {Util.FilterEmoji(result.Title)}";
			var description = $"{Util.FilterEmoji(result.Author)}";
			var item = new CustomListTableData.CustomCellInfo(title, description);
			var request = UnityWebRequestTexture.GetTexture($"https://i.ytimg.com/vi/{result.ID}/mqdefault.jpg");
			yield return request.SendWebRequest();
			if (!request.isNetworkError && !request.isHttpError)
			{
				var tex = ((DownloadHandlerTexture) request.downloadHandler).texture;
				item.icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f, 100, 1);
			}
			else
			{
				Plugin.Logger.Debug(request.error);
			}

			_customListTableData.data.Add(item);
			_customListTableData.tableView.ReloadData();

			_refineButton.interactable = true;
			_downloadButton.interactable = true;
			_downloadButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.green;
			if (_selectedCell == -1)
			{
				_selectedCell = 0;
			}
			_searchResultsLoadingText.gameObject.SetActive(false);
		}

		private void OnDownloadFinished(VideoConfig video)
		{
			if (_currentVideo == video)
			{
				PlaybackController.Instance.PrepareVideo(video);
			}
			UpdateStatusText(video);
		}

		public void ShowKeyboard()
		{
			if (_currentLevel == null)
			{
				Plugin.Logger.Warn("Selected level was null on search action");
				return;
			}
			_searchKeyboard.SetText(_currentLevel.songName + " - " + _currentLevel.songAuthorName);
			_bsmlParserParams.EmitEvent("show-keyboard");
		}

		[UIAction("on-refine-action")]
		[UsedImplicitly]
		private void OnRefineAction()
		{
			ShowKeyboard();
		}

		[UIAction("on-delete-video-action")]
		[UsedImplicitly]
		private void OnDeleteVideoAction()
		{
			if (_currentVideo == null)
			{
				Plugin.Logger.Warn("Current video was null on delete action");
				return;
			}

			PlaybackController.Instance.StopPreview(true);

			switch (_currentVideo.DownloadState)
			{
				case DownloadState.Downloading:
					_downloadController.CancelDownload(_currentVideo);
					break;
				case DownloadState.NotDownloaded:
				case DownloadState.Cancelled:
					_downloadController.StartDownload(_currentVideo);
					break;
				default:
					VideoLoader.DeleteVideo(_currentVideo);
					break;
			}

			UpdateStatusText(_currentVideo);
			SetButtonState(true);
		}

		[UIAction("on-delete-config-action")]
		[UsedImplicitly]
		private void OnDeleteConfigAction()
		{
			if (_currentVideo == null)
			{
				return;
			}

			PlaybackController.Instance.StopPreview(true);

			if (_currentVideo.DownloadState == DownloadState.Downloading)
			{
				_downloadController.CancelDownload(_currentVideo);
			}

			VideoLoader.DeleteVideo(_currentVideo);
			var success = VideoLoader.DeleteConfig(_currentVideo);
			if (success)
			{
				_currentVideo = null;
			}
			ResetVideoMenu();
		}

		[UIAction("on-back-action")]
		[UsedImplicitly]
		private void OnBackAction()
		{
			_videoDetailsViewRect.gameObject.SetActive(true);
			_videoSearchResultsViewRect.gameObject.SetActive(false);
			SetupVideoDetails();
		}

		[UIAction("on-query")]
		[UsedImplicitly]
		private void OnQueryAction(string query)
		{
			_noVideoViewRect.gameObject.SetActive(false);
			_videoDetailsViewRect.gameObject.SetActive(false);
			_videoSearchResultsViewRect.gameObject.SetActive(true);

			ResetSearchView();
			_downloadButton.interactable = false;
			_refineButton.interactable = false;
			StartCoroutine(SearchLoadingCoroutine());

			_downloadController.Search(query);
		}

		private void SearchProgress(DownloadController.YTResult result)
		{
			//Event is being invoked twice for whatever reason, so keep a list of what has been added before
			if (_searchResults.Contains(result))
			{
				return;
			}

			_searchResults.Add(result);
			var updateSearchResultsCoroutine = UpdateSearchResults(result);
			StartCoroutine(updateSearchResultsCoroutine);
		}

		private void ResetSearchView()
		{
			StopAllCoroutines();

			if (_customListTableData.data != null && _customListTableData.data.Count > 0)
			{
				_customListTableData.data.Clear();
				_customListTableData.tableView.ReloadData();
			}

			_downloadButton.interactable = false;
			_downloadButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
			_selectedCell = -1;
		}

		private IEnumerator SearchLoadingCoroutine()
		{
			var count = 0;
			const string loadingText = "Searching for videos, please wait";
			_searchResultsLoadingText.gameObject.SetActive(true);

			//Loading animation
			while (_searchResultsLoadingText.gameObject.activeInHierarchy)
			{
				string periods = string.Empty;
				count++;

				for (var i = 0; i < count; i++)
				{
					periods += ".";
				}

				if (count == 3)
				{
					count = 0;
				}

				_searchResultsLoadingText.SetText(loadingText + periods);

				yield return new WaitForSeconds(0.5f);
			}
		}

		[UIAction("on-select-cell")]
		[UsedImplicitly]
		private void OnSelectCell(TableView view, int selection)
		{
			if (_customListTableData.data.Count > selection)
			{
				_selectedCell = selection;
				_downloadButton.interactable = true;
				Plugin.Logger.Debug($"Selected cell: [{_selectedCell}] {_downloadController.SearchResults[selection].ToString()}");
			}
			else
			{
				_downloadButton.interactable = false;
				_selectedCell = -1;
			}
		}

		[UIAction("on-download-action")]
		[UsedImplicitly]
		private void OnDownloadAction()
		{
			Plugin.Logger.Debug("Download pressed");
			if (_selectedCell < 0 || _currentLevel == null)
			{
				Plugin.Logger.Error("No cell or level selected on download action");
				return;
			}

			_downloadButton.interactable = false;
			VideoConfig config = new VideoConfig(_downloadController.SearchResults[_selectedCell], _currentLevel);
			VideoLoader.SaveVideoConfig(config);
			VideoLoader.AddConfigToCache(config);
			_downloadController.StartDownload(config);
			_currentVideo = config;
			SetupVideoDetails();
		}

		[UIAction("on-preview-action")]
		[UsedImplicitly]
		private void OnPreviewAction()
		{
			StartCoroutine(PlaybackController.Instance.StartPreviewCoroutine());
			SetButtonState(true);
		}

		[UIAction("on-offset-decrease-action-high")]
		[UsedImplicitly]
		private void DecreaseOffsetHigh() => ApplyOffset(-1000);

		[UIAction("on-offset-decrease-action-mid")]
		[UsedImplicitly]
		private void DecreaseOffsetMid() => ApplyOffset(-100);

		[UIAction("on-offset-decrease-action-low")]
		[UsedImplicitly]
		private void DecreaseOffsetLow() => ApplyOffset(-20);

		[UIAction("on-offset-increase-action-high")]
		[UsedImplicitly]
		private void IncreaseOffsetHigh() => ApplyOffset(1000);

		[UIAction("on-offset-increase-action-mid")]
		[UsedImplicitly]
		private void IncreaseOffsetMid() => ApplyOffset(100);

		[UIAction("on-offset-increase-action-low")]
		[UsedImplicitly]
		private void IncreaseOffsetLow() => ApplyOffset(20);
	}

	public class VideoMenuStatus : MonoBehaviour
	{
		public event EventHandler DidEnable = null!;
		public event EventHandler DidDisable = null!;

		private void OnEnable()
		{
			var handler = DidEnable;

			handler.Invoke(this, EventArgs.Empty);
		}

		private void OnDisable()
		{
			var handler = DidDisable;

			handler.Invoke(this, EventArgs.Empty);
		}
	}
}