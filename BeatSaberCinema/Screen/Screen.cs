using System.Linq;
using UnityEngine;

namespace BeatSaberCinema
{
	public class Screen: MonoBehaviour
	{
		private readonly GameObject _screenGameObject;
		private readonly GameObject _screenBodyGameObject;
		private readonly CurvedSurface _screenSurface;
		private readonly Renderer _screenRenderer;
		private CurvedSurface _screenBodySurface = null!;
		private readonly CustomBloomPrePass _screenBloomPrePass;
		private readonly SoftParent _softParent;

		public Screen()
		{
			_screenGameObject = new GameObject("CinemaScreen");
			_screenSurface = _screenGameObject.AddComponent<CurvedSurface>();
			_screenGameObject.layer = LayerMask.NameToLayer("Environment");
			_screenRenderer = _screenGameObject.GetComponent<Renderer>();
			_screenBodyGameObject = CreateBody();
			_screenBloomPrePass = _screenGameObject.AddComponent<CustomBloomPrePass>();
			_softParent = _screenGameObject.AddComponent<SoftParent>();

			Hide();
		}

		private GameObject CreateBody()
		{
			GameObject body = new GameObject("CinemaScreenBody");
			_screenBodySurface = body.AddComponent<CurvedSurface>();
			body.transform.parent = _screenGameObject.transform;
			body.transform.localPosition = new Vector3(0, 0, 0.4f); //A fixed offset is necessary for the center segments of the curved screen
			body.transform.localScale = new Vector3(1.01f, 1.01f, 1.01f);
			Renderer bodyRenderer = body.GetComponent<Renderer>();
			var sourceMaterial = Resources.FindObjectsOfTypeAll<Material>().LastOrDefault(x => x.name.StartsWith("DarkEnvironmentSimple"));
			if (sourceMaterial != null)
			{
				bodyRenderer.material = new Material(sourceMaterial);
			}
			else
			{
				Log.Error("Source material for body was not found!");
				body.transform.localScale = Vector3.zero;
			}

			body.layer = LayerMask.NameToLayer("Environment");
			return body;
		}

		public void Show()
		{
			_screenGameObject.SetActive(true);
		}

		public void Hide()
		{
			_screenGameObject.SetActive(false);
		}

		public void ShowBody()
		{
			_screenBodyGameObject.SetActive(true);
		}

		public void HideBody()
		{
			_screenBodyGameObject.SetActive(false);
		}

		public Renderer GetRenderer()
		{
			return _screenRenderer;
		}

		public void SetTransform(Transform parentTransform)
		{
			_screenGameObject.transform.parent = parentTransform;
		}

		public void SetPlacement(Placement placement)
		{
			SetPlacement(placement.Position, placement.Rotation, placement.Width, placement.Height, placement.Curvature, placement.Subsurfaces);
		}

		public void SetPlacement(Vector3 pos, Vector3 rot, float width, float height, float? curvatureDegrees = null, int? subsurfaces = null)
		{
			_screenGameObject.transform.position = pos;
			_screenGameObject.transform.eulerAngles = rot;
			_screenGameObject.transform.localScale = Vector3.one;
			InitializeSurfaces(width, height, pos.z, curvatureDegrees, subsurfaces);
			RegenerateScreenSurfaces();
		}

		public void InitializeSurfaces(float width, float height, float distance, float? curvatureDegrees, int? subsurfaces)
		{
			_screenSurface.Initialize(width, height, distance, curvatureDegrees, subsurfaces);
			_screenBodySurface.Initialize(width, height, distance, curvatureDegrees, subsurfaces);
			_screenBloomPrePass.UpdateScreenDimensions(width, height);
		}

		public void RegenerateScreenSurfaces()
		{
			_screenSurface.Generate();
			_screenBodySurface.Generate();
			_screenBloomPrePass.UpdateMesh();
		}

		public void SetBloomIntensity(float? bloomIntensity)
		{
			_screenBloomPrePass.SetBloomIntensityConfigSetting(bloomIntensity);
		}

		public void SetAspectRatio(float ratio)
		{
			_screenSurface.Width = _screenSurface.Height * ratio;
			_screenBodySurface.Width = _screenSurface.Height * ratio;
			_screenBloomPrePass.UpdateScreenDimensions(_screenSurface.Width, _screenSurface.Height);
			RegenerateScreenSurfaces();
		}

		public void SetSoftParent(Transform? parent)
		{
			_softParent.enabled = parent != null;
			_softParent.AssignParent(parent);
		}
	}
}