using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using RestSharp;

namespace Neo4jClient.Test
{
    public class RestTestHarness : IEnumerable
    {
        readonly IDictionary<IRestRequest, IHttpResponse> recordedResponses = new Dictionary<IRestRequest, IHttpResponse>();
        readonly IList<IRestRequest> processedRequests = new List<IRestRequest>();
        const string BaseUri = "http://foo/db/data";

        public void Add(IRestRequest request, IHttpResponse response)
        {
            recordedResponses.Add(request, response);
        }

        IHttpFactory HttpFactory
        {
            get { return GenerateHttpFactory(BaseUri); }
        }

        public IGraphClient CreateAndConnectGraphClient()
        {
            var graphClient = new GraphClient(new Uri(BaseUri), HttpFactory);
            graphClient.Connect();
            return graphClient;
        }

        public IEnumerator GetEnumerator()
        {
            throw new NotSupportedException();
        }

        public void AssertAllRequestsWereReceived()
        {
            var resourcesThatWereNeverRequested = recordedResponses
                .Select(r => r.Key)
                .Where(r => !processedRequests.Contains(r))
                .Select(r => r.Resource)
                .ToArray();

            if (!resourcesThatWereNeverRequested.Any())
                return;

            Assert.Fail(
                "The test expected REST requests for the following resources, but they were never made: {0}",
                string.Join(", ", resourcesThatWereNeverRequested));
        }

        public IHttpFactory GenerateHttpFactory(string baseUri)
        {
            var httpFactory = Substitute.For<IHttpFactory>();
            httpFactory
                .Create()
                .Returns(callInfo =>
                {
                    var http = Substitute.For<IHttp>();
                    http.Delete().Returns(ci => HandleRequest(http, Method.DELETE, baseUri));
                    http.Get().Returns(ci => HandleRequest(http, Method.GET, baseUri));
                    http.Post().Returns(ci => HandleRequest(http, Method.POST, baseUri));
                    http.Parameters.ReturnsForAnyArgs(ci => HandleParameters(http, Method.POST, baseUri));
                    http.Put().Returns(ci => HandleRequest(http, Method.PUT, baseUri));
                    return http;
                });
            return httpFactory;
        }

        IList<HttpParameter> HandleParameters(IHttp http, Method method, string baseUri)
        {
            var matchingRequests = recordedResponses
                .Where(can => http.Url.AbsoluteUri == baseUri + can.Key.Resource)
                .Where(can => can.Key.Method == method);

            if (method == Method.POST)
            {
                matchingRequests = matchingRequests
                    .Where(can =>
                    {
                        var request = can.Key;
                        var requestParam = request
                            .Parameters
                            .Where(p => p.Type == ParameterType.GetOrPost)
                            .Select(p => p.Value as string)
                            .SingleOrDefault();
                        return !string.IsNullOrEmpty(requestParam);
                    });
            }

            return matchingRequests
                .Select(can => can
                    .Key
                    .Parameters
                    .Select(param => new HttpParameter { Name = param.Name, Value = param.Value.ToString() })
                    .ToList())
                .SingleOrDefault();
        }

        IHttpResponse HandleRequest(IHttp http, Method method, string baseUri)
        {
            var matchingRequests = recordedResponses
                .Where(can => http.Url.AbsoluteUri == baseUri + can.Key.Resource)
                .Where(can => can.Key.Method == method);

            if (method == Method.POST)
            {
                matchingRequests = matchingRequests
                    .Where(can =>
                    {
                        var request = can.Key;
                        var requestBody = request
                            .Parameters
                            .Where(p => p.Type == ParameterType.RequestBody)
                            .Select(p => p.Value as string)
                            .SingleOrDefault();
                        requestBody = requestBody ?? "";
                        return requestBody == http.RequestBody;
                    });
            }

            var result = matchingRequests.SingleOrDefault();

            processedRequests.Add(result.Key);

            var response = result.Value;
            if (response.ResponseStatus == ResponseStatus.None)
                response.ResponseStatus = ResponseStatus.Completed;
            return response;
        }
    }
}
