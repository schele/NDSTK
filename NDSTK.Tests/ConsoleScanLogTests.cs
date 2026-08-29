using NDSTK.CookieScanner;

namespace NDSTK.Tests;

public class ConsoleScanLogTests
{
    // The console tool's contract with a pipeline: progress on stdout, problems on stderr, so a
    // caller can redirect one without losing the other. That split is the only reason this class
    // exists rather than the engine calling Console directly.
    [Fact]
    public void Info_goes_to_standard_output_and_warning_to_standard_error()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var log = new ConsoleScanLog();
            log.Info("progress");
            log.Warning("something went wrong");
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        Assert.Contains("progress", output.ToString());
        Assert.DoesNotContain("something went wrong", output.ToString());
        Assert.Contains("something went wrong", error.ToString());
        Assert.DoesNotContain("progress", error.ToString());
    }

    // The engine passes fully-formed sentences; the log must not decorate them with levels or
    // timestamps. The baseline comparison in the final task depends on the console output being
    // byte-identical to what the pre-refactor build produced.
    [Fact]
    public void Messages_are_written_verbatim_with_no_prefix()
    {
        var output = new StringWriter();
        TextWriter previousOut = Console.Out;

        try
        {
            Console.SetOut(output);
            new ConsoleScanLog().Info("  pass 1/6: Undecided");
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        Assert.Equal("  pass 1/6: Undecided", output.ToString().TrimEnd('\r', '\n'));
    }
}
