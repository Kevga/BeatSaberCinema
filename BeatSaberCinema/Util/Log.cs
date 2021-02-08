using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace BeatSaberCinema
{
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public static class Log
	{
		internal static IPA.Logging.Logger IpaLogger = null!;

		[Conditional("DEBUG")]
		public static void Debug(string message)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Debug, message);
		}

		[Conditional("DEBUG")]
		public static void Debug(Exception exception)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Debug, exception);
		}

		public static void Info(string message)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Info, message);
		}

		public static void Info(Exception exception)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Info, exception);
		}

		public static void Warn(string message)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Warning, message);
		}

		public static void Warn(Exception exception)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Warning, exception);
		}

		public static void Error(string message)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Error, message);
		}

		public static void Error(Exception exception)
		{
			IpaLogger.Log(IPA.Logging.Logger.Level.Error, exception);
		}
	}
}