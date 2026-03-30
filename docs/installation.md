# Installation

## Option 1: Install from NuGet (Recommended)

```bash
dotnet tool install --global Pbt
```

## Option 2: Install from Local Package

```bash
dotnet tool install --global --add-source ./src/Pbt/nupkg Pbt
```

## Option 3: Build from Source

```bash
git clone https://github.com/jonaolden/pbt.git
cd pbt
dotnet build
dotnet run --project src/Pbt -- --help
```

## Requirements

- .NET 10.0 SDK or later
- Windows, macOS, or Linux

## Verify Installation

```bash
pbt --version
pbt --help
```

## Update / Uninstall

```bash
# Update to latest version
dotnet tool update --global Pbt

# Uninstall
dotnet tool uninstall --global Pbt
```

## Publishing to NuGet (Maintainers)

```bash
# 1. Update version in src/Pbt/Pbt.csproj
# 2. Build and pack
cd src/Pbt
dotnet pack --configuration Release

# 3. Push to NuGet.org
dotnet nuget push nupkg/Pbt.X.Y.Z.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```
