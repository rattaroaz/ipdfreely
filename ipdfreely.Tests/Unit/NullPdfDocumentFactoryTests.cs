using FluentAssertions;
using ipdfreely.Services;
using Xunit;

namespace ipdfreely.Tests.Unit;

public class NullPdfDocumentFactoryTests
{
    [Fact]
    public void Implements_IPdfDocumentFactory()
    {
        new NullPdfDocumentFactory().Should().BeAssignableTo<IPdfDocumentFactory>();
    }

    [Theory]
    [InlineData("any/path.pdf")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/no/such/file.pdf")]
    public async Task OpenFromFilePathAsync_AnyPath_ReturnsNull(string path)
    {
        var factory = new NullPdfDocumentFactory();
        (await factory.OpenFromFilePathAsync(path)).Should().BeNull();
    }

    [Fact]
    public async Task OpenFromFilePathAsync_WithCancellationToken_ReturnsNullWithoutThrowing()
    {
        var factory = new NullPdfDocumentFactory();
        using var cts = new CancellationTokenSource();
        (await factory.OpenFromFilePathAsync("x.pdf", cts.Token)).Should().BeNull();
    }

    [Fact]
    public async Task OpenFromFilePathAsync_PreCancelledToken_StillReturnsNull()
    {
        // Contract: Null factory never throws; it just reports "no PDF support".
        var factory = new NullPdfDocumentFactory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        (await factory.OpenFromFilePathAsync("x.pdf", cts.Token)).Should().BeNull();
    }

    [Fact]
    public async Task OpenFromFilePathAsync_ManyConcurrentCalls_AllReturnNull()
    {
        var factory = new NullPdfDocumentFactory();
        var tasks = Enumerable.Range(0, 32)
            .Select(i => factory.OpenFromFilePathAsync($"file-{i}.pdf"))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r == null);
    }
}
