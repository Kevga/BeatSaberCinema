using System;
using UnityEngine;

namespace BeatSaberCinema
{
	//Loosely based on https://gist.github.com/mfav/8cdcc922d1a75d0a7a7abf5d46e23ef0

	[RequireComponent(typeof(MeshFilter))]
	[RequireComponent(typeof(MeshRenderer))]
	public class CurvedSurface : MonoBehaviour
	{
		private class MeshData
		{
			public Vector3[] Vertices { get; set; } = null!;
			public int[] Triangles { get; set; } = null!;
			public Vector2[] UVs { get; set; } = null!;
		}

		private const float MIN_CURVATURE = 0.0001f;

		private float _radius;
		private float _distance;
		public float Distance
		{
			get => _distance;
			set
			{
				_distance = value;
				UpdateRadius();
				Generate();
			}
		}

		private float _width;
		public float Width
		{
			get => _width;
			set
			{
				_width = value;
				UpdateRadius();
			}
		}

		public float Height { get; set; }

		private float? _curvatureDegreesFixed;
		private float _curvatureDegreesAutomatic;
		private float CurvatureDegrees => _curvatureDegreesFixed ?? _curvatureDegreesAutomatic;

		private const int SUBSURFACE_COUNT = 32;

		public void Initialize(float width, float height, float distance, float? curvatureDegrees)
		{
			if (curvatureDegrees != null)
			{
				//Limit range and prevent infinities and div/0
				curvatureDegrees = Math.Max(MIN_CURVATURE, curvatureDegrees.Value);
				curvatureDegrees = Math.Min(360, curvatureDegrees.Value);
			}

			_curvatureDegreesFixed = curvatureDegrees;
			_width = width;
			Height = height;
			_distance = distance;
			UpdateRadius();
		}

		private void Update()
		{
			var distance = transform.position.z;
			if (Math.Abs(_distance - distance) < 0.01f)
			{
				return;
			}

			Distance = distance;
		}

		private void UpdateRadius()
		{
			_curvatureDegreesAutomatic = MIN_CURVATURE;
			if (_curvatureDegreesFixed != null || !SettingsStore.Instance.CurvedScreen)
			{
				_radius = (float) (GetCircleFraction() / (2 * Math.PI)) * Width;
			}
			else
			{
				_radius = Distance;
				_curvatureDegreesAutomatic = (float) (360/(((2 * Math.PI) * _radius) / _width));
			}
		}

		private float GetCircleFraction()
		{
			var circleFraction = float.MaxValue;
			if (CurvatureDegrees > 0)
			{
				circleFraction = 360f / CurvatureDegrees;
			}

			return circleFraction;
		}

		public void Generate()
		{
			var surface = CreateSurface();
			UpdateMeshFilter(surface);
		}

		private MeshData CreateSurface()
		{
			var surface = new MeshData
			{
				Vertices = new Vector3[(SUBSURFACE_COUNT + 2)*2],
				UVs = new Vector2[(SUBSURFACE_COUNT + 2)*2],
				Triangles = new int[SUBSURFACE_COUNT*6]
			};

			int i,j;
			for (i = j = 0; i < SUBSURFACE_COUNT+1; i++)
			{
				GenerateVertexPair(surface, i);

				if (i >= SUBSURFACE_COUNT)
				{
					continue;
				}

				ConnectVertices(surface, i, ref j);
			}

			return surface;
		}

		private void UpdateMeshFilter(MeshData surface)
		{
			var filter = GetComponent<MeshFilter>();

			var mesh = new Mesh
			{
				vertices = surface.Vertices,
				triangles = surface.Triangles
			};

			mesh.SetUVs(0, surface.UVs);
			filter.mesh = mesh;
		}

		private void GenerateVertexPair(MeshData surface, int i)
		{
			var segmentDistance = ((float)i) / SUBSURFACE_COUNT;
			var arcDegrees = CurvatureDegrees  * Mathf.Deg2Rad;
			var theta = -0.5f + segmentDistance;

			var x = Mathf.Sin(theta * arcDegrees) * _radius;
			var z = (Mathf.Cos(theta * arcDegrees) * _radius) - _radius;

			surface.Vertices[i] = new Vector3(x, Height / 2f, z);
			surface.Vertices[i + SUBSURFACE_COUNT + 1] = new Vector3(x, -Height / 2f, z);
			surface.UVs[i] = new Vector2(i / (float)SUBSURFACE_COUNT, 1);
			surface.UVs[i + SUBSURFACE_COUNT + 1] = new Vector2(i / (float)SUBSURFACE_COUNT, 0);
		}

		private static void ConnectVertices(MeshData surface, int i, ref int j)
		{
			//Left triangle
			surface.Triangles[j++] = i;
			surface.Triangles[j++] = i + 1;
			surface.Triangles[j++] = i + SUBSURFACE_COUNT + 1;

			//Right triangle
			surface.Triangles[j++] = i + 1;
			surface.Triangles[j++] = i + SUBSURFACE_COUNT + 2;
			surface.Triangles[j++] = i + SUBSURFACE_COUNT + 1;
		}
	}
}