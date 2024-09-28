using System;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BeatSaberCinema
{
	[UsedImplicitly]
	public class LevelDetailViewController
	{
		// ReSharper disable MemberInitializerValueIgnored
		[UIObject("level-detail-root")] private readonly GameObject _root = null!;
		[UIComponent("level-detail-button")] private readonly Button _button = null!;
		[UIComponent("level-detail-button")] private readonly TextMeshProUGUI _buttonText = null!;
		[UIComponent("level-detail-text")] private readonly TextMeshProUGUI _label = null!;
		// ReSharper restore MemberInitializerValueIgnored

		private readonly Image? _buttonUnderline;
		private readonly StandardLevelDetailViewController? _standardLevelDetailViewController;

		internal event Action? ButtonPressedAction;

		// ReSharper disable Unity.InefficientPropertyAccess
		internal LevelDetailViewController()
		{
			_standardLevelDetailViewController = Resources.FindObjectsOfTypeAll<StandardLevelDetailViewController>().LastOrDefault();
			if (_standardLevelDetailViewController == null)
			{
				return;
			}

			var levelDetail = _standardLevelDetailViewController.transform.Find("LevelDetail");
			if (levelDetail == null)
			{
				_standardLevelDetailViewController = null;
				return;
			}

			BSMLParser.Instance.Parse(Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "BeatSaberCinema.VideoMenu.Views.level-detail.bsml"), levelDetail.gameObject, this);
			SetActive(false);


			_buttonUnderline = _button.transform.Find("Underline").gameObject.GetComponent<Image>();

			//Clone background from level difficulty selection
			var beatmapDifficulty = levelDetail.Find("BeatmapDifficulty");
			var beatmapCharacteristic = levelDetail.Find("BeatmapCharacteristic");
			var actionButtons = levelDetail.Find("ActionButtons");
			var levelDetailBackground = beatmapDifficulty.Find("BG");
			if (beatmapDifficulty == null || beatmapCharacteristic == null || actionButtons == null || levelDetailBackground == null)
			{
				_standardLevelDetailViewController = null;
				return;
			}

			var characteristicTransform = beatmapCharacteristic.GetComponent<RectTransform>();
			var difficultyTransform = beatmapDifficulty.GetComponent<RectTransform>();
			var actionButtonTransform = actionButtons.GetComponent<RectTransform>();

			//The difference between characteristic and difficulty transforms. Using this would make it equal size to those
			var offsetMinYDifference = difficultyTransform.offsetMin.y + (difficultyTransform.offsetMin.y - characteristicTransform.offsetMin.y);
			//The maximum it can be without overlapping with the action buttons
			var offsetMinYMax = actionButtonTransform.offsetMin.y + actionButtonTransform.sizeDelta.y;
			//We take whichever is larger to make best use of the available space
			var offsetMinY = Math.Max(offsetMinYDifference, offsetMinYMax);

			var offsetMin = new Vector2(difficultyTransform.offsetMin.x, offsetMinY);
			var offsetMax = new Vector2(difficultyTransform.offsetMax.x, difficultyTransform.offsetMax.y + (difficultyTransform.offsetMax.y - characteristicTransform.offsetMax.y));

			var rectTransform = _root.GetComponent<RectTransform>();
			rectTransform.offsetMin = offsetMin;
			rectTransform.offsetMax = offsetMax;

			Object.Instantiate(levelDetailBackground, _root.transform);
		}

		public void SetActive(bool active) {
			if (_standardLevelDetailViewController == null)
			{
				return;
			}

			_root.SetActive(active);
		}

		public void SetText(string? label, string? button = null, Color? textColor = null, Color? underlineColor = null)
		{
			if (_standardLevelDetailViewController == null)
			{
				return;
			}

			_label.gameObject.SetActive(label != null);
			_label.text = label ?? "";
			_label.color = textColor ?? Color.white;
			_button.gameObject.SetActive(button != null);
			_buttonText.text = button ?? "";
			_buttonUnderline!.color = underlineColor ?? Color.clear;
		}

		[UIAction("level-detail-button-action")]
		[UsedImplicitly]
		private void OnButtonPress()
		{
			ButtonPressedAction?.Invoke();
		}

		public void RefreshContent()
		{
			if (!Util.IsMultiplayer() && _standardLevelDetailViewController != null)
			{
				_standardLevelDetailViewController.RefreshContentLevelDetailView();
			}
		}
	}
}