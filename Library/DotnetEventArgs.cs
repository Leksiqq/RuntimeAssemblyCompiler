namespace Net.Leksi.RuntimeAssemblyCompiler;

public class DotnetEventArgs: EventArgs
{
    public bool Success { get; private set; }
    public string Output { get; private set; }
    public string Error { get; private set; }
    public string Arguments { get; private set; }

    internal DotnetEventArgs(bool success, string output, string error, string arguments)
    {
        Success = success;
        Output = output;
        Error = error;
        Arguments = arguments;
    }
}
