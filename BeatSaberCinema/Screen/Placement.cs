using UnityEngine;

namespace BeatSaberCinema
{
	public class Placement
	{
		public Vector3 Position;
		public Vector3 Rotation;
		public float Height;
		public float Width;
		public float? Curvature;
		public bool? CurveYAxis;
		public int? Subsurfaces;

		private static Placement SoloGameplayPlacement => new Placement(
			new Vector3(0, 12.4f, 67.8f),
			new Vector3(-7, 0, 0),
			25f
		);

		private static Placement MultiplayerPlacement => new Placement(
			new Vector3(0, 5f, 67f),
			new Vector3(-5, 0, 0),
			17f
		);

		public static Placement MenuPlacement => new Placement(
			new Vector3(0, 4f, 16),
			new Vector3(0, 0, 0),
			8f
		);

		public static Placement CoverPlacement => new Placement(
			new Vector3(0, 5.9f, 75f),
			new Vector3(-8, 0, 0),
			12f
		);

		private Placement(Vector3 position, Vector3 rotation, float height, float? width = null, float? curvature = null)
		{
			Position = position;
			Rotation = rotation;
			Height = height;
			Width = width ?? (height * (16f / 9f));
			Curvature = curvature;
		}

		public static Placement CreatePlacementForConfig(VideoConfig? config, PlaybackController.Scene scene, float aspectRatio)
		{
			var defaultPlacement = GetDefaultPlacementForScene(scene);
			if (scene == PlaybackController.Scene.MultiplayerGameplay || config == null)
			{
				return defaultPlacement;
			}

			var placement = new Placement(
				config.screenPosition ?? defaultPlacement.Position,
				config.screenRotation ?? defaultPlacement.Rotation,
				config.screenHeight ?? defaultPlacement.Height
			);

			placement.Width = placement.Height * aspectRatio;
			placement.Curvature = config.screenCurvature ?? defaultPlacement.Curvature;
			placement.Subsurfaces = config.screenSubsurfaces;
			placement.CurveYAxis = config.curveYAxis;

			return placement;
		}

		public static Placement GetDefaultPlacementForScene(PlaybackController.Scene scene)
		{
			return scene switch
			{
				PlaybackController.Scene.SoloGameplay => (GetDefaultEnvironmentPlacement() ?? SoloGameplayPlacement),
				PlaybackController.Scene.MultiplayerGameplay => MultiplayerPlacement,
				PlaybackController.Scene.Menu => MenuPlacement,
				_ => SoloGameplayPlacement
			};
		}

		private static Placement? GetDefaultEnvironmentPlacement()
		{
			return Util.GetEnvironmentName() switch
			{
				"LinkinParkEnvironment" => new Placement(new Vector3(0f, 6.2f, 52.7f), Vector3.zero, 16f, null, 0f),
				"BTSEnvironment" => new Placement(new Vector3(0, 12.4f, 80f), new Vector3(-7, 0, 0), 25f),
				"OriginsEnvironment" => new Placement(new Vector3(0, 12.4f, 66.7f), new Vector3(-7, 0, 0), 25f),
				"KaleidoscopeEnvironment" => new Placement(new Vector3(0f, -0.5f, 35f), Vector3.zero, 12f),
				"InterscopeEnvironment" => new Placement(new Vector3(0f, 6.3f, 37f), Vector3.zero, 12.5f),
				"CrabRaveEnvironment" => new Placement(new Vector3(0f, 5.46f, 40f), new Vector3(-5f, 0f, 0f), 13f),
				"MonstercatEnvironment" => new Placement(new Vector3(0f, 5.46f, 40f), new Vector3(-5f, 0f, 0f), 13f),
				"SkrillexEnvironment" => new Placement(new Vector3(0f, 1.5f, 40f), Vector3.zero, 12f),
				"WeaveEnvironment" => new Placement(new Vector3(0f, 1.5f, 21f), Vector3.zero, 4.3f, null, 0f),
				"PyroEnvironment" => new Placement(new Vector3(0f, 12f, 60f), Vector3.zero, 24f, null, 0f),
				"EDMEnvironment" => new Placement(new Vector3(0f, 1.5f, 25f), Vector3.zero, 8f),
				"LizzoEnvironment" => new Placement(new Vector3(0f, 8f, 63f), Vector3.zero, 16f),
				"Dragons2Environment" => new Placement(new Vector3(0f, 5.8f, 67f), Vector3.zero, 33f),
				_ => null
			};
		}
	}
}