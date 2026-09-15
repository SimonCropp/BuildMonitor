public class CachingSecretStoreTests
{
    [Test]
    public async Task ReadsTheStoreOnce()
    {
        var inner = new CountingSecretStore();
        inner.Write("token", "value");
        var store = new CachingSecretStore(inner);

        await Assert.That(store.Read("token")).IsEqualTo("value");
        await Assert.That(store.Read("token")).IsEqualTo("value");
        await Assert.That(inner.Reads).IsEqualTo(1);
    }

    [Test]
    public async Task AMissIsNotKept()
    {
        var inner = new CountingSecretStore();
        var store = new CachingSecretStore(inner);

        await Assert.That(store.Read("token")).IsNull();
        inner.Write("token", "value");
        await Assert.That(store.Read("token")).IsEqualTo("value");
    }

    [Test]
    public async Task AWriteReachesTheStoreAndMemory()
    {
        var inner = new CountingSecretStore();
        inner.Write("token", "old");
        var store = new CachingSecretStore(inner);
        store.Read("token");
        store.Write("token", "new");

        await Assert.That(store.Read("token")).IsEqualTo("new");
        await Assert.That(inner.Reads).IsEqualTo(1);
        await Assert.That(inner.Read("token")).IsEqualTo("new");
    }

    [Test]
    public async Task ADeleteReachesTheStoreAndMemory()
    {
        var inner = new CountingSecretStore();
        inner.Write("token", "value");
        var store = new CachingSecretStore(inner);
        store.Read("token");
        store.Delete("token");

        await Assert.That(store.Read("token")).IsNull();
        await Assert.That(inner.Read("token")).IsNull();
    }

    [Test]
    public async Task AReadThatCrossesAWriteIsNotKept()
    {
        // A refresh writes a new token while another thread's first read is still out at the store,
        // which answered with the token being replaced. Kept, that would be served until a restart.
        var inner = new CountingSecretStore();
        inner.Write("token", "old");
        var store = new CachingSecretStore(inner);
        inner.DuringRead = () => store.Write("token", "new");

        await Assert.That(store.Read("token")).IsEqualTo("old");
        await Assert.That(store.Read("token")).IsEqualTo("new");
    }
}
