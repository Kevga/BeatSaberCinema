using System;
using System.Linq;
using BeatSaberMarkupLanguage;
using System.Reflection;
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
		[UIObject("level-detail-root")] private readonly GameObject _root = null!;
		[UIComponent("level-detail-button")] private readonly Button _button = null!;
		[UIComponent("level-detail-button")] private readonly TextMeshProUGUI _buttonText = null!;
		[UIComponent("level-detail-text")] private readonly TextMeshProUGUI _label = null!;
		private readonly Image _buttonUnderline;
		private readonly StandardLevelDetailViewController _standardLevelDetailViewController;

		internal Action? ButtonPressedAction;

		internal LevelDetailViewController()
		{
			_standardLevelDetailViewController = Resources.FindObjectsOfTypeAll<StandardLevelDetailViewController>().Last();
			BSMLParser.instance.Parse(Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "BeatSaberCinema.VideoMenu.Views.level-detail.bsml"), _standardLevelDetailViewController.transform.Find("LevelDetail").gameObject, this);
			var rectTransform = _root.GetComponent<RectTransform>();
			rectTransform.offsetMin = new Vector2(0.8f, -45.8f);
			rectTransform.offsetMax = new Vector2(-2.9f, -41.2f);

			_buttonUnderline = _button.transform.Find("Underline").gameObject.GetComponent<Image>();

			//Clone background from level difficulty selection
			var bg = _standardLevelDetailViewController.transform.Find("LevelDetail").Find("BeatmapDifficulty").Find("BG");
			Object.Instantiate(bg, _root.transform);
		}

		public void SetActive(bool active) {
			_root.SetActive(active);
		}

		public void SetText(string? label, string? button = null, Color? textColor = null, Color? underlineColor = null)
		{
			_label.gameObject.SetActive(label != null);
			_label.text = label ?? "";
			_label.color = textColor ?? Color.white;
			_button.gameObject.SetActive(button != null);
			_buttonText.text = button ?? "";
			_buttonUnderline.color = underlineColor ?? Color.clear;
		}

		[UIAction("level-detail-button-action")]
		[UsedImplicitly]
		private void OnButtonPress()
		{
			ButtonPressedAction?.Invoke();
		}

		public void RefreshContent()
		{
			if (!Util.IsMultiplayer())
			{
				_standardLevelDetailViewController.RefreshContentLevelDetailView();
			}
		}
	}
}