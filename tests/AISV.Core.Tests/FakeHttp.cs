using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AISV.Core.Tests;

/// <summary>假的 HTTP：按顺序回事先排好的响应，记下收到的每个请求。</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
	public sealed record Seen(HttpMethod Method, string Url, string Body, Dictionary<string, string> Headers);

	private readonly Queue<Func<HttpResponseMessage>> _replies = new Queue<Func<HttpResponseMessage>>();

	public List<Seen> Requests { get; } = new List<Seen>();

	public FakeHttp Reply(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
	{
		_replies.Enqueue(() =>
		{
			HttpResponseMessage r = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
			foreach ((string name, string value) in headers)
			{
				r.Headers.TryAddWithoutValidation(name, value);
			}
			return r;
		});
		return this;
	}

	public FakeHttp Throw(Exception ex)
	{
		_replies.Enqueue(() => throw ex);
		return this;
	}

	public static string Chat(string content, string finish = "stop", int completion = 10) =>
		"{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "},\"finish_reason\":\"" + finish + "\"}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":" + completion + "}}";

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, IEnumerable<string>> h in request.Headers)
		{
			headers[h.Key] = string.Join(",", h.Value);
		}
		string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add(new Seen(request.Method, request.RequestUri!.ToString(), body, headers));
		if (_replies.Count == 0)
		{
			throw new InvalidOperationException("没有排好的响应了");
		}
		return _replies.Dequeue()();
	}
}
