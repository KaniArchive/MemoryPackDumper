# MemoryPackDumper

A tool to recover MemoryPack class definitions from game assemblies.

*Originally made for **Blue Archive**, should theoretically work with other games that use MemoryPack.*

## Install

You can download the latest pre-build binaries at [Releases](https://github.com/KaniArchive/MemoryPackDumper/releases)

[Windows](https://github.com/KaniArchive/MemoryPackDumper/releases/latest/download/MemoryPackDumper-win-x64.zip) | [Linux](https://github.com/KaniArchive/MemoryPackDumper/releases/latest/download/MemoryPackDumper-linux-x64.zip) | [MacOS](https://github.com/KaniArchive/MemoryPackDumper/releases/latest/download/MemoryPackDumper-osx-arm64.zip)

## Usage

```bash
# Show help
MemoryPackDumper.exe --help

# Generate MemoryPack classes (single file)
MemoryPackDumper.exe --dummy-dll "path/to/dummydll"

# Specify output file
MemoryPackDumper.exe --dummy-dll "path/to/dummydll" --output-file "MemoryPack.cs"

# Split classes into individual files organized by namespace, output-file will now make a folder
MemoryPackDumper.exe --dummy-dll "path/to/dummydll" --split-class --output-file "./output"

# Split with custom root namespace
MemoryPackDumper.exe --dummy-dll "path/to/dummydll" --split-class --namespace "MyGame" --output-file "./output"

# Split with no root namespace (use original namespaces only)
MemoryPackDumper.exe --dummy-dll "path/to/dummydll" --split-class --namespace "" --output-file "./output"

# Specify a dll to limit the search
MemoryPackDumper.exe --dummy-dll "path/to/dummydll" --target-dll "Game.dll" --output-file "MemoryPack.cs"

# Export MemoryPack IDL (.mpk) instead of C#
MemoryPackDumper.exe --dummy-dll "path/to/dummydll" --schema
```

## Build

1. Install [.NET SDK](https://dotnet.microsoft.com/en-us/download)
2. Clone this repository

```sh
git clone https://github.com/KaniArchive/MemoryPackDumper
cd MemoryPackDumper/MemoryPackDumper.CLI
```

3. Build using `dotnet`

```sh
dotnet build
```

## Options

- `-d, --dummy-dll`: Specifies the dummy DLL directory (Required)
- `-o, --output-file`: Specifies the output file or directory when using --split-class (Default: MemoryPack.cs, or MemoryPack.mpk with --schema)
- `-n, --namespace`: Specifies the C# namespace for generated classes (Default: MemoryPackData)
- `-sc, --split-class`: Split classes into individual files organized by namespace folders
- `-s, --schema`: Emit MemoryPack IDL (`.mpk`) instead of C# code
- `-ah, --allow-hidden`: Include private, protected, and internal members in output
- `-nl, --namespace-to-look-for`: Specifies the namespace to look for (filters types)
- `-tl, --type-to-look-for`: Specifies the type to look for (filters types)
- `-t, --target-dll`: Specifies a specific DLL to process
- `-v, --verbose`: Enable verbose debug logging
- `-sw, --suppress-warnings`: Suppress warning messages

> [!IMPORTANT]
> **Disclaimer:** This software is made solely for educational purposes. This project do not claim any responsibility for any usage of this software.

## Acknowledgement

- [ArkanDash/FbsDumper](https://github.com/ArkanDash/FbsDumper)
- [Hiro420/FbsDumperV2](https://github.com/Hiro420/FbsDumperV2)

---

<sub>`FbsDumper` is under **GPL v3**. See [LICENSE](LICENSE) for copyright and license details.</sub>
