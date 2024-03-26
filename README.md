# ConvA

ConvA is a command-line tool for converting project references to/from DLL references.

## Description

The tool takes a path to a repository, a conversion type, a path to a project to convert, and a version of package reference as arguments. It then converts the project references based on the provided conversion type.

## Usage

You can run the tool with the following command:

```bash
dotnet run -- [path to repo] -t [conversion type] -p [path to project] --patch-version [version]
```

The arguments are:

- `path to repo`: The path to the working repository.
- `-t, --type`: The project conversion type. Possible values are `Proj`, `Proj2`, `Dll`, `Package`, `Props`.
- `-p, --path`: The path to the project to convert.
- `--patch-version`: The version of package reference.

## Conversion Types

- `Proj`: Converts to project references.
- `Proj2`: Converts to project references (alternative method).
- `Dll`: Converts to DLL references.
- `Package`: Converts to package references.
- `Props`: Converts to property references.

## Configuration

The tool uses a configuration file named `convacfg.json` located in the application data folder. The configuration file contains the repository path and the conversion type.

## Building

To build the tool, run:

```bash
dotnet build
```

## Running

To run the tool, use:

```bash
dotnet run -- [arguments]
```

Replace `[arguments]` with the arguments described in the Usage section.