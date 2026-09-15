namespace VsixGallery
{
	public sealed record ValidationFinding(string Severity, string Code, string Message)
	{
		public static ValidationFinding Warning(string code, string message)
		{
			ValidationRules.Get(code);
			return new("warning", code, message);
		}

		public static ValidationFinding Error(string code, string message) =>
			new("error", code, message);
	}
}
