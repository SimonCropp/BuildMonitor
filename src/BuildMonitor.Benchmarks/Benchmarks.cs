using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

/// <summary>
/// The entry point. Not named Program, which the Windows head this references has already.
/// </summary>
static class Benchmarks
{
    /// <summary>
    /// In process, so a run needs no project generated and built beside this one. STA for the rows
    /// canvas, a Windows Forms control.
    /// </summary>
    [STAThread]
    static void Main(string[] args) =>
        BenchmarkSwitcher
            .FromAssembly(typeof(Benchmarks).Assembly)
            .Run(args, DefaultConfig.Instance.AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance).AsDefault()));
}
