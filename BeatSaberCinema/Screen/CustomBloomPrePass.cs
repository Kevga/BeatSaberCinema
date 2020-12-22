using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BS_Utils.Utilities;

namespace BeatSaberCinema
{
	internal class CustomBloomPrePass : MonoBehaviour, CameraRenderCallbacksManager.ICameraRenderCallbacks
	{
		//Most cameras have their own BloomPrePass, so save one for each camera and use that when rendering the bloom for the camera
		private readonly Dictionary<Camera, BloomPrePass?> _bloomPrePassDict = new Dictionary<Camera, BloomPrePass?>();
		private readonly Dictionary<Camera, BloomPrePassRendererSO> _bloomPrePassRendererDict = new Dictionary<Camera, BloomPrePassRendererSO>();
		private readonly Dictionary<Camera, BloomPrePassRenderDataSO.Data> _bloomPrePassRenderDataDict = new Dictionary<Camera, BloomPrePassRenderDataSO.Data>();
		private readonly Dictionary<Camera, IBloomPrePassParams> _bloomPrePassParamsDict = new Dictionary<Camera, IBloomPrePassParams>();

		private Material _additiveMaterial = null!;
		private KawaseBlurRendererSO _kawaseBlurRenderer = null!;

		private Renderer _renderer = null!;
		private Mesh _mesh = null!;
		private static readonly int Alpha = Shader.PropertyToID("_Alpha");
		private const int DOWNSAMPLE = 2;
		private const float BLOOM_BOOST_FACTOR = 0.08f;
		private float _boost1;
		private float _boost2;

		private void Start()
		{
			_mesh = GetComponent<MeshFilter>().mesh;
			_renderer = GetComponent<Renderer>();

			KawaseBloomMainEffectSO kawaseBloomMainEffect = Resources.FindObjectsOfTypeAll<KawaseBloomMainEffectSO>().First();
			_kawaseBlurRenderer = kawaseBloomMainEffect.GetPrivateField<KawaseBlurRendererSO>("_kawaseBlurRenderer");
			_additiveMaterial = new Material(Shader.Find("Hidden/BlitAdd"));
			_additiveMaterial.SetFloat(Alpha, 1f);

			BSEvents.menuSceneLoaded += UpdateMesh;
			BSEvents.gameSceneLoaded += UpdateMesh;
			BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoaded;
		}

		public void UpdateMesh()
		{
			_mesh = GetComponent<MeshFilter>().mesh;
			_renderer = GetComponent<Renderer>();
		}

		public void UpdateBloomBoost(float screenHeight, float screenDistance)
		{
			var boost = (BLOOM_BOOST_FACTOR / (float) Math.Sqrt(screenHeight/screenDistance)) * (float) Math.Sqrt(SettingsStore.Instance.BloomIntensity/100f);
			_boost1 = _boost2 = boost;
			Plugin.Logger.Debug("Set bloom boost to "+boost);
		}

		private void GetPrivateFields(Camera camera)
		{
			if (_bloomPrePassDict.ContainsKey(camera))
			{
				return;
			}

			BloomPrePass bloomPrePass;
			try
			{
				bloomPrePass = Resources.FindObjectsOfTypeAll<BloomPrePass>().First(x => x.name == camera.name);
			}
			catch (Exception)
			{
				_bloomPrePassDict.Add(camera, null);
				return;
			}

			_bloomPrePassDict.Add(camera, bloomPrePass);
			_bloomPrePassRendererDict.Add(camera, bloomPrePass.GetPrivateField<BloomPrePassRendererSO>("_bloomPrepassRenderer"));
			_bloomPrePassRenderDataDict.Add(camera, bloomPrePass.GetPrivateField<BloomPrePassRenderDataSO.Data>("_renderData"));
			var effectsContainer = bloomPrePass.GetPrivateField<BloomPrePassEffectContainerSO>("_bloomPrePassEffectContainer");
			_bloomPrePassParamsDict.Add(camera, effectsContainer.GetPrivateField<BloomPrePassEffectSO>("_bloomPrePassEffect"));
		}

		public void OnCameraPostRender(Camera camera)
		{
			//intentionally empty
		}

		public void OnCameraPreRender(Camera camera)
		{
			if (camera == null)
			{
				return;
			}

			try
			{
				ApplyBloomEffect(camera);
			}
			catch (Exception e)
			{
				Plugin.Logger.Error(e);
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
			//TODO Fix this instead of skipping. Current workaround is to use CameraPlus instead. Investigate what BloomPrePassRendererSO does differently
			if (camera.name == "SmoothCamera")
			{
				return;
			}

			GetPrivateFields(camera);
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

			bloomPrePassRenderer.GetCameraParams(camera, out var projectionMatrix, out _, out var stereoCameraEyeOffset);

			//The next few lines are taken from bloomPrePassRenderer.RenderAndSetData()
			var textureToScreenRatio = new Vector2
			{
				x = Mathf.Clamp01((float) (1.0 / ((double) Mathf.Tan((float) (bloomPrePassParams.fov.x * 0.5 * (Math.PI / 180.0))) * projectionMatrix.m00))),
				y = Mathf.Clamp01((float) (1.0 / ((double) Mathf.Tan((float) (bloomPrePassParams.fov.y * 0.5 * (Math.PI / 180.0))) * projectionMatrix.m11)))
			};
			projectionMatrix.m00 *= textureToScreenRatio.x;
			projectionMatrix.m02 *= textureToScreenRatio.x;
			projectionMatrix.m11 *= textureToScreenRatio.y;
			projectionMatrix.m12 *= textureToScreenRatio.y;

			RenderTexture temporary = RenderTexture.GetTemporary(bloomPrePassParams.textureWidth, bloomPrePassParams.textureHeight, 0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
			Graphics.SetRenderTarget(temporary);
			GL.Clear(true, true, Color.black);

			GL.PushMatrix();
			GL.LoadProjectionMatrix(projectionMatrix);
			_renderer.material.SetPass(0);
			var transformTemp = transform;
			Graphics.DrawMeshNow(_mesh, Matrix4x4.TRS(transformTemp.position, transformTemp.rotation, transformTemp.lossyScale));
			GL.PopMatrix();

			RenderTexture blur2 = RenderTexture.GetTemporary(bloomPrePassParams.textureWidth >> DOWNSAMPLE, bloomPrePassParams.textureHeight >> DOWNSAMPLE,
				0, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
			DoubleBlur(temporary, blur2,
				KawaseBlurRendererSO.KernelSize.Kernel127, _boost1,
				KawaseBlurRendererSO.KernelSize.Kernel35, _boost2, 0.5f, DOWNSAMPLE);

			Graphics.Blit(blur2, bloomPrePassRenderData.bloomPrePassRenderTexture, _additiveMaterial);

			RenderTexture.ReleaseTemporary(temporary);
			RenderTexture.ReleaseTemporary(blur2);

			BloomPrePassRendererSO.SetDataToShaders(stereoCameraEyeOffset, textureToScreenRatio, bloomPrePassRenderData.bloomPrePassRenderTexture);
			GL.sRGBWrite = sRGBWrite;
		}

		public void OnMenuSceneLoaded(ScenesTransitionSetupDataSO scenesTransitionSetupDataSo)
		{
			UpdateMesh();
		}

		private void OnWillRenderObject() {
			CameraRenderCallbacksManager.RegisterForCameraCallbacks(Camera.current, this);
		}

		public void OnDisable()
		{
			CameraRenderCallbacksManager.UnregisterFromCameraCallbacks(this);
		}

		private void OnDestroy()
		{
			CameraRenderCallbacksManager.UnregisterFromCameraCallbacks(this);
		}

		private void DoubleBlur(RenderTexture src, RenderTexture dest, KawaseBlurRendererSO.KernelSize kernelSize0, float boost0, KawaseBlurRendererSO.KernelSize kernelSize1, float boost1, float secondBlurAlpha, int downsample)
		{
			int[] blurKernel = _kawaseBlurRenderer.GetBlurKernel(kernelSize0);
			int[] blurKernel2 = _kawaseBlurRenderer.GetBlurKernel(kernelSize1);
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
			RenderTexture temporary = RenderTexture.GetTemporary(descriptor);
			_kawaseBlurRenderer.Blur(src, temporary, blurKernel, 0f, downsample, 0, num, 0f, 1f, false, true, KawaseBlurRendererSO.WeightsType.None);
			_kawaseBlurRenderer.Blur(temporary, dest, blurKernel, boost0, 0, num, blurKernel.Length - num, 0f, 1f, false, true, KawaseBlurRendererSO.WeightsType.None);
			_kawaseBlurRenderer.Blur(temporary, dest, blurKernel2, boost1, 0, num, blurKernel2.Length - num, 0f, secondBlurAlpha, true, true, KawaseBlurRendererSO.WeightsType.None);
			RenderTexture.ReleaseTemporary(temporary);
		}
	}
}