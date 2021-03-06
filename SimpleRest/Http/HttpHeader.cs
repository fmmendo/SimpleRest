﻿namespace SimpleRest
{
	/// <summary>
	/// Representation of an HTTP header
	/// </summary>
	public sealed class HttpHeader
	{
		/// <summary>
		/// Name of the header
		/// </summary>
		public string Name { get; set; }
		/// <summary>
		/// Value of the header
		/// </summary>
		public string Value { get; set; }
	}
}
