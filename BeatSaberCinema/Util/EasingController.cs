using System;
using System.Collections;
using UnityEngine;

namespace BeatSaberCinema
{
	public class EasingController
	{
		private enum EasingDirection { EaseIn = 1, EaseOut = -1 }

		private float _easingValue;
		private IEnumerator? _easingCoroutine;
		private const float DEFAULT_DURATION = 1.0f;

		public event Action<float>? EasingUpdate;

		public bool IsFading { get; private set; }

		public bool IsOne => Math.Abs(_easingValue - 1f) < 0.00001f;
		public bool IsZero => _easingValue == 0;

		public float Value
		{
			get => _easingValue;
			set
			{
				EasingUpdate?.Invoke(_easingValue);
				_easingValue = value;
			}
		}

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
				CoroutineStarter.Instance.StopCoroutine(_easingCoroutine);
			}

			IsFading = true;
			var speed = (int) easingDirection / (float) Math.Max(0.0001, duration);
			_easingCoroutine = Ease(speed);

			CoroutineStarter.Instance.StartCoroutine(_easingCoroutine);
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

			IsFading = false;
		}
	}
}