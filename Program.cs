using System;
using System.Threading.Tasks;
using ipdfreely.Services;
using ipdfreely.Tests;

namespace TestConsole;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("    ipdfreely Test Console");
        Console.WriteLine("========================================");
        Console.WriteLine();

        try
        {
            // Run basic tests
            await SimpleTest.RunBasicTests();
            Console.WriteLine();
            
            // Run performance test
            SimpleTest.RunQuickPerformanceTest();
            Console.WriteLine();
            
            Console.WriteLine("🎉 All tests completed successfully! 🎉");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}
