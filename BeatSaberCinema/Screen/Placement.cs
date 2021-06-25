using System;
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

		public static Placement SoloGameplayPlacement => new Placement(
			new Vector3(0, 12.4f, 67.8f),
			new Vector3(-8, 0, 0),
			25f
		);

		public static Placement MultiplayerPlacement => new Placement(
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

		public Placement(Vector3 position, Vector3 rotation, float height, float? width = null, float? curvature = null)
		{
			Position = position;
			Rotation = rotation;
			Height = height;
			Width = width ?? (height * (16f / 9f));
		}

		public Placement(VideoConfig? config, PlaybackController.Scene scene, float aspectRatio)
		{
			var defaultPlacement = GetDefaultPlacementForScene(scene);
			Position = config?.screenPosition ?? defaultPlacement.Position;
			Rotation = config?.screenRotation ?? defaultPlacement.Rotation;
			Height = config?.screenHeight ?? defaultPlacement.Height;
			Width = Height * aspectRatio;
			Curvature = config?.screenCurvature;
		}

		public static Placement GetDefaultPlacementForScene(PlaybackController.Scene scene)
		{
			return scene switch
			{
				PlaybackController.Scene.SoloGameplay => SoloGameplayPlacement,
				PlaybackController.Scene.MultiplayerGameplay => MultiplayerPlacement,
				PlaybackController.Scene.Menu => MenuPlacement,
				_ => SoloGameplayPlacement
			};
		}
	}
}