using System.Linq;
using UnityEngine;

namespace BeatSaberCinema
{
	public class Screen: MonoBehaviour
	{
		private readonly GameObject _screenSurface;
		private readonly Renderer _screenRenderer;
		private readonly GameObject _screenBody;

		public Screen()
		{
			_screenSurface = GameObject.CreatePrimitive(PrimitiveType.Quad);
			_screenSurface.name = "CinemaScreen";
			_screenSurface.layer = LayerMask.NameToLayer("Environment"); //Causes screen to be reflected on the ground
			_screenRenderer = _screenSurface.GetComponent<Renderer>();
			_screenBody = CreateBody();
			_screenSurface.AddComponent<CustomBloomPrePass>();
		}

		private GameObject CreateBody()
		{
			GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (body.GetComponent<Collider>() != null)
            {
	            Destroy(body.GetComponent<Collider>());
            }

            body.name = "CinemaScreenBody";
            body.transform.parent = _screenSurface.transform;
            body.transform.localPosition = new Vector3(0, 0, 1f);
            body.transform.localScale = new Vector3(1.05f, 1.05f, 1f);
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.material = new Material(Resources.FindObjectsOfTypeAll<Material>()
                .Last(x => x.name == "DarkEnvironmentSimple"));
            body.layer = LayerMask.NameToLayer("Environment");

            return body;
		}

		public void Show()
		{
			_screenSurface.SetActive(true);
		}

		public void Hide()
		{
			_screenSurface.SetActive(false);
		}

		public Renderer GetRenderer()
		{
			return _screenRenderer;
		}

		public void SetTransform(Transform parentTransform)
		{
			_screenSurface.transform.parent = parentTransform;
		}

		public void SetPlacement(Vector3 pos, Vector3 rot, Vector3 scale)
		{
			_screenSurface.transform.position = pos;
			_screenSurface.transform.eulerAngles = rot;
			_screenSurface.transform.localScale = scale;
		}

		public void SetAspectRatio(float ratio)
		{
			var localScale = _screenSurface.transform.localScale;
			localScale =
				new Vector3(
					localScale.y * ratio,
					localScale.y,
					localScale.z);
			_screenSurface.transform.localScale = localScale;
		}
	}
}