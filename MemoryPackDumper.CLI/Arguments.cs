namespace MemoryPackDumper.CLI;

public static class Args
{
    /// <summary>
    ///     MemoryPack Dumper
    /// </summary>
    /// <param name="dummyDll">-d, Specifies the dummy DLL directory.</param>
    /// <param name="outputFile">-o, Specifies the output file or directory (when using --split-class).</param>
    /// <param name="namespace">-n, Specifies the C# namespace for generated classes</param>
    /// <param name="namespaceToLookFor">-nl, Specifies the namespace to look for</param>
    /// <param name="typeToLookFor">-tl, Specifies the type to look for</param>
    /// <param name="targetDll">-t, Specifies a specific DLL to process (if not set, processes all DLLs)</param>
    /// <param name="splitClass">-sc, Split classes into individual files organized by namespace</param>
    /// <param name="schema">-s, Emit MemoryPack IDL (.mpschema) instead of C# code.</param>
    /// <param name="allowHidden">-ah, Include private, protected, and internal members in output.</param>
    /// <param name="noReferencedTypes">-nr, Do not emit referenced non-MemoryPackable types.</param>
    /// <param name="verbose">-v, Enable verbose debug logging.</param>
    /// <param name="suppressWarnings">-sw, Suppress warning messages.</param>
    public static void Run(
        string dummyDll,
        string? outputFile = null,
        string @namespace = "MemoryPackData",
        string? namespaceToLookFor = null,
        string? typeToLookFor = null,
        string? targetDll = null,
        bool splitClass = false,
        bool schema = false,
        bool allowHidden = false,
        bool noReferencedTypes = false,
        bool verbose = false,
        bool suppressWarnings = false) =>
        Parser.Execute(dummyDll, outputFile, @namespace, namespaceToLookFor, typeToLookFor, targetDll, splitClass,
            schema, allowHidden, noReferencedTypes, verbose, suppressWarnings);
}