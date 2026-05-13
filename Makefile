# Build configuration
CONFIGURATION = Release
PROJECT_FILE = src/InProcess.DevTools/InProcess.DevTools.csproj
NUSPEC_FILE = InProcess.DevTools.nuspec
OUTPUT_DIR = artifacts

.PHONY: all clean build pack help

default: help

all: clean build pack

clean:
	@echo "Cleaning..."
	@dotnet clean $(PROJECT_FILE) -c $(CONFIGURATION)
	@rm -rf $(OUTPUT_DIR)
	@rm -rf src/InProcess.DevTools/bin
	@rm -rf src/InProcess.DevTools/obj

build:
	@echo "Building $(CONFIGURATION)..."
	@dotnet build $(PROJECT_FILE) -c $(CONFIGURATION)

pack: build
	@echo "Packaging NuGet..."
	@mkdir -p $(OUTPUT_DIR)
	@dotnet pack $(PROJECT_FILE) -c $(CONFIGURATION) -o $(OUTPUT_DIR) /p:NuspecFile=../../$(NUSPEC_FILE)

help:
	@echo "Available targets:"
	@echo "  all     - Clean, build and pack"
	@echo "  clean   - Remove build artifacts"
	@echo "  build   - Build the project in Release mode"
	@echo "  pack    - Create the NuGet package using nuspec"
