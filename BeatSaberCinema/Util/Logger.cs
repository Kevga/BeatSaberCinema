using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace BeatSaberCinema
{
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public class Logger
	{
		private readonly IPA.Logging.Logger _logger;

		public Logger(IPA.Logging.Logger logger)
		{
			_logger = logger;
		}

		[Conditional("DEBUG")]
		public void Debug(string message)
		{
			_logger.Log(IPA.Logging.Logger.Level.Debug, message);
		}

		[Conditional("DEBUG")]
		public void Debug(Exception exception)
		{
			_logger.Log(IPA.Logging.Logger.Level.Debug, exception);
		}

		public void Info(string message)
		{
			_logger.Log(IPA.Logging.Logger.Level.Info, message);
		}

		public void Info(Exception exception)
		{
			_logger.Log(IPA.Logging.Logger.Level.Info, exception);
		}

		public void Warn(string message)
		{
			_logger.Log(IPA.Logging.Logger.Level.Warning, message);
		}

		public void Warn(Exception exception)
		{
			_logger.Log(IPA.Logging.Logger.Level.Warning, exception);
		}

		public void Error(string message)
		{
			_logger.Log(IPA.Logging.Logger.Level.Error, message);
		}

		public void Error(Exception exception)
		{
			_logger.Log(IPA.Logging.Logger.Level.Error, exception);
		}
	}
}