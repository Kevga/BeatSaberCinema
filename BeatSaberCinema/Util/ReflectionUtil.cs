using System;
using System.Linq;
using System.Reflection;

namespace BeatSaberCinema
{
	public static class ReflectionUtil
	{
		private static Assembly? FindAssembly(string assemblyName)
		{
			return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == assemblyName);
		}

		public static Type? FindType(string assemblyName, string qualifiedTypeName)
		{
			var asm = FindAssembly(assemblyName);
			var t = asm?.GetType(qualifiedTypeName);
			return t ?? null;
		}

		public static Type? FindType(Assembly asm, string qualifiedTypeName)
		{
			var t = asm?.GetType(qualifiedTypeName);
			return t ?? null;
		}

		public static EventInfo? FindEvent(string assemblyName, string qualifiedTypeName, string eventName)
		{
			var type = FindType(assemblyName, qualifiedTypeName);
			return type?.GetEvent(eventName);
		}

		public static object? AddDelegateToStaticType(EventInfo eventInfo, Delegate @delegate)
		{
			var addMethod = eventInfo.GetAddMethod();
			object[] addHandlerArgs = { @delegate };
			return addMethod.Invoke(null, addHandlerArgs);
		}
	}
}