using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeatSaberCinema
{
	public class ScreenController
	{
		internal readonly List<GameObject> screens = new List<GameObject>();

		private readonly MaterialPropertyBlock _materialPropertyBlock;
		private static readonly int Brightness = Shader.PropertyToID("_Brightness");
		private static readonly int Contrast = Shader.PropertyToID("_Contrast");
		private static readonly int Saturation = Shader.PropertyToID("_Saturation");
		private static readonly int Hue = Shader.PropertyToID("_Hue");
		private static readonly int Gamma = Shader.PropertyToID("_Gamma");
		private static readonly int Exposure = Shader.PropertyToID("_Exposure");
		private static readonly int VignetteRadius = Shader.PropertyToID("_VignetteRadius");
		private static readonly int VignetteSoftness = Shader.PropertyToID("_VignetteSoftness");
		private static readonly int VignetteElliptical = Shader.PropertyToID("_VignetteOval");

		public ScreenController()
		{
			_materialPropertyBlock = new MaterialPropertyBlock();
		}

		internal void CreateScreen(Transform parent)
		{
			var newScreen = new GameObject("CinemaScreen");
			newScreen.transform.parent = parent.transform;
			newScreen.AddComponent<CurvedSurface>();
			newScreen.layer = LayerMask.NameToLayer("Environment");
			newScreen.GetComponent<Renderer>();
			CreateScreenBody(newScreen.transform);
			newScreen.AddComponent<CustomBloomPrePass>();
			newScreen.AddComponent<SoftParent>();
			screens.Add(newScreen);
		}

		private static void CreateScreenBody(Transform parent)
		{
			GameObject body = new GameObject("Body");
			body.AddComponent<CurvedSurface>();
			body.transform.parent = parent.transform;
			body.transform.localPosition = new Vector3(0, 0, 0.4f); //A fixed offset is necessary for the center segments of the curved screen
			body.transform.localScale = new Vector3(1.01f, 1.01f, 1.01f);
			Renderer bodyRenderer = body.GetComponent<Renderer>();
			var sourceMaterial = new Material(Resources.FindObjectsOfTypeAll<Shader>().LastOrDefault(x => x.name == "Custom/OpaqueNeonLight"));
			if (sourceMaterial != null)
			{
				bodyRenderer.material = new Material(sourceMaterial);
			}
			else
			{
				Log.Error("Source material for body was not found!");
				body.transform.localScale = Vector3.zero;
			}

			bodyRenderer.material.color = new Color(0, 0, 0, 0);


			body.layer = LayerMask.NameToLayer("Environment");
		}

		public void SetScreensActive(bool active)
		{
			foreach (var screen in screens)
			{
				screen.SetActive(active);
			}
		}

		public void SetScreenBodiesActive(bool active)
		{
			foreach (var screen in screens)
			{
				screen.transform.GetChild(0).gameObject.SetActive(active);
			}
		}

		public Renderer GetRenderer()
		{
			return screens[0].GetComponent<Renderer>();
		}

		public void SetPlacement(Placement placement)
		{
			SetPlacement(placement.Position, placement.Rotation, placement.Width, placement.Height, placement.Curvature, placement.Subsurfaces);
		}

		private void SetPlacement(Vector3 pos, Vector3 rot, float width, float height, float? curvatureDegrees = null, int? subsurfaces = null)
		{
			var screen = screens[0];
			screen.transform.position = pos;
			screen.transform.eulerAngles = rot;
			screen.transform.localScale = Vector3.one;

			//Set body distance. Needs to scale up with distance from origin to prevent z-fighting
			var distance = Vector3.Distance(screen.transform.position, Vector3.zero);
			var bodyDistance = Math.Max(0.05f, distance / 250f);
			screen.transform.Find("Body").localPosition = new Vector3(0, 0, bodyDistance);

			InitializeSurfaces(width, height, pos.z, curvatureDegrees, subsurfaces);
			RegenerateScreenSurfaces();
		}

		private void InitializeSurfaces(float width, float height, float distance, float? curvatureDegrees, int? subsurfaces)
		{
			foreach (var screen in screens)
			{
				var screenSurface = screen.GetComponent<CurvedSurface>();
				var screenBodySurface = screen.transform.GetChild(0).GetComponent<CurvedSurface>();
				var screenBloomPrePass = screen.GetComponent<CustomBloomPrePass>();

				screenSurface.Initialize(width, height, distance, curvatureDegrees, subsurfaces);
				screenBodySurface.Initialize(width, height, distance, curvatureDegrees, subsurfaces);
				screenBloomPrePass.UpdateScreenDimensions(width, height);
			}
		}

		private void RegenerateScreenSurfaces()
		{
			foreach (var screen in screens)
			{
				screen.GetComponent<CurvedSurface>().Generate();
				screen.transform.GetChild(0).GetComponent<CurvedSurface>().Generate(); //screen body
				screen.GetComponent<CustomBloomPrePass>().UpdateMesh();
			}
		}

		public void SetBloomIntensity(float? bloomIntensity)
		{
			foreach (var screen in screens)
			{
				screen.GetComponent<CustomBloomPrePass>().SetBloomIntensityConfigSetting(bloomIntensity);
			}
		}

		public void SetAspectRatio(float ratio)
		{
			foreach (var screen in screens)
			{
				var screenSurface = screen.GetComponent<CurvedSurface>();
				var screenBodySurface = screen.transform.GetChild(0).GetComponent<CurvedSurface>();
				var screenBloomPrePass = screen.GetComponent<CustomBloomPrePass>();

				screenSurface.Width = screenSurface.Height * ratio;
				screenBodySurface.Width = screenSurface.Height * ratio;
				screenBloomPrePass.UpdateScreenDimensions(screenSurface.Width, screenSurface.Height);
			}

			RegenerateScreenSurfaces();
		}

		public void SetSoftParent(Transform? parent)
		{
			var softParent = screens[0].GetComponent<SoftParent>();
			softParent.enabled = parent != null;
			softParent.AssignParent(parent);
		}

		public void SetShaderParameters(VideoConfig? config)
		{
			foreach (var screen in screens)
			{
				var screenRenderer = screen.GetComponent<Renderer>();

				var colorCorrection = config?.colorCorrection;
				var vignette = config?.vignette;

				screenRenderer.GetPropertyBlock(_materialPropertyBlock);

				SetShaderFloat(Brightness, colorCorrection?.brightness, 0f, 2f, 1f);
				SetShaderFloat(Contrast, colorCorrection?.contrast, 0f, 5f, 1f);
				SetShaderFloat(Saturation, colorCorrection?.saturation, 0f, 5f, 1f);
				SetShaderFloat(Hue, colorCorrection?.hue, -360f, 360f, 0f);
				SetShaderFloat(Exposure, colorCorrection?.exposure, 0f, 5f, 1f);
				SetShaderFloat(Gamma, colorCorrection?.gamma, 0f, 5f, 1f);

				SetVignette(vignette, _materialPropertyBlock);

				screenRenderer.SetPropertyBlock(_materialPropertyBlock);
			}
		}

		public void SetVignette(VideoConfig.Vignette? vignette = null, MaterialPropertyBlock? materialPropertyBlock = null)
		{
			foreach (var screen in screens)
			{
				var screenRenderer = screen.GetComponent<Renderer>();
				var setPropertyBlock = materialPropertyBlock == null;
				if (setPropertyBlock)
				{
					screenRenderer.GetPropertyBlock(_materialPropertyBlock);
					materialPropertyBlock = _materialPropertyBlock;
				}

				var elliptical = SettingsStore.Instance.CornerRoundness > 0 && vignette == null;
				SetShaderFloat(VignetteRadius, vignette?.radius, 0f, 1f, (elliptical ? 1 - SettingsStore.Instance.CornerRoundness : 1f));
				SetShaderFloat(VignetteSoftness, vignette?.softness, 0f, 1f, (elliptical ? 0.02f : 0.005f));
				materialPropertyBlock!.SetInt(VignetteElliptical,
					vignette?.type == "oval" || vignette?.type == "elliptical" || vignette?.type == "ellipse" || (vignette?.type == null && elliptical)
						? 1
						: 0);

				if (setPropertyBlock)
				{
					screenRenderer.SetPropertyBlock(_materialPropertyBlock);
				}
			}
		}

		private void SetShaderFloat(int nameID, float? value, float min, float max, float defaultValue)
		{
			_materialPropertyBlock.SetFloat(nameID, Math.Min(max, Math.Max(min, value ?? defaultValue)));
		}
	}
}