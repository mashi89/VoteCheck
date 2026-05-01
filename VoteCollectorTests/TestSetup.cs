using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaSHi;

namespace VoteCollectorTests;

[TestClass]
public static class TestSetup
{
    [AssemblyInitialize]
    public static void DisableLogFileSink( TestContext _ )
    {
        Logger.FileSinkEnabled = false;
    }
}
