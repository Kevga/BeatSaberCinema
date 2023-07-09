using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using IPA.Logging;

namespace BeatSaberCinema
{
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public static class Log
	{
		internal static Logger IpaLogger = null!;

#if DEBUG
		private static void _Log(string message, Logger.Level logLevel, string filePath, string member, int line)
		{
			var pathParts = filePath.Split('\\');
			var className = pathParts[pathParts.Length - 1].Replace(".cs", "");
			var caller = new StackFrame(3, true).GetMethod().Name;
			var prefix = $"[{caller}->{className}.{member}:{line}]: ";
			IpaLogger.Log(logLevel, $"{prefix}{message}");
		}

		public static void Debug(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			_Log(message, Logger.Level.Debug, filePath, member, line);
		}

		public static void Debug(string message, bool evenInReleaseBuild, [CallerFilePath] string filePath = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			_Log(message, Logger.Level.Debug, filePath, member, line);
		}

		public static void Info(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			_Log(message, Logger.Level.Info, filePath, member, line);
		}

		public static void Warn(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			_Log(message, Logger.Level.Warning, filePath, member, line);
		}

		public static void Error(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
		{
			_Log(message, Logger.Level.Error, filePath, member, line);
		}
#else
		private static void _Log(string message, Logger.Level logLevel)
		{
			IpaLogger.Log(logLevel, message);
		}

		public static void Debug(string message, bool evenInReleaseBuild)
		{
			_Log(message, Logger.Level.Debug);
		}

		public static void Info(string message)
		{
			_Log(message, Logger.Level.Info);
		}

		public static void Warn(string message)
		{
			_Log(message, Logger.Level.Warning);
		}

		public static void Error(string message)
		{
			_Log(message, Logger.Level.Error);
		}
#endif
		[Conditional("DEBUG")]
		public static void Debug(Exception exception)
		{
			IpaLogger.Log(Logger.Level.Debug, exception);
		}

		[Conditional("DEBUG")]
		public static void Debug(string message)
		{
			IpaLogger.Log(Logger.Level.Debug, message);
		}

		public static void Info(Exception exception)
		{
			IpaLogger.Log(Logger.Level.Info, exception);
		}

		public static void Warn(Exception exception)
		{
			IpaLogger.Log(Logger.Level.Warning, exception);
		}

		public static void Error(Exception exception)
		{
			IpaLogger.Log(Logger.Level.Error, exception);
		}
	}
}