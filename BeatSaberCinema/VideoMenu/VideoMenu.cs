using System;
using System.Collections;
using System.Collections.Generic;
using BeatmapEditor3D.DataModels;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using IPA.Utilities;
using JetBrains.Annotations;
using SongCore.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// ReSharper disable ArrangeMethodOrOperatorBody
namespace BeatSaberCinema
{
	public class VideoMenu
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
		[UIObject("offset-controls")] private readonly GameObject _offsetControls = null!;
		[UIObject("customize-offset-toggle")] private readonly GameObject _customizeOffsetToggle = null!;
		[UIParams] private readonly BSMLParserParams _bsmlParserParams = null!;

		private Coroutine? _searchLoadingCoroutine;
		private Coroutine? _updateSearchResultsCoroutine;

		[UIValue("customize-offset")]
		public bool CustomizeOffset
		{
			get => _currentVideo != null && (!_currentVideo.IsOfficialConfig || _currentVideo.userSettings?.customOffset == true || _currentVideo.IsWIPLevel);
			set
			{
				if (_currentVideo == null || !value)
				{
					return;
				}

				_currentVideo.userSettings ??= new VideoConfig.UserSettings();
				_currentVideo.userSettings.customOffset = true;
				_currentVideo.userSettings.originalOffset = _currentVideo.offset;
				_currentVideo.NeedsToSave = true;
				_customizeOffsetToggle.SetActive(false);
				_offsetControls.SetActive(true);
			}
		}

		private VideoMenuStatus _menuStatus = null!;
		private LevelDetailViewController? _levelDetailMenu;
		private bool _videoMenuInitialized;

		private BeatmapLevel? _currentLevel;
		private bool _currentLevelIsPlaylistSong;
		private ExtraSongData? _extraSongData;
		private ExtraSongData.DifficultyData? _difficultyData;
		private VideoConfig? _currentVideo;
		private bool _videoMenuActive;
		private int _selectedCell;
		private string _searchText = "";
		private string? _thumbnailURL;
		private readonly DownloadController _downloadController = new DownloadController();
		private readonly SearchController _searchController = new SearchController();
		private readonly List<YTResult> _searchResults = new List<YTResult>();

		public static VideoMenu? Instance { get; private set; }

		public void Init()
		{
			Events.LevelSelected -= OnLevelSelected;
			Events.LevelSelected += OnLevelSelected;
			Events.DifficultySelected -= OnDifficultySelected;
			Events.DifficultySelected += OnDifficultySelected;

			if (_root == null)
			{
				Log.Debug("RootObject is null!");
				return;
			}

			if (_levelDetailMenu != null)
			{
				_levelDetailMenu.ButtonPressedAction -= OnDeleteVideoAction;
			}

			_levelDetailMenu = new LevelDetailViewController();
			_levelDetailMenu.ButtonPressedAction += OnDeleteVideoAction;
			CreateStatusListener();
			_deleteButton.transform.localScale *= 0.5f;

			_searchKeyboard.ClearOnOpen = false;

			if (_videoMenuInitialized)
			{
				return;
			}

			_videoMenuInitialized = true;
			_videoDetailsViewRect.gameObject.SetActive(false);
			_videoSearchResultsViewRect.gameObject.SetActive(false);

			_searchController.SearchProgress += SearchProgress;
			_searchController.SearchFinished += SearchFinished;
			_downloadController.DownloadProgress += OnDownloadProgress;
			_downloadController.DownloadFinished += OnDownloadFinished;
			VideoLoader.ConfigChanged += OnConfigChanged;

			if (!_downloadController.LibrariesAvailable())
			{
				Log.Warn($"One or more of the libraries are missing. Downloading videos will not work. To fix this, reinstall Cinema and make sure yt-dlp and ffmpeg are in the Libs folder of Beat Saber, which is located at {UnityGame.LibraryPath}.");
			}
		}

		public void CreateStatusListener()
		{
			//This needs to be reinitialized every time a fresh menu scene load happens
			if (_menuStatus != null)
			{
				_menuStatus.DidEnable -= StatusViewerDidEnable;
				_menuStatus.DidDisable -= StatusViewerDidDisable;
				Object.Destroy(_menuStatus);
			}

			_menuStatus = _root.AddComponent<VideoMenuStatus>();
			Log.Debug("Adding status listener to: " + _menuStatus.name);
			_menuStatus.DidEnable += StatusViewerDidEnable;
			_menuStatus.DidDisable += StatusViewerDidDisable;
		}

		public static void AddTab()
		{
			if (Instance == null)
			{
				Log.Debug("Initializing VideoMenu");
				Instance = new VideoMenu();
				Instance.Init();
			}

			Log.Debug("Adding tab");
			GameplaySetup.Instance.AddTab("Cinema", "BeatSaberCinema.VideoMenu.Views.video-menu.bsml", Instance, MenuType.All);
		}

		public static void RemoveTab()
		{
			Log.Debug("Removing tab");
			GameplaySetup.Instance.RemoveTab("Cinema");
			Instance = null;
		}

		public void ResetVideoMenu()
		{
			_bsmlParserParams.EmitEvent("hide-keyboard");
			_noVideoViewRect.gameObject.SetActive(true);
			_videoDetailsViewRect.gameObject.SetActive(false);
			SetButtonState(false);

			if (!_downloadController.LibrariesAvailable())
			{
				_noVideoText.text = "Libraries not found. Please reinstall Cinema.\r\nMake sure you unzip the files from the Libs folder into 'Beat Saber\\Libs'.";
				_searchButton.gameObject.SetActive(false);
				return;
			}

			if (!Plugin.Enabled)
			{
				_noVideoText.text = "Cinema is disabled.\r\nYou can re-enable it on the left side of the main menu.";
				return;
			}

			if (_currentLevel == null)
			{
				_noVideoText.text = "No level selected";
				return;
			}

			_noVideoText.text = "No video configured";
		}

		private void OnDownloadProgress(VideoConfig videoConfig)
		{
			UpdateStatusText(videoConfig);
			SetupLevelDetailView(videoConfig);
		}

		public void SetButtonState(bool state)
		{
			_previewButton.interactable = state;
			_deleteButton.interactable = state;
			_deleteVideoButton.interactable = state;
			_searchButton.gameObject.SetActive(_currentLevel != null &&
			                                   !VideoLoader.IsDlcSong(_currentLevel) &&
			                                   _downloadController.LibrariesAvailable());
			_previewButtonText.text = PlaybackController.Instance.IsPreviewPlaying ? "Stop preview" : "Preview";

			if (_currentLevel != null && VideoLoader.IsDlcSong(_currentLevel) && _downloadController.LibrariesAvailable())
			{
				CheckEntitlementAndEnableSearch(_currentLevel);
			}

			if (_currentVideo == null)
			{
				return;
			}

			//Hide delete config button for mapper-made configs
			var officialConfig = _currentVideo.configByMapper == true && !_currentVideo.IsWIPLevel;
			_deleteButton.gameObject.SetActive(!officialConfig);

			switch (_currentVideo.DownloadState)
			{
				case DownloadState.Converting:
				case DownloadState.Preparing:
				case DownloadState.Downloading:
				case DownloadState.DownloadingVideo:
				case DownloadState.DownloadingAudio:
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

		private async void CheckEntitlementAndEnableSearch(BeatmapLevel level)
		{
			var entitlement = await VideoLoader.GetEntitlementForLevel(level);
			if (entitlement == EntitlementStatus.Owned && _currentLevel == level)
			{
				_searchButton.gameObject.SetActive(true);
			}
		}

		public void SetupVideoDetails()
		{
			if (_videoSearchResultsViewRect == null)
			{
				Log.Warn("Video search results view rect is null, skipping UI setup");
				return;
			}

			_videoSearchResultsViewRect.gameObject.SetActive(false);
			_levelDetailMenu?.SetActive(false);

			if (_currentVideo == null || !_downloadController.LibrariesAvailable())
			{
				ResetVideoMenu();
				Log.Debug("No video configured");
				return;
			}

			SetupLevelDetailView(_currentVideo);

			//Skip setting up the video menu if it's not showing. Prevents an unnecessary web request for the thumbnail.
			if (!_videoMenuActive)
			{
				ResetVideoMenu();
				Log.Debug("Video Menu is not active");
				return;
			}

			if (_currentVideo.videoID == null && _currentVideo.videoUrl == null)
			{
				ResetVideoMenu();
				if (_currentVideo.forceEnvironmentModifications != true)
				{
					return;
				}

				_noVideoText.text = "This map uses Cinema to modify the environment\r\nwithout displaying a video.\r\n\r\nNo configuration options available.";
				_searchButton.interactable = false;
				_searchButton.gameObject.SetActive(false);

				return;
			}

			_noVideoViewRect.gameObject.SetActive(false);
			_videoDetailsViewRect.gameObject.SetActive(true);

			SetButtonState(true);

			_videoTitleText.text = Util.FilterEmoji(_currentVideo.title ?? "Untitled Video");
			_videoAuthorText.text = "Author: " + Util.FilterEmoji(_currentVideo.author ?? "Unknown Author");
			_videoDurationText.text = "Duration: " + Util.SecondsToString(_currentVideo.duration);

			_videoOffsetText.text = $"{_currentVideo.offset:n0}" + " ms";
			SetThumbnail(_currentVideo.videoID != null ? $"https://i.ytimg.com/vi/{_currentVideo.videoID}/hqdefault.jpg" : null);

			UpdateStatusText(_currentVideo);
			if (CustomizeOffset)
			{
				_customizeOffsetToggle.SetActive(false);
				_offsetControls.SetActive(true);
			}
			else
			{
				_customizeOffsetToggle.SetActive(true);
				_offsetControls.SetActive(false);
			}

			_bsmlParserParams.EmitEvent("update-customize-offset");
		}

		public void SetupLevelDetailView(VideoConfig videoConfig)
		{
			if (videoConfig != _currentVideo)
			{
				return;
			}

			//This is the case if the map only uses environment modifications
			if ((_currentVideo.videoID == null && _currentVideo.videoUrl == null) || _levelDetailMenu == null)
			{
				return;
			}

			switch (videoConfig.DownloadState)
			{
				case DownloadState.Downloaded:
					if (videoConfig.IsWIPLevel && _difficultyData?.HasCinema() == false && _extraSongData?.HasCinemaInAnyDifficulty() == false)
					{
						_levelDetailMenu.SetActive(true);
						_levelDetailMenu.SetText("Please add Cinema as a suggestion", null, Color.red);
					}
					else if (videoConfig.ErrorMessage != null)
					{
						_levelDetailMenu.SetActive(true);
						_levelDetailMenu.SetText(videoConfig.ErrorMessage, null, Color.red, Color.red);
					}
					else
					{
						_levelDetailMenu.SetText("Video ready!", null, Color.green);
					}

					break;
				case DownloadState.Preparing:
					_levelDetailMenu.SetActive(true);
					_levelDetailMenu.SetText($"Preparing download...", "Cancel", Color.yellow, Color.red);
					break;
				case DownloadState.Downloading:
					_levelDetailMenu.SetActive(true);
					_levelDetailMenu.SetText($"Downloading ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)", "Cancel", Color.yellow, Color.red);
					break;
				case DownloadState.DownloadingVideo:
					_levelDetailMenu.SetActive(true);
					_levelDetailMenu.SetText($"Downloading video ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)", "Cancel", Color.yellow, Color.red);
					break;
				case DownloadState.DownloadingAudio:
					_levelDetailMenu.SetActive(true);
					_levelDetailMenu.SetText($"Downloading audio ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)", "Cancel", Color.yellow, Color.red);
					break;
				case DownloadState.Converting:
					_levelDetailMenu.SetActive(true);
					_levelDetailMenu.SetText($"Converting...",
						"Cancel", Color.yellow, Color.red);
					break;
				case DownloadState.NotDownloaded:
					_levelDetailMenu.SetActive(true);
					if (videoConfig.ErrorMessage != null)
					{
						_levelDetailMenu.SetText(videoConfig.ErrorMessage, "Retry", Color.red, Color.red);
					}
					else if (_difficultyData?.HasCinemaRequirement() == true)
					{
						_levelDetailMenu.SetText("Video required to play this map", "Download", Color.red, Color.green);
					}
					else
					{
						_levelDetailMenu.SetText("Video available", "Download Video", null, Color.green);
					}

					break;
				case DownloadState.Cancelled:
					_levelDetailMenu.SetActive(true);
					_levelDetailMenu.SetText("Cancelling...", "Download Video", Color.red, Color.green);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public void UpdateStatusText(VideoConfig videoConfig)
		{
			if (videoConfig != _currentVideo || !_videoMenuActive)
			{
				return;
			}

			switch (videoConfig.DownloadState)
			{
				case DownloadState.Downloaded:
					_videoStatusText.text = "Downloaded";
					_videoStatusText.color = Color.green;
					break;
				case DownloadState.Preparing:
					_videoStatusText.text = $"Preparing download...";
					_videoStatusText.color = Color.yellow;
					_previewButton.interactable = false;
					break;
				case DownloadState.Downloading:
					_videoStatusText.text = $"Downloading ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)";
					_videoStatusText.color = Color.yellow;
					_previewButton.interactable = false;
					break;
				case DownloadState.DownloadingVideo:
					_videoStatusText.text = $"Downloading video ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)";
					_videoStatusText.color = Color.yellow;
					_previewButton.interactable = false;
					break;
				case DownloadState.DownloadingAudio:
					_videoStatusText.text = $"Downloading audio ({Convert.ToInt32(videoConfig.DownloadProgress * 100).ToString()}%)";
					_videoStatusText.color = Color.yellow;
					_previewButton.interactable = false;
					break;
				case DownloadState.Converting:
					_videoStatusText.text = $"Converting...";
					_videoStatusText.color = Color.yellow;
					_previewButton.interactable = false;
					break;
				case DownloadState.NotDownloaded:
					_videoStatusText.text = videoConfig.ErrorMessage ?? "Not downloaded";
					_videoStatusText.color = Color.red;
					_previewButton.interactable = false;
					break;
				case DownloadState.Cancelled:
					_videoStatusText.text = "Cancelling...";
					_videoStatusText.color = Color.red;
					_previewButton.interactable = false;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void SetThumbnail(string? url)
		{
			if (url != null && url == _thumbnailURL)
			{
				return;
			}

			_thumbnailURL = url;

			if (url == null)
			{
				SetThumbnailFromCover(_currentLevel);
				return;
			}

			_videoThumnnail.SetImageAsync(url);
		}

		private async void SetThumbnailFromCover(BeatmapLevel? level)
		{
			if (level == null)
			{
				return;
			}

			var coverSprite = await level.previewMediaData.GetCoverSpriteAsync();
			_videoThumnnail.sprite = coverSprite;
		}

		public void SetSelectedLevel(BeatmapLevel level)
		{
			if (_currentLevel != null && level.levelID == _currentLevel.levelID)
			{
				return;
			}

			Log.Debug($"Setting level to {level.levelID}");
			HandleDidSelectLevel(level);
		}

		public void HandleDidSelectEditorBeatmap(BeatmapDataModel beatmapData, string originalPath)
		{
			if (!Plugin.Enabled)
			{
				return;
			}

			PlaybackController.Instance.StopPreview(true);
			if (_currentVideo?.NeedsToSave == true)
			{
				VideoLoader.SaveVideoConfig(_currentVideo);
			}

			_currentVideo = VideoLoader.GetConfigForEditorLevel(beatmapData, originalPath);
			VideoLoader.SetupFileSystemWatcher(originalPath);
			PlaybackController.Instance.SetSelectedLevel(null, _currentVideo);
		}

		[Obsolete("This overload is depreciated, isPlaylistSong is determined automatically.", true)]
		public void HandleDidSelectLevel(BeatmapLevel? level, bool isPlaylistSong = false)
		{
			HandleDidSelectLevel(level);
		}

		public void HandleDidSelectLevel(BeatmapLevel? level)
		{
			//These will be set a bit later by a Harmony patch. Clear them to not accidentally access outdated info.
			_extraSongData = null;
			_difficultyData = null;

			if (!Plugin.Enabled || (_currentLevel == level && _currentLevelIsPlaylistSong)) //Ignores the duplicate event that occurs when selecting a playlist song
			{
				return;
			}

			_currentLevelIsPlaylistSong = InstalledMods.BeatSaberPlaylistsLib && level.IsPlaylistLevel();
			if (InstalledMods.BeatSaberPlaylistsLib && _currentLevelIsPlaylistSong)
			{
				level = level.GetLevelFromPlaylistIfAvailable();
			}

			PlaybackController.Instance.StopPreview(true);

			if (_currentVideo?.NeedsToSave == true)
			{
				VideoLoader.SaveVideoConfig(_currentVideo);
			}
			_currentLevel = level;
			if (_currentLevel == null)
			{
				_currentVideo = null;
				PlaybackController.Instance.SetSelectedLevel(null, null);
				SetupVideoDetails();
				return;
			}

			_currentVideo = VideoLoader.GetConfigForLevel(_currentLevel);

			VideoLoader.SetupFileSystemWatcher(_currentLevel);
			PlaybackController.Instance.SetSelectedLevel(_currentLevel, _currentVideo);
			SetupVideoDetails();

			_searchText = _currentLevel.songName + (!string.IsNullOrEmpty(_currentLevel.songAuthorName) ? " " + _currentLevel.songAuthorName : "");
		}

		private void OnLevelSelected(LevelSelectedArgs levelSelectedArgs)
		{
			if (!_videoMenuInitialized)
			{
				Log.Debug("Initializing video menu (late)");
				Init();
			}

			if (levelSelectedArgs.BeatmapData != null)
			{
				HandleDidSelectEditorBeatmap(levelSelectedArgs.BeatmapData, levelSelectedArgs.OriginalPath!);
				return;
			}

			HandleDidSelectLevel(levelSelectedArgs.BeatmapLevel);
		}

		private void OnDifficultySelected(ExtraSongDataArgs extraSongDataArgs)
		{
			_extraSongData = extraSongDataArgs.SongData;
			_difficultyData = extraSongDataArgs.SelectedDifficultyData;
			if (_currentVideo != null)
			{
				SetupLevelDetailView(_currentVideo);
			}
		}

		public void OnConfigChanged(VideoConfig? config)
		{
			_currentVideo = config;
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

			if (PlaybackController.Instance == null)
			{
				return;
			}

			_searchController.StopSearch();

			try
			{
				PlaybackController.Instance.StopPreview(true);
			}
			catch (Exception exception)
			{
				//This can happen when closing the game
				Log.Debug(exception);
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
				Log.Warn("Selected level was null on search action");
				return;
			}

			OnQueryAction(_searchText);
			_customListTableData.TableView.ScrollToCellWithIdx(0, TableView.ScrollPositionType.Beginning, false);
			_customListTableData.TableView.ClearSelection();
		}

		private IEnumerator UpdateSearchResults(YTResult result)
		{
			var title = $"[{Util.SecondsToString(result.Duration)}] {Util.FilterEmoji(result.Title)}";
			var description = $"{Util.FilterEmoji(result.Author)}";

			try
			{
				var stillImage = result.IsStillImage();
				string descriptionAddition;
				if (stillImage)
				{
					descriptionAddition = "Likely a still image";
				}
				else
				{
					descriptionAddition = result.GetQualityString() ?? "";
				}

				if (descriptionAddition.Length > 0)
				{
					description += ("   |   " + descriptionAddition);
				}
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}

			var item = new CustomListTableData.CustomCellInfo(title, description);
			var request = UnityWebRequestTexture.GetTexture($"https://i.ytimg.com/vi/{result.ID}/mqdefault.jpg");
			yield return request.SendWebRequest();
			if (request.result != UnityWebRequest.Result.ConnectionError && request.result != UnityWebRequest.Result.ProtocolError)
			{
				var tex = ((DownloadHandlerTexture) request.downloadHandler).texture;
				item.Icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f, 100, 1);
			}
			else
			{
				Log.Debug(request.error);
			}

			_customListTableData.Data.Add(item);
			_customListTableData.TableView.ReloadDataKeepingPosition();

			_downloadButton.interactable = (_selectedCell != -1);
			_downloadButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.green;
			_searchResultsLoadingText.gameObject.SetActive(false);
		}

		private void OnDownloadFinished(VideoConfig video)
		{
			if (_currentVideo != video)
			{
				return;
			}

			if (video.ErrorMessage != null)
			{
				SetupVideoDetails();
				return;
			}

			PlaybackController.Instance.PrepareVideo(video);

			if (_currentLevel != null)
			{
				VideoLoader.RemoveConfigFromCache(_currentLevel);
			}

			SetupVideoDetails();
			_levelDetailMenu?.SetActive(true);
			_levelDetailMenu?.RefreshContent();
		}

		public void ShowKeyboard()
		{
			_searchKeyboard.SetText(_searchText);
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
				Log.Warn("Current video was null on delete action");
				return;
			}

			PlaybackController.Instance.StopPreview(true);

			switch (_currentVideo.DownloadState)
			{
				case DownloadState.Preparing:
				case DownloadState.Downloading:
				case DownloadState.DownloadingAudio:
				case DownloadState.DownloadingVideo:
					_downloadController.CancelDownload(_currentVideo);
					break;
				case DownloadState.NotDownloaded:
				case DownloadState.Cancelled:
					_currentVideo.DownloadProgress = 0;
					_searchController.StopSearch();
					_downloadController.StartDownload(_currentVideo, SettingsStore.Instance.QualityMode);
					_currentVideo.NeedsToSave = true;
					VideoLoader.AddConfigToCache(_currentVideo, _currentLevel!);
					break;
				default:
					VideoLoader.DeleteVideo(_currentVideo);
					PlaybackController.Instance.VideoPlayer.Stop();
					PlaybackController.Instance.VideoPlayer.Player.url = null;
					PlaybackController.Instance.VideoPlayer.Player.Prepare();
					SetupLevelDetailView(_currentVideo);
					_levelDetailMenu?.RefreshContent();
					break;
			}

			UpdateStatusText(_currentVideo);
			SetButtonState(true);
		}

		[UIAction("on-delete-config-action")]
		[UsedImplicitly]
		private void OnDeleteConfigAction()
		{
			if (_currentVideo == null || _currentLevel == null)
			{
				Log.Warn("Failed to delete config: Either currentVideo or currentLevel is null");
				return;
			}

			PlaybackController.Instance.StopPreview(true);
			PlaybackController.Instance.StopPlayback();
			PlaybackController.Instance.VideoPlayer.Hide();

			if (_currentVideo.IsDownloading)
			{
				_downloadController.CancelDownload(_currentVideo);
			}

			VideoLoader.DeleteVideo(_currentVideo);
			var success = VideoLoader.DeleteConfig(_currentVideo, _currentLevel);
			if (success)
			{
				_currentVideo = null;
			}

			_levelDetailMenu?.SetActive(false);
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
			_searchLoadingCoroutine = CoroutineStarter.Instance.StartCoroutine(SearchLoadingCoroutine());

			_searchController.Search(query);
			_searchText = query;
		}

		private void SearchProgress(YTResult result)
		{
			//Event is being invoked twice for whatever reason, so keep a list of what has been added before
			if (_searchResults.Contains(result))
			{
				return;
			}

			_searchResults.Add(result);
			var updateSearchResultsCoroutine = UpdateSearchResults(result);
			_updateSearchResultsCoroutine = CoroutineStarter.Instance.StartCoroutine(updateSearchResultsCoroutine);
		}

		private void SearchFinished()
		{
			if (_searchResults.Count != 0)
			{
				return;
			}

			ResetSearchView();
			_searchResultsLoadingText.gameObject.SetActive(true);
			_searchResultsLoadingText.SetText("No search results found.\r\nUse the Refine Search button in the bottom right to choose a different search query.");
		}

		private void ResetSearchView()
		{
			if (_searchLoadingCoroutine != null)
			{
				CoroutineStarter.Instance.StopCoroutine(_searchLoadingCoroutine);
			}
			if (_updateSearchResultsCoroutine != null)
			{
				CoroutineStarter.Instance.StopCoroutine(_updateSearchResultsCoroutine);
			}

			if (_customListTableData.Data != null && _customListTableData.Data.Count > 0)
			{
				_customListTableData.Data.Clear();
				_customListTableData.TableView.ReloadData();
			}

			_downloadButton.interactable = false;
			_downloadButton.transform.Find("Underline").gameObject.GetComponent<Image>().color = Color.grey;
			_selectedCell = -1;
			_searchResults.Clear();
			_bsmlParserParams.EmitEvent("hide-keyboard");
		}

		private IEnumerator SearchLoadingCoroutine()
		{
			var count = 0;
			const string loadingText = "Searching for videos, please wait";
			_searchResultsLoadingText.gameObject.SetActive(true);

			//Loading animation
			while (_searchResultsLoadingText.gameObject.activeInHierarchy)
			{
				var periods = string.Empty;
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
			if (_customListTableData.Data.Count > selection)
			{
				_selectedCell = selection;
				_downloadButton.interactable = true;
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
			Log.Debug("Download pressed");
			if (_selectedCell < 0 || _currentLevel == null)
			{
				Log.Error("No cell or level selected on download action");
				return;
			}

			_downloadButton.interactable = false;
			var config = new VideoConfig(_searchController.SearchResults[_selectedCell], VideoLoader.GetLevelPath(_currentLevel)) { NeedsToSave = true };
			VideoLoader.AddConfigToCache(config, _currentLevel);
			_searchController.StopSearch();
			_downloadController.StartDownload(config, SettingsStore.Instance.QualityMode);
			_currentVideo = config;
			SetupVideoDetails();
		}

		[UIAction("on-preview-action")]
		[UsedImplicitly]
		private void OnPreviewAction()
		{
			PlaybackController.Instance.StartPreview();
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