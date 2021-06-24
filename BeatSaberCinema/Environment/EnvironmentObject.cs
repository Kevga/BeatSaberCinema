using System;
using Newtonsoft.Json;
using UnityEngine;

namespace BeatSaberCinema
{
	[Serializable]
	[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
	public class EnvironmentObject
	{
		[JsonIgnore] public GameObject gameObject;
		public string name;
		public string? parentName;
		[JsonIgnore] public Vector3 position;
		[JsonIgnore] public Vector3 localPosition;
		[JsonIgnore] public Vector3 rotation;
		[JsonIgnore] public Vector3 scale;
		[JsonIgnore] public bool clone;
		[JsonIgnore] public bool activeInHierarchy;

		// ReSharper disable once InconsistentNaming
		public Transform transform => gameObject.transform;

		public void SetActive(bool active)
		{
			gameObject.SetActive(active);
		}

		public EnvironmentObject(GameObject gameObject, bool clone)
		{
			this.gameObject = gameObject;
			var cachedTransform = transform;
			name = cachedTransform.name;
			if (cachedTransform.parent != null)
			{
				parentName = cachedTransform.parent.name;
			}
			position = cachedTransform.position;
			localPosition = cachedTransform.localPosition;
			rotation = cachedTransform.eulerAngles;
			scale = cachedTransform.localScale;
			this.clone = clone;
			activeInHierarchy = gameObject.activeInHierarchy;
		}
	}
}