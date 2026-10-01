# Build configuration
CONFIGURATION = Release
PROJECT_FILE = src/InProcess.DevTools/InProcess.DevTools.csproj
NUSPEC_FILE = InProcess.DevTools.nuspec
SAMPLE_PROJECT = samples/InProcess.DevTools.Sample/InProcess.DevTools.Sample.csproj
OUTPUT_DIR = artifacts
# Must match BaseOutputPath in Directory.Build.props (Unix)
BIN_DIR = /tmp/InProcess.DevTools/bin/InProcess.DevTools/$(CONFIGURATION)/net10.0

.PHONY: all clean build pack help run-sample test

default: help

all: clean build pack

clean:
	@echo "Cleaning..."
	@dotnet clean $(PROJECT_FILE) -c $(CONFIGURATION)
	@dotnet clean $(SAMPLE_PROJECT) -c $(CONFIGURATION)
	@rm -rf $(OUTPUT_DIR)
	@rm -rf src/InProcess.DevTools/bin
	@rm -rf src/InProcess.DevTools/obj
	@rm -rf samples/InProcess.DevTools.Sample/bin
	@rm -rf samples/InProcess.DevTools.Sample/obj

build:
	@echo "Building $(CONFIGURATION)..."
	@dotnet build $(PROJECT_FILE) -c $(CONFIGURATION)

pack: build
	@echo "Packaging NuGet..."
	@mkdir -p $(OUTPUT_DIR)
	@dotnet pack $(PROJECT_FILE) -c $(CONFIGURATION) -o $(OUTPUT_DIR) /p:NuspecFile=../../$(NUSPEC_FILE) /p:NuspecProperties="dllpath=$(BIN_DIR)/Avalonia.Diagnostics.dll"

run-sample:
	@echo "Running sample..."
	@dotnet run --project $(SAMPLE_PROJECT)

test:
	@echo "Running tests..."
	@dotnet run --project tests/InProcess.DevTools.Tests

help:
	@echo "Available targets:"
	@echo "  all          - Clean, build and pack"
	@echo "  clean        - Remove build artifacts"
	@echo "  test         - Run the headless MCP acceptance tests"
	@echo "  build        - Build the project in Release mode"
	@echo "  pack         - Create the NuGet package using nuspec"
	@echo "  run-sample   - Build and run the included sample project"
