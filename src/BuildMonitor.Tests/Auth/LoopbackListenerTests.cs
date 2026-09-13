public class LoopbackListenerTests
{
    [Test]
    public async Task ReturnsTheQueryOfTheCallback()
    {
        using var listener = new LoopbackListener();
        var waiting = listener.WaitForCallback(Cancel.None);
        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{listener.Port}/callback?code=abc%20def&state=s1");
        var body = await response.Content.ReadAsStringAsync();
        var query = await waiting;

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(body).Contains("You can close this window");
        await Assert.That(query["code"]).IsEqualTo("abc def");
        await Assert.That(query["state"]).IsEqualTo("s1");
    }

    [Test]
    public async Task IgnoresRequestsWithoutACode()
    {
        using var listener = new LoopbackListener();
        var waiting = listener.WaitForCallback(Cancel.None);
        using var client = new HttpClient();
        var favicon = await client.GetAsync($"http://127.0.0.1:{listener.Port}/favicon.ico");
        await Assert.That(favicon.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        await client.GetAsync($"http://127.0.0.1:{listener.Port}/callback?error=access_denied&error_description=No");
        var query = await waiting;
        await Assert.That(query["error"]).IsEqualTo("access_denied");
    }

    [Test]
    public async Task ParsesARequestLine()
    {
        var query = LoopbackListener.Parse("GET /callback?code=x&state=y%2Fz&flag HTTP/1.1");
        await Assert.That(query["code"]).IsEqualTo("x");
        await Assert.That(query["state"]).IsEqualTo("y/z");
        await Assert.That(query["flag"]).IsEqualTo("");
    }

    [Test]
    public async Task CancellationStopsWaiting()
    {
        using var listener = new LoopbackListener();
        using var cancel = new CancelSource();
        var waiting = listener.WaitForCallback(cancel.Token);
        await cancel.CancelAsync();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
    }
}
