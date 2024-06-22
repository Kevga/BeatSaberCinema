﻿using System;
using System.Collections.Generic;
using System.Linq;
using BS_Utils.Utilities;
using UnityEngine;

namespace BeatSaberCinema
{
	internal class CustomBloomPrePass : MonoBehaviour
	{
		//Most cameras have their own BloomPrePass, so save one for each camera and use that when rendering the bloom for the camera
		private readonly Dictionary<Camera, BloomPrePass?> _bloomPrePassDict = new Dictionary<Camera, BloomPrePass?>();
		private readonly Dictionary<Camera, BloomPrePassRendererSO> _bloomPrePassRendererDict = new Dictionary<Camera, BloomPrePassRendererSO>();
		private readonly Dictionary<Camera, BloomPrePassRenderDataSO.Data> _bloomPrePassRenderDataDict = new Dictionary<Camera, BloomPrePassRenderDataSO.Data>();
		private readonly Dictionary<Camera, IBloomPrePassParams> _bloomPrePassParamsDict = new Dictionary<Camera, IBloomPrePassParams>();

		private Material _additiveMaterial = null!;
		private KawaseBlurRendererSO? _kawaseBlurRenderer;

		private Renderer _renderer = null!;
		private Mesh _mesh = null!;
		private static readonly int Alpha = Shader.PropertyToID("_Alpha");
		private const int DOWNSAMPLE = 1;
		private const float BLOOM_BOOST_BASE_FACTOR = 0.045f;
		private float? _bloomIntensityConfigSetting;
		private Vector2 _screenDimensions;

		private float BloomIntensity =>
			Mathf.Clamp(_bloomIntensityConfigSetting ?? SettingsStore.Instance.BloomIntensity / 100f, 0f, 2f);

		private void Start()
		{
			UpdateMesh();
			_renderer = GetComponent<Renderer>();

			_kawaseBlurRenderer = Resources.FindObjectsOfTypeAll<KawaseBlurRendererSO>().FirstOrDefault();
			if (_kawaseBlurRenderer == null)
			{
				Log.Error("KawaseBlurRendererSO not found!");
			}
			else
			{
				_additiveMaterial = new Material(_kawaseBlurRenderer._additiveMaterial.shader);
			}

			BSEvents.menuSceneLoaded += UpdateMesh;
			BSEvents.gameSceneLoaded += UpdateMesh;
			BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoaded;

			OnMenuSceneLoaded(null);
		}

		public void UpdateMesh()
		{
			_mesh = GetComponent<MeshFilter>().mesh;
		}

		public void UpdateScreenDimensions(float width, float height)
		{
			_screenDimensions = new Vector2(width, height);
		}

		public void SetBloomIntensityConfigSetting(float? bloomIntensity)
		{
			_bloomIntensityConfigSetting = bloomIntensity;
		}

		private float GetBloomBoost(Camera camera)
		{
			//Base calculation scales down with screen area and up with distance
			var area = _screenDimensions.x * _screenDimensions.y;
			var boost = (BLOOM_BOOST_BASE_FACTOR / (float) Math.Sqrt(Math.Sqrt(area)/GetCameraDistance(camera)));

			//Apply map/user setting on top
			//User-facing setting uses scale of 0-200 (in percent), so divide by 100
			boost *= (float) Math.Sqrt(BloomIntensity);

			//Mitigate extreme amounts of bloom at the edges of the camera frustum when not looking directly at the screen
			var fov = camera.fieldOfView;
			var cameraTransform = camera.transform;
			var targetDirection = gameObject.transform.position - cameraTransform.position;
			var angle = Vector3.Angle(targetDirection, cameraTransform.forward);
			var attenuation = angle / (fov/2);
			//Prevent attenuation from causing brightness fluctuations when looking close to the center
			const float threshold = 0.3f;
			attenuation = Math.Max(threshold, attenuation);
			boost /= ((attenuation + (1 - threshold)));

			//Adjust for FoV
			boost *= fov / 100f;

			return boost;
		}

		private float GetCameraDistance(Camera camera)
		{
			return (gameObject.transform.position - camera.transform.position).magnitude;
		}

		private void GetPrivateFields(Camera camera)
		{
			if (_bloomPrePassDict.ContainsKey(camera))
			{
				return;
			}

			var bloomPrePass = camera.GetComponent<BloomPrePass>();
			_bloomPrePassDict.Add(camera, bloomPrePass);
			if (bloomPrePass == null)
			{
				return;
			}

			_bloomPrePassRendererDict.Add(camera, bloomPrePass._bloomPrepassRenderer);
			_bloomPrePassRenderDataDict.Add(camera, bloomPrePass._renderData);
			var effectsContainer = bloomPrePass._bloomPrePassEffectContainer;
			_bloomPrePassParamsDict.Add(camera, effectsContainer._bloomPrePassEffect);
		}

		public void OnCameraPostRender(Camera camera)
		{
			//intentionally empty
		}

		public void OnCameraPreRender(Camera camera)
		{
			if (camera == null || _kawaseBlurRenderer == null)
			{
				return;
			}

			try
			{
				ApplyBloomEffect(camera);
			}
			catch (Exception e)
			{
				Log.Error(e);
				var result = _bloomPrePassDict.TryGetValue(camera, out var bloomPrePass);
				if (result == false)
				{
					_bloomPrePassDict.Add(camera, null);
				}

				if (bloomPrePass != null)
				{
					_bloomPrePassDict[camera] = null;
				}
			}
		}

		private void ApplyBloomEffect(Camera camera)
		{
			//TODO Fix SmoothCamera instead of skipping. Current workaround is to use CameraPlus instead. Investigate what BloomPrePassRendererSO does differently
			//Mirror cam has no BloomPrePass
			if (camera.name == "SmoothCamera" || camera.name.StartsWith("MirrorCam") || camera.name == "BurnMarksCamera")
			{
				return;
			}

			if (BloomIntensity == 0)
			{
				return;
			}

			try
			{
				GetPrivateFields(camera);
			}
			catch (Exception e)
			{
				Log.Error(e);
				if (!_bloomPrePassDict.ContainsKey(camera))
				{
					_bloomPrePassDict.Add(camera, null);
				}
			}

			_bloomPrePassDict.TryGetValue(camera, out var bloomPrePass);
			if (bloomPrePass == null)
			{
				return;
			}

			var rendererFound = _bloomPrePassRendererDict.TryGetValue(camera, out var bloomPrePassRenderer);
			var paramsFound = _bloomPrePassParamsDict.TryGetValue(camera, out var bloomPrePassParams);
			var renderDataFound = _bloomPrePassRenderDataDict.TryGetValue(camera, out var bloomPrePassRenderData);

			//Never the case in my testing, but better safe than sorry
			if (!rendererFound || !paramsFound || !renderDataFound)
			{
				return;
			}

			var sRGBWrite = GL.sRGBWrite;
			GL.sRGBWrite = false;

			bloomPrePassRenderer!.GetCameraParams(camera, out var projectionMatrix, out _, out var stereoCameraEyeOffset);

			//The next few lines are taken from bloomPrePassRenderer.RenderAndSetData()
			var textureToScreenRatio = new Vector2
			{
				x = Mathf.Clamp01((float) (1.0 / ((double) Mathf.Tan((float) (bloomPrePassParams!.fov.x * 0.5 * (Math.PI / 180.0))) * projectionMatrix.m00))),
				y = Mathf.Clamp01((float) (1.0 / ((double) Mathf.Tan((float) (bloomPrePassParams.fov.y * 0.5 * (Math.PI / 180.0))) * projectionMatrix.m11)))
			};
			projectionMatrix.m00 *= textureToScreenRatio.x;
			projectionMatrix.m02 *= textureToScreenRatio.x;
			projectionMatrix.m11 *= textureToScreenRatio.y;
			projectionMatrix.m12 *= textureToScreenRatio.y;

			var temporary = RenderTexture.GetTemporary(bloomPrePassParams.textureWidth, bloomPrePassParams.textureHeight, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
			Graphics.SetRenderTarget(temporary);
			GL.Clear(true, true, Color.black);

			GL.PushMatrix();
			GL.LoadProjectionMatrix(projectionMatrix);
			_renderer.material.SetPass(0);
			var transformTemp = transform;
			Graphics.DrawMeshNow(_mesh, Matrix4x4.TRS(transformTemp.position, transformTemp.rotation, transformTemp.lossyScale));
			GL.PopMatrix();

			var boost = GetBloomBoost(camera);
			var blur2 = RenderTexture.GetTemporary(bloomPrePassParams.textureWidth >> DOWNSAMPLE, bloomPrePassParams.textureHeight >> DOWNSAMPLE,
				0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
			DoubleBlur(temporary, blur2,
				KawaseBlurRendererSO.KernelSize.Kernel127, boost,
				KawaseBlurRendererSO.KernelSize.Kernel35, boost, 0.5f, DOWNSAMPLE);

			Graphics.Blit(blur2, bloomPrePassRenderData!.bloomPrePassRenderTexture, _additiveMaterial);

			RenderTexture.ReleaseTemporary(temporary);
			RenderTexture.ReleaseTemporary(blur2);

			BloomPrePassRendererSO.SetDataToShaders(stereoCameraEyeOffset, textureToScreenRatio, bloomPrePassRenderData.bloomPrePassRenderTexture, bloomPrePassRenderData.toneMapping);
			GL.sRGBWrite = sRGBWrite;
		}

		public void OnMenuSceneLoaded(ScenesTransitionSetupDataSO scenesTransitionSetupDataSo)
		{
			_kawaseBlurRenderer = Resources.FindObjectsOfTypeAll<KawaseBlurRendererSO>().FirstOrDefault();
			if (_kawaseBlurRenderer == null)
			{
				Log.Error("KawaseBlurRendererSO not found!");
			}
			else
			{
				_additiveMaterial = new Material(_kawaseBlurRenderer._additiveMaterial.shader);
			}

			UpdateMesh();
		}

		public void OnEnable()
		{
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Combine(Camera.onPreRender, new Camera.CameraCallback(OnCameraPreRender));
		}

		public void OnDisable()
		{
			Camera.onPreRender = (Camera.CameraCallback)Delegate.Remove(Camera.onPreRender, new Camera.CameraCallback(OnCameraPreRender))!;
		}

		private void OnDestroy()
		{
			BSEvents.menuSceneLoaded -= UpdateMesh;
			BSEvents.gameSceneLoaded -= UpdateMesh;
			BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoaded;
		}

		private void DoubleBlur(RenderTexture src, RenderTexture dest, KawaseBlurRendererSO.KernelSize kernelSize0, float boost0, KawaseBlurRendererSO.KernelSize kernelSize1, float boost1, float secondBlurAlpha, int downsample)
		{
			if (_kawaseBlurRenderer == null)
			{
				return;
			}

			var blurKernel = _kawaseBlurRenderer.GetBlurKernel(kernelSize0);
			var blurKernel2 = _kawaseBlurRenderer.GetBlurKernel(kernelSize1);
			var num = 0;
			while (num < blurKernel.Length && num < blurKernel2.Length && blurKernel[num] == blurKernel2[num])
			{
				num++;
			}
			var width = src.width >> downsample;
			var height = src.height >> downsample;
			var descriptor = src.descriptor;
			descriptor.depthBufferBits = 0;
			descriptor.width = width;
			descriptor.height = height;
			var temporary = RenderTexture.GetTemporary(descriptor);
			_kawaseBlurRenderer.Blur(src, temporary, blurKernel, 0f, downsample, 0, num, 0f, 1f, false, true, KawaseBlurRendererSO.WeightsType.None);
			_kawaseBlurRenderer.Blur(temporary, dest, blurKernel, boost0, 0, num, blurKernel.Length - num, 0f, 1f, false, true, KawaseBlurRendererSO.WeightsType.None);
			_kawaseBlurRenderer.Blur(temporary, dest, blurKernel2, boost1, 0, num, blurKernel2.Length - num, 0f, secondBlurAlpha, true, true, KawaseBlurRendererSO.WeightsType.None);
			RenderTexture.ReleaseTemporary(temporary);
		}
	}
}