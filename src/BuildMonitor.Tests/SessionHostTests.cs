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
                host.Mutate(MonitorSession.Quit);
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

    /// <summary>
    /// The frame loop idles until a change is raised, and applies a frame of input itself every
    /// time it wakes, which is usually nothing. Raised for that too, the loop would wake itself.
    /// </summary>
    [Test]
    public async Task OnlyANewStateIsRaised()
    {
        var host = new SessionHost(SessionState.Start(new()));
        var raised = 0;
        host.Changed += () => raised++;

        host.Mutate(_ => _);
        await Assert.That(raised).IsEqualTo(0);

        host.Mutate(_ => MonitorSession.SetStatus(_, "Refreshing"));
        await Assert.That(raised).IsEqualTo(1);
    }

    /// <summary>
    /// Outside the gate, so a handler may read the state it was raised for, and mutate again.
    /// </summary>
    [Test]
    public async Task AChangeIsRaisedOnceTheGateIsOpen()
    {
        var host = new SessionHost(SessionState.Start(new()));
        host.Changed += () =>
        {
            if (host.State.Status == "Refreshing")
            {
                host.Mutate(_ => MonitorSession.SetStatus(_, "Refreshed"));
            }
        };

        host.Mutate(_ => MonitorSession.SetStatus(_, "Refreshing"));

        await Assert.That(host.State.Status).IsEqualTo("Refreshed");
    }
}
