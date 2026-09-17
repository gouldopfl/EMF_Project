using System.Net;
using System.Text;
using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class EcfrVeteransReviewerRegulatoryTextProviderTests
{
    [Fact]
    public async Task GetCurrentAsync_FetchesSectionOnceAndSelectsApplicableSubsections()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider =
            new EcfrVeteransReviewerRegulatoryTextProvider(client);

        var result =
            await provider.GetCurrentAsync(
            [
                "38 C.F.R. § 3.310(a)",
                "38 C.F.R. § 3.310(b)"
            ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(
            "(a) General. Causation text.",
            result[0].Text);
        Assert.Contains(
            "(b) Aggravation. Aggravation text.",
            result[1].Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "(1) Nested baseline detail.",
            result[1].Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "(c) Unrelated text.",
            result[1].Text,
            StringComparison.Ordinal);
        Assert.All(
            result,
            item => Assert.Equal(
                new DateOnly(2026, 9, 15),
                item.UpToDateAsOf));
        Assert.All(
            result,
            item => Assert.Equal(64, item.SourceSha256.Length));

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains(
            handler.RequestUris,
            uri => uri.AbsolutePath.EndsWith(
                "/api/versioner/v1/titles.json",
                StringComparison.Ordinal));
        Assert.Contains(
            handler.RequestUris,
            uri =>
                uri.AbsolutePath.EndsWith(
                    "/api/versioner/v1/full/2026-09-15/title-38.xml",
                    StringComparison.Ordinal) &&
                uri.Query.Contains("part=3", StringComparison.Ordinal) &&
                uri.Query.Contains("section=3.310", StringComparison.Ordinal));
    }


    [Fact]
    public async Task GetCurrentAsync_AcceptsCompactCfrCitation()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider =
            new EcfrVeteransReviewerRegulatoryTextProvider(client);

        var result =
            await provider.GetCurrentAsync(
            [
                "38 CFR 3.310(a)"
            ]);

        var regulation = Assert.Single(result);
        Assert.Equal("38 CFR 3.310(a)", regulation.Citation);
        Assert.Equal("(a) General. Causation text.", regulation.Text);
    }

    [Fact]
    public async Task GetCurrentAsync_RejectsUnsupportedNestedCitation()
    {
        var provider =
            new EcfrVeteransReviewerRegulatoryTextProvider(
                new HttpClient(new RecordingHandler()));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetCurrentAsync(
                [
                    "38 C.F.R. § 3.310(b)(1)"
                ]));

        Assert.Contains(
            "Unsupported reviewer regulatory citation",
            exception.Message,
            StringComparison.Ordinal);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri =
                request.RequestUri ??
                throw new InvalidOperationException("Request URI is missing.");

            RequestUris.Add(uri);

            if (uri.AbsolutePath.EndsWith(
                    "/api/versioner/v1/titles.json",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    JsonResponse(
                        """
                        {
                          "titles": [
                            {
                              "number": 38,
                              "name": "Pensions, Bonuses, and Veterans' Relief",
                              "up_to_date_as_of": "2026-09-15",
                              "reserved": false
                            }
                          ],
                          "meta": {
                            "date": "2026-09-15",
                            "import_in_progress": false
                          }
                        }
                        """));
            }

            if (uri.AbsolutePath.EndsWith(
                    "/api/versioner/v1/full/2026-09-15/title-38.xml",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    XmlResponse(
                        """
                        <ROOT>
                          <DIV8 N="§ 3.310" TYPE="SECTION">
                            <HEAD>§ 3.310 Test section.</HEAD>
                            <P>(a) General. Causation text.</P>
                            <P>(b) Aggravation. Aggravation text.</P>
                            <P>(1) Nested baseline detail.</P>
                            <P>(c) Unrelated text.</P>
                          </DIV8>
                        </ROOT>
                        """));
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string value) =>
            new(HttpStatusCode.OK)
            {
                Content =
                    new StringContent(
                        value,
                        Encoding.UTF8,
                        "application/json")
            };

        private static HttpResponseMessage XmlResponse(string value) =>
            new(HttpStatusCode.OK)
            {
                Content =
                    new StringContent(
                        value,
                        Encoding.UTF8,
                        "application/xml")
            };
    }
}
