namespace MemoryPackDumper.CLI;

public static class Args
{
    /// <summary>
    ///     MemoryPack Dumper
    /// </summary>
    /// <param name="dummyDll">-d, Specifies the dummy DLL directory.</param>
    /// <param name="outputFile">-o, Specifies the output file.</param>
    /// <param name="namespace">-n, Specifies the C# namespace for generated classes</param>
    /// <param name="namespaceToLookFor">-nl, Specifies the namespace to look for</param>
    /// <param name="targetDll">-t, Specifies a specific DLL to process (if not set, processes all DLLs)</param>
    /// <param name="verbose">-v, Enable verbose debug logging.</param>
    /// <param name="suppressWarnings">-sw, Suppress warning messages.</param>
    public static void Run(
        string dummyDll,
        string outputFile = "MemoryPack.cs",
        string @namespace = "MemoryPackData",
        string? namespaceToLookFor = null,
        string? targetDll = null,
        bool verbose = false,
        bool suppressWarnings = false)
    {
        Parser.Execute(dummyDll, outputFile, @namespace, namespaceToLookFor, targetDll, verbose, suppressWarnings);
    }
}