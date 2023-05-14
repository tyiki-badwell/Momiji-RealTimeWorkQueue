using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.SharedMemory;

[TestClass]
public class MemoryMappedFileUnitTest
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct Struct
    {
        public byte a;
        public byte b;
        public byte c;
        public byte d;
    }

    [TestMethod]
    public void Test1()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);
            builder.AddConsole();
            builder.AddDebug();
        });

        using var test = new IPCBuffer<Struct>("test", 2, loggerFactory);
        {
            var s1 = test.AsSpan(0, 2);

            s1[0].a = 10;
            s1[0].b = 20;
            s1[0].c = 30;
            s1[0].d = 40;

            s1[1].a = 50;
            s1[1].b = 60;
            s1[1].c = 70;
            s1[1].d = 80;

            var s2 = test.AsSpan(0, 1);
            Assert.AreEqual(10, s2[0].a);
            Assert.AreEqual(20, s2[0].b);
            Assert.AreEqual(30, s2[0].c);
            Assert.AreEqual(40, s2[0].d);

            var s3 = test.AsSpan(1, 1);
            Assert.AreEqual(50, s3[0].a);
            Assert.AreEqual(60, s3[0].b);
            Assert.AreEqual(70, s3[0].c);
            Assert.AreEqual(80, s3[0].d);
        }



    }
}
