/// <summary>
/// The gate around the one mutable reference.
/// </summary>
public class SessionHostTests
{
    [Test]
    public async Task TheChangeIsWhatComesBack()
    {
        var host = new SessionHost(SessionState.Start(new()));

        var state = host.Mutate(_ => MonitorSession.SetStatus(_, "Refreshing"));

        await Assert.That(state.Status).IsEqualTo("Refreshing");
        await Assert.That(host.State.Status).IsEqualTo("Refreshing");
    }

    /// <summary>
    /// The gate is reentrant, so a nested change does not deadlock. It swaps in its own state and
    /// the outer change then swaps in the one it had already computed, undoing it: a quit swapped
    /// in from inside an action is exactly how the Update button came to do nothing. Loud, because
    /// there is nothing to see when it happens quietly.
    /// </summary>
    [Test]
    public async Task AChangeThatChangesAgainIsRefused()
    {
        var host = new SessionHost(SessionState.Start(new()));

        await Assert.That(() => host.Mutate(outer =>
            {
                host.Mutate(_ => MonitorSession.Quit(_));
                return MonitorSession.SetStatus(outer, "Refreshing");
            }))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// A refused nested change does not leave the gate shut: the next one through works.
    /// </summary>
    [Test]
    public async Task TheGateSurvivesARefusal()
    {
        var host = new SessionHost(SessionState.Start(new()));
        try
        {
            host.Mutate(_ =>
            {
                host.Mutate(inner => inner);
                return _;
            });
        }
        catch (InvalidOperationException)
        {
        }

        await Assert.That(host.Mutate(_ => MonitorSession.SetStatus(_, "Refreshing")).Status).IsEqualTo("Refreshing");
    }
}
