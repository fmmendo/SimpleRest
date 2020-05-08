﻿#region License
//   Copyright 2010 John Sheehan
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License. 
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Mendo.UWP.Extensions;

namespace SimpleRest
{
	/// <summary>
	/// Client to translate RestRequests into Http requests and process response result
	/// </summary>
	public sealed partial class RestClient : IRestClient
	{
        Version version;

        public IHttpFactory HttpFactory { get { return new Http(); } }

		/// <summary>
		/// Default constructor that registers default content handlers
		/// </summary>
		public RestClient()
		{
            var asmName = this.GetType().AssemblyQualifiedName;
            var versionExpression = new System.Text.RegularExpressions.Regex("Version=(?<version>[0-9.]*)");
            var m = versionExpression.Match(asmName);
            if (m.Success)
            {
                version = new Version(m.Groups["version"].Value);
            }
			DefaultParameters = new List<Parameter>();
			FollowRedirects = true;
		}

		/// <summary>
		/// Sets the BaseUrl property for requests made by this client instance
		/// </summary>
		/// <param name="baseUrl"></param>
		public RestClient(string baseUrl)
			: this()
		{
			BaseUrl = baseUrl;
		}

		/// <summary>
		/// Parameters included with every request made with this instance of RestClient
		/// If specified in both client and request, the request wins
		/// </summary>
		public IList<Parameter> DefaultParameters { get; private set; }

		/// <summary>
		/// Maximum number of redirects to follow if FollowRedirects is true
		/// </summary>
		public int? MaxRedirects { get; set; }

		/// <summary>
		/// Default is true. Determine whether or not requests that result in 
		/// HTTP status codes of 3xx should follow returned redirect
		/// </summary>
		public bool FollowRedirects { get; set; }

		/// <summary>
		/// UserAgent to use for requests made by this client instance
		/// </summary>
		public string UserAgent { get; set; }

		/// <summary>
		/// Timeout in milliseconds to use for requests made by this client instance
		/// </summary>
		public int Timeout { get; set; }

		/// <summary>
		/// Whether to invoke async callbacks using the SynchronizationContext.Current captured when invoked
		/// </summary>
		public bool UseSynchronizationContext { get; set; }

		/// <summary>
		/// Authenticator to use for requests made by this client instance
		/// </summary>
		public IAuthenticator Authenticator { get; set; }

		private string _baseUrl;
		/// <summary>
		/// Combined with Request.Resource to construct URL for request
		/// Should include scheme and domain without trailing slash.
		/// </summary>
		/// <example>
		/// client.BaseUrl = "http://example.com";
		/// </example>
		public string BaseUrl
		{
			get
			{
				return _baseUrl;
			}
			set
			{
				_baseUrl = value;
				if (_baseUrl != null && _baseUrl.EndsWith("/"))
				{
					_baseUrl = _baseUrl.Substring(0, _baseUrl.Length - 1);
				}
			}
		}

		private void AuthenticateIfNeeded(RestClient client, IRestRequest request)
		{
			if (Authenticator != null)
			{
				Authenticator.Authenticate(client, request);
			}
		}

		/// <summary>
		/// Assembles URL to call based on parameters, method and resource
		/// </summary>
		/// <param name="request">RestRequest to execute</param>
		/// <returns>Assembled System.Uri</returns>
		public Uri BuildUri(IRestRequest request)
		{
			var assembled = request.Resource;
			var urlParms = request.Parameters.Where(p => p.Type == ParameterType.UrlSegment);
			foreach (var p in urlParms)
			{
				assembled = assembled.Replace("{" + p.Name + "}", p.Value.ToString().UrlEncode());
			}

			if (!string.IsNullOrEmpty(assembled) && assembled.StartsWith("/"))
			{
				assembled = assembled.Substring(1);
			}

			if (!string.IsNullOrEmpty(BaseUrl))
			{
				if (string.IsNullOrEmpty(assembled))
				{
					assembled = BaseUrl;
				}
				else
				{
					assembled = string.Format("{0}/{1}", BaseUrl, assembled);
				}
			}

			if (request.Method != Method.POST 
					&& request.Method != Method.PUT 
					&& request.Method != Method.PATCH)
			{
				// build and attach querystring if this is a get-style request
				if (request.Parameters.Any(p => p.Type == ParameterType.GetOrPost))
				{
					if (assembled.EndsWith("/"))
					{
						assembled = assembled.Substring(0, assembled.Length - 1);
					}

					var data = EncodeParameters(request);
					assembled = string.Format("{0}?{1}", assembled, data);
				}
			}

			return new Uri(assembled);
		}

		private string EncodeParameters(IRestRequest request)
		{
			var querystring = new StringBuilder();
			foreach (var p in request.Parameters.Where(p => p.Type == ParameterType.GetOrPost))
			{
				if (querystring.Length > 1)
					querystring.Append("&");
				querystring.AppendFormat("{0}={1}", p.Name.UrlEncode(), (p.Value.ToString()).UrlEncode());
			}

			return querystring.ToString();
		}

		private void ConfigureHttp(IRestRequest request, IHttp http)
		{
			// move RestClient.DefaultParameters into Request.Parameters
			foreach(var p in DefaultParameters)
			{
				if(request.Parameters.Any(p2 => p2.Name == p.Name && p2.Type == p.Type))
				{
					continue;
				}

				request.AddParameter(p);
			}

			http.Url = BuildUri(request);

			var userAgent = UserAgent ?? http.UserAgent;
			http.UserAgent = string.IsNullOrEmpty(userAgent) ? userAgent : "RestSharp " + version.ToString();

			var timeout = request.Timeout > 0 ? request.Timeout : Timeout;
			if (timeout > 0)
			{
				http.Timeout = timeout;
			}

			var headers = from p in request.Parameters
						  where p.Type == ParameterType.HttpHeader
						  select new HttpHeader
						  {
							  Name = p.Name,
							  Value = p.Value.ToString()
						  };

			foreach(var header in headers)
			{
				http.Headers.Add(header);
			}

			var cookies = from p in request.Parameters
						  where p.Type == ParameterType.Cookie
						  select new HttpCookie
						  {
							  Name = p.Name,
							  Value = p.Value.ToString()
						  };

			foreach(var cookie in cookies)
			{
				http.Cookies.Add(cookie);
			}

			var @params = from p in request.Parameters
						  where p.Type == ParameterType.GetOrPost
								&& p.Value != null
						  select new HttpParameter
						  {
							  Name = p.Name,
							  Value = p.Value.ToString()
						  };

			foreach(var parameter in @params)
			{
				http.Parameters.Add(parameter);
			}

			var body = (from p in request.Parameters
						where p.Type == ParameterType.RequestBody
						select p).FirstOrDefault();

			if(body != null)
			{
				http.RequestBody = body.Value.ToString();
				http.RequestContentType = body.Name;
			}
		}

		private RestResponse ConvertToRestResponse(IRestRequest request, HttpResponse httpResponse)
		{
			var restResponse = new RestResponse();
			restResponse.Content = httpResponse.Content;
			restResponse.ContentEncoding = httpResponse.ContentEncoding;
			restResponse.ContentLength = httpResponse.ContentLength;
			restResponse.ContentType = httpResponse.ContentType;
			restResponse.ErrorException = httpResponse.ErrorException;
			restResponse.ErrorMessage = httpResponse.ErrorMessage;
			restResponse.RawBytes = httpResponse.RawBytes;
			restResponse.ResponseStatus = httpResponse.ResponseStatus;
			restResponse.ResponseUri = httpResponse.ResponseUri;
			restResponse.Server = httpResponse.Server;
			restResponse.StatusCode = httpResponse.StatusCode;
			restResponse.StatusDescription = httpResponse.StatusDescription;
			restResponse.Request = request;
            restResponse.FromCache = httpResponse.FromCache;
            restResponse.CacheExpired = httpResponse.CacheExpired;

            foreach (var header in httpResponse.Headers)
			{
				restResponse.Headers.Add(new Parameter { Name = header.Name, Value = header.Value, Type = ParameterType.HttpHeader });
			}

			foreach (var cookie in httpResponse.Cookies)
			{
				restResponse.Cookies.Add(new RestResponseCookie {
					Comment = cookie.Comment,
					CommentUri = cookie.CommentUri,
					Discard = cookie.Discard,
					Domain = cookie.Domain,
					Expired = cookie.Expired,
					Expires = cookie.Expires,
					HttpOnly = cookie.HttpOnly,
					Name = cookie.Name,
					Path = cookie.Path,
					Port = cookie.Port,
					Secure = cookie.Secure,
					TimeStamp = cookie.TimeStamp,
					Value = cookie.Value,
					Version = cookie.Version
				});
			}

			return restResponse;
		}
	}
}
