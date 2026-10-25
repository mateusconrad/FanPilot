// Runs once, before any test. The test classes run in parallel, and some keep pool threads busy while they wait (a
// Parallel.For queued on FanManager's lock, HttpListener handlers); the .NET Framework pool starts with one thread
// per core and adds about one every half second. Then SseHub's pumps start seconds late and a frame "arrives late"
// only in the tests (SseHubTests failed so once in the CI and once here).
using System.Threading;

static class TestModule
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        ThreadPool.SetMinThreads(64, 64);
    }
}

namespace System.Runtime.CompilerServices
{
    // The C# compiler only needs the attribute to exist: .NET Framework 4.7.2 does not have it.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    sealed class ModuleInitializerAttribute : Attribute { }
}
