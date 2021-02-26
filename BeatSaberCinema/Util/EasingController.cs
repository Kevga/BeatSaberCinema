using System;
using System.Collections;
using UnityEngine;

namespace BeatSaberCinema
{
	public class EasingController
	{
		private enum EasingDirection {EaseIn = 1, EaseOut = -1}
		private float _easingValue;
		private IEnumerator? _easingCoroutine;
		private const float DEFAULT_DURATION = 1.0f;
		private bool _isEasing;

		public event Action<float>? EasingUpdate;

		public bool IsFading => _isEasing;

		public EasingController(float initialValue = 0f)
		{
			_easingValue = initialValue;
		}

		public void EaseIn(float duration = DEFAULT_DURATION)
		{
			StartEasingCoroutine(EasingDirection.EaseIn, duration);
		}

		public void EaseOut(float duration = DEFAULT_DURATION)
		{
			StartEasingCoroutine(EasingDirection.EaseOut, duration);
		}

		private void StartEasingCoroutine(EasingDirection easingDirection, float duration)
		{
			if (_easingCoroutine != null)
			{
				SharedCoroutineStarter.instance.StopCoroutine(_easingCoroutine);
			}

			_isEasing = true;
			var speed = (int) easingDirection / (float) Math.Max(0.0001, duration);
			_easingCoroutine = Ease(speed);
			SharedCoroutineStarter.instance.StartCoroutine(_easingCoroutine);
		}

		private IEnumerator Ease(float speed)
		{
			do
			{
				_easingValue += Time.deltaTime * speed;
				_easingValue = Math.Max(0, Math.Min(1, _easingValue));
				EasingUpdate?.Invoke(_easingValue);
				yield return null;
			} while (_easingValue > 0 && _easingValue < 1);
			_isEasing = false;
		}
	}
}