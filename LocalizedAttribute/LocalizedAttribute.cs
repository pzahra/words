using System;

namespace PatTech.Localization
{
	[AttributeUsage(
		AttributeTargets.Parameter
		| AttributeTargets.ReturnValue
		| AttributeTargets.Property
		| AttributeTargets.Field,
		AllowMultiple = false)]
	public class LocalizedAttribute : Attribute { }
}
