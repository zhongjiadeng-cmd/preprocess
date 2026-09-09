.PHONY: help run build test test-python test-dotnet package qa

PYTHON ?= python3
DOTNET ?= dotnet

help:
	@echo "make run          启动桌面应用"
	@echo "make build        构建应用、测试和 QA 工具"
	@echo "make test         运行 Python 和 C# 测试"
	@echo "make package      构建并验证 macOS 应用包"
	@echo "make qa           运行工作流界面验证工具"

run:
	$(DOTNET) run --project src/GrayscaleLayersMac/GrayscaleLayersMac.csproj

build:
	$(DOTNET) build Preprocess.slnx

test: test-python test-dotnet

test-python:
	$(PYTHON) -m pytest -q

test-dotnet:
	$(DOTNET) test tests/GrayscaleLayersMac.Tests/GrayscaleLayersMac.Tests.csproj

package:
	bash scripts/test-macos-app-bundle.sh

qa:
	$(DOTNET) run --project tools/ProcessingWorkflowQa/ProcessingWorkflowQa.csproj
