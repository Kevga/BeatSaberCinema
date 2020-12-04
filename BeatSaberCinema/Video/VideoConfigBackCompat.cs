using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BeatSaberCinema
{
	[Serializable]
	[SuppressMessage("ReSharper", "NotAccessedField.Global")]
	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public class VideoConfigBackCompat
	{
		public string? title;
		public string? author;
		public string? description;
		public string? duration; //s
		public string? URL;
		public string? thumbnailURL;
		public bool loop;
		public int offset; //ms
		public string? videoPath;
		public bool hasBeenCut;
		public bool needsCut;
		public string? cutCommand;
		public string[]? cutVideoArgs = { "", "", "" };
		public string? cutVideoPath;
	}

	[Serializable]
	public class VideoConfigListBackCompat
	{
		public int activeVideo;
		public List<VideoConfigBackCompat>? videos;
	}
}