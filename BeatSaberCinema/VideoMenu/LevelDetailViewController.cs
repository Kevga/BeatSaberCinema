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
	internal class LevelDetailViewController
	{
		[UIObject("level-detail-root")] private readonly GameObject _root = null!;
		[UIComponent("level-detail-button")] private readonly Button _button = null!;
		[UIComponent("level-detail-button")] private readonly TextMeshProUGUI _buttonText = null!;
		[UIComponent("level-detail-text")] private readonly TextMeshProUGUI _label = null!;
		private readonly Image _buttonUnderline;

		internal Action? buttonPressed;

		internal LevelDetailViewController()
		{
			var standardLevelDetailViewController = Resources.FindObjectsOfTypeAll<StandardLevelDetailViewController>().Last();
			BSMLParser.instance.Parse(Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "BeatSaberCinema.VideoMenu.Views.level-detail.bsml"), standardLevelDetailViewController.transform.Find("LevelDetail").gameObject, this);
			var rectTransform = _root.GetComponent<RectTransform>();
			rectTransform.offsetMin = new Vector2(0.8f, -46.6f);
			rectTransform.offsetMax = new Vector2(-2.9f, -41.2f);

			_buttonUnderline = _button.transform.Find("Underline").gameObject.GetComponent<Image>();

			//Clone background from level difficulty selection
			var bg = standardLevelDetailViewController.transform.Find("LevelDetail").Find("BeatmapDifficulty").Find("BG");
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
		private void ButtonPressed()
		{
			buttonPressed?.Invoke();
		}
	}
}