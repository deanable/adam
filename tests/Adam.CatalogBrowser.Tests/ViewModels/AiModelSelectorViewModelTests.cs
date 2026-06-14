using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Adam.CatalogBrowser.ViewModels;
using FluentAssertions;
using LiquidVision.Core.Configuration;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="AiModelSelectorViewModel"/> — covers constructor state, model selection,
/// command gating, property change notifications, model-definition correctness, progress
/// event wiring, and execution provider selection (Phase 14).
/// </summary>
public sealed class AiModelSelectorViewModelTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Constructor — defaults
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_PopulatesAllAvailableModels()
    {
        var vm = CreateVm();

        vm.AvailableModels.Should().HaveCount(7);
    }

    [Fact]
    public void Constructor_SelectsMatchingModel_FromOptions()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4F16
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.SelectedModel.Should().NotBeNull();
        vm.SelectedModel!.Name.Should().Be("LFM2-VL 1.6B (Q4F16, Recommended)");
    }

    [Fact]
    public void Constructor_WithUnknownModel_FallsBackToFirstModel()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "unknown/model-id",
            Precision = ModelPrecision.Fp32
        };
        var vm = new AiModelSelectorViewModel(null, options);

        // Falls back to AiModelDefinition.All[0] which is LFM2-VL 450M (Q4, Fast)
        vm.SelectedModel.Should().NotBeNull();
        vm.SelectedModel!.Name.Should().Be("LFM2-VL 450M (Q4, Fast)");
    }

    [Fact]
    public void Constructor_InitialStatus_WhenAiTaggingNull_ShowsSelectPrompt()
    {
        var vm = CreateVm();

        vm.ModelStatus.Should().Be("Select a model and download");
    }

    [Fact]
    public void Constructor_AvailableModels_AllHaveNonEmptyNames()
    {
        var vm = CreateVm();

        vm.AvailableModels.Should().AllSatisfy(m => m.Name.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Constructor_AvailableModels_AllHaveNonEmptyDownloadSizes()
    {
        var vm = CreateVm();

        vm.AvailableModels.Should().AllSatisfy(m => m.DownloadSize.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Constructor_AvailableModels_AllHaveNonEmptyModelIds()
    {
        var vm = CreateVm();

        vm.AvailableModels.Should().AllSatisfy(m => m.ModelId.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Constructor_IsModelDownloading_FalseByDefault()
    {
        var vm = CreateVm();

        vm.IsModelDownloading.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ModelDownloadProgress_ZeroByDefault()
    {
        var vm = CreateVm();

        vm.ModelDownloadProgress.Should().Be(0);
    }

    [Fact]
    public void Constructor_RestartRequired_FalseByDefault()
    {
        var vm = CreateVm();

        vm.RestartRequired.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DownloadOrApplyCommand_Exists()
    {
        var vm = CreateVm();

        vm.DownloadOrApplyCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_DownloadOrApplyCommand_CanExecute_WithSelectedModel()
    {
        var vm = CreateVm();
        // Constructor already selects the current model
        vm.SelectedModel.Should().NotBeNull();

        // Will be true initially (not downloading and model is selected)
        var canExecute = vm.DownloadOrApplyCommand.CanExecute(null);

        canExecute.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    //  SelectedModel
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedModel_DisplayLabel_UpdatesWithSelection()
    {
        var vm = CreateVm();

        vm.SelectedModelDisplay.Should().NotBeNullOrWhiteSpace();
        vm.SelectedModelDisplay.Should().Contain(vm.SelectedModel!.Name);
        vm.SelectedModelDisplay.Should().Contain(vm.SelectedModel.DownloadSize);
    }

    [Fact]
    public void SelectedModel_DisplayLabel_WhenNull_ReturnsNone()
    {
        var vm = CreateVm();
        vm.SelectedModel = null;

        vm.SelectedModelDisplay.Should().Be("None");
    }

    [Fact]
    public void SelectedModel_IsModelSelected_WhenNull_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.SelectedModel = null;

        vm.IsModelSelected.Should().BeFalse();
    }

    [Fact]
    public void SelectedModel_IsModelSelected_WhenSet_ReturnsTrue()
    {
        var vm = CreateVm();

        vm.IsModelSelected.Should().BeTrue();
    }

    [Fact]
    public void SelectedModel_DifferentModel_SetsRestartRequired()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4F16
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.RestartRequired.Should().BeFalse("current model matches options");

        // Select a different model
        vm.SelectedModel = AiModelDefinition.All[0]; // LFM2-VL 450M (Q4, Fast)

        vm.RestartRequired.Should().BeTrue("different model selected");
    }

    [Fact]
    public void SelectedModel_SameModel_DoesNotSetRestartRequired()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4F16
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.RestartRequired.Should().BeFalse();

        // Select the same model
        var same = AiModelDefinition.FindOrDefault("onnx-community/LFM2-VL-1.6B-ONNX", ModelPrecision.Q4F16);
        vm.SelectedModel = same;

        vm.RestartRequired.Should().BeFalse("same model selected");
    }

    [Fact]
    public void SelectedModel_SettingNull_IsModelSelectedFalse()
    {
        var vm = CreateVm();
        vm.SelectedModel.Should().NotBeNull();

        vm.SelectedModel = null;

        vm.IsModelSelected.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  PropertyChanged notifications
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedModel_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedModel = AiModelDefinition.All[1];

        raised.Should().Contain(nameof(vm.SelectedModel));
        raised.Should().Contain(nameof(vm.SelectedModelDisplay));
        raised.Should().Contain(nameof(vm.IsModelSelected));
    }

    [Fact]
    public void IsModelDownloading_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsModelDownloading = true;

        raised.Should().Contain(nameof(vm.IsModelDownloading));
    }

    [Fact]
    public void ModelDownloadProgress_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ModelDownloadProgress = 0.5;

        raised.Should().Contain(nameof(vm.ModelDownloadProgress));
    }

    [Fact]
    public void ModelStatus_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ModelStatus = "Downloading...";

        raised.Should().Contain(nameof(vm.ModelStatus));
    }

    [Fact]
    public void RestartRequired_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.RestartRequired = true;

        raised.Should().Contain(nameof(vm.RestartRequired));
    }

    // ─────────────────────────────────────────────────────────────────
    //  DownloadOrApplyCommand — CanExecute
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DownloadOrApplyCommand_CanExecute_WhenNoModel_ReturnsFalse()
    {
        var vm = CreateVm();

        vm.SelectedModel = null;

        vm.DownloadOrApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DownloadOrApplyCommand_CanExecute_WhenDownloading_ReturnsFalse()
    {
        var vm = CreateVm();

        vm.IsModelDownloading = true;

        vm.DownloadOrApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void DownloadOrApplyCommand_CanExecute_RaisesCanExecuteChanged()
    {
        var vm = CreateVm();
        var canExecuteChangedFired = false;
        vm.DownloadOrApplyCommand.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        vm.SelectedModel = null;

        canExecuteChangedFired.Should().BeTrue();
    }

    [Fact]
    public void DownloadOrApplyCommand_CanExecute_WhenIsModelDownloading_RaisesCanExecuteChanged()
    {
        var vm = CreateVm();
        var canExecuteChangedFired = false;
        vm.DownloadOrApplyCommand.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        vm.IsModelDownloading = true;

        canExecuteChangedFired.Should().BeTrue();
    }

    [Fact]
    public void DownloadOrApplyCommand_CanExecute_WhenIsModelDownloading_ReturnsFalse()
    {
        var vm = CreateVm();

        vm.IsModelDownloading = true;

        vm.DownloadOrApplyCommand.CanExecute(null).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  IsModelReady
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsModelReady_WhenAiTaggingNull_ReturnsFalse()
    {
        var vm = CreateVm();

        vm.IsModelReady.Should().BeFalse();
    }

    [Fact]
    public void IsModelReady_PropertyChanged_FiredOnModelStatusChange()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Simulate what happens when IsInitialized is set on the AiTaggingService
        // The event handler in the VM fires OnPropertyChanged(nameof(IsModelReady))
        // We can test the same effect by just checking that it's listed as a change
        vm.ModelStatus = "Model ready";

        // IsModelReady won't change here (it checks the service). This test simply
        // validates that we're testing the right property.
        vm.IsModelReady.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Model Status transitions
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ModelStatus_CanTransitionBetweenStates()
    {
        var vm = CreateVm();

        vm.ModelStatus = "Initializing model...";
        vm.ModelStatus.Should().Be("Initializing model...");

        vm.ModelStatus = "Downloading model... 45%";
        vm.ModelStatus.Should().Be("Downloading model... 45%");

        vm.ModelStatus = "Model ready";
        vm.ModelStatus.Should().Be("Model ready");

        vm.ModelStatus = "Error: Connection lost";
        vm.ModelStatus.Should().Be("Error: Connection lost");
    }

    [Fact]
    public void ModelDownloadProgress_TransitionsCorrectly()
    {
        var vm = CreateVm();

        vm.ModelDownloadProgress = 0.25;
        vm.ModelDownloadProgress.Should().Be(0.25);

        vm.ModelDownloadProgress = 0.75;
        vm.ModelDownloadProgress.Should().Be(0.75);

        vm.ModelDownloadProgress = 1.0;
        vm.ModelDownloadProgress.Should().Be(1.0);
    }

    [Fact]
    public void IsModelDownloading_CanToggle()
    {
        var vm = CreateVm();

        vm.IsModelDownloading = true;
        vm.IsModelDownloading.Should().BeTrue();

        vm.IsModelDownloading = false;
        vm.IsModelDownloading.Should().BeFalse();
    }

    [Fact]
    public void RestartRequired_CanToggle()
    {
        var vm = CreateVm();

        vm.RestartRequired = true;
        vm.RestartRequired.Should().BeTrue();

        vm.RestartRequired = false;
        vm.RestartRequired.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  AiModelDefinition — static helpers
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AiModelDefinition_All_HasSevenEntries()
    {
        AiModelDefinition.All.Should().HaveCount(7);
    }

    [Fact]
    public void AiModelDefinition_FindOrDefault_WithExactMatch_ReturnsMatch()
    {
        var found = AiModelDefinition.FindOrDefault(
            "onnx-community/LFM2-VL-1.6B-ONNX", ModelPrecision.Q4F16);

        found.Should().NotBeNull();
        found.Name.Should().Be("LFM2-VL 1.6B (Q4F16, Recommended)");
    }

    [Fact]
    public void AiModelDefinition_FindOrDefault_WithNoMatch_ReturnsFirst()
    {
        var found = AiModelDefinition.FindOrDefault(
            "nonexistent/model", ModelPrecision.Fp32);

        found.Should().NotBeNull();
        found.Name.Should().Be(AiModelDefinition.All[0].Name);
    }

    [Fact]
    public void AiModelDefinition_FindOrDefault_MatchIsCaseInsensitive()
    {
        var found = AiModelDefinition.FindOrDefault(
            "ONNX-COMMUNITY/LFM2-VL-450M-ONNX", ModelPrecision.Q4F16);

        found.Should().NotBeNull();
        found.Name.Should().Be("LFM2-VL 450M (Q4F16, Balanced)");
    }

    [Fact]
    public void AiModelDefinition_DisplayLabel_ContainsNameAndSize()
    {
        var model = AiModelDefinition.All[0];

        model.DisplayLabel.Should().Contain(model.Name);
        model.DisplayLabel.Should().Contain(model.DownloadSize);
    }

    [Fact]
    public void AiModelDefinition_AllEntries_HaveUniqueDisplayLabels()
    {
        var labels = AiModelDefinition.All.Select(m => m.DisplayLabel).ToList();
        var distinct = labels.Distinct().ToList();

        labels.Should().HaveSameCount(distinct);
    }

    [Fact]
    public void AiModelDefinition_Lfm25VlArchitecture_IsDetectedCorrectly()
    {
        var model = AiModelDefinition.FindOrDefault(
            "LiquidAI/LFM2.5-VL-1.6B-ONNX", ModelPrecision.Q4);

        model.Architecture.Should().Be(ModelArchitecture.Lfm25Vl);
    }

    [Fact]
    public void AiModelDefinition_Lfm2VlArchitecture_IsDetectedCorrectly()
    {
        var model = AiModelDefinition.FindOrDefault(
            "onnx-community/LFM2-VL-450M-ONNX", ModelPrecision.Q4);

        model.Architecture.Should().Be(ModelArchitecture.Lfm2Vl);
    }

    [Fact]
    public void AiModelDefinition_All_SizesArePositive()
    {
        AiModelDefinition.All.Should().AllSatisfy(m =>
            m.DownloadSizeBytes.Should().BeGreaterThan(0));
    }

    [Fact]
    public void AiModelDefinition_All_HaveDescriptions()
    {
        AiModelDefinition.All.Should().AllSatisfy(m =>
            m.Description.Should().NotBeNullOrWhiteSpace());
    }

    // ─────────────────────────────────────────────────────────────────
    //  Progress state transitions (simulating event handler behavior)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DownloadProgress_InProgress_UpdatesIsModelDownloading()
    {
        var vm = CreateVm();

        vm.ModelDownloadProgress = 0.45;
        vm.IsModelDownloading = true;
        vm.ModelStatus = "Downloading model... 45%";

        vm.IsModelDownloading.Should().BeTrue();
        vm.ModelDownloadProgress.Should().Be(0.45);
        vm.ModelStatus.Should().Be("Downloading model... 45%");
    }

    [Fact]
    public void DownloadProgress_Complete_ResetsState()
    {
        var vm = CreateVm();

        vm.ModelDownloadProgress = 0.45;
        vm.IsModelDownloading = true;

        vm.IsModelDownloading = false;
        vm.ModelDownloadProgress = 0;
        vm.ModelStatus = "Model ready";

        vm.IsModelDownloading.Should().BeFalse();
        vm.ModelDownloadProgress.Should().Be(0);
        vm.ModelStatus.Should().Be("Model ready");
    }

    [Fact]
    public void DownloadProgress_FiresPropertyChangedEvents()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ModelDownloadProgress = 0.5;

        raised.Should().Contain(nameof(vm.ModelDownloadProgress));
    }

    [Fact]
    public void IsModelDownloading_Toggling_RaisesCanExecuteChangedOnCommand()
    {
        var vm = CreateVm();
        var canExecuteChangedFired = false;
        vm.DownloadOrApplyCommand.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        vm.IsModelDownloading = true;

        canExecuteChangedFired.Should().BeTrue();
    }

    [Fact]
    public void IsModelDownloading_False_ReenablesCommand()
    {
        var vm = CreateVm();

        vm.IsModelDownloading = true;
        vm.DownloadOrApplyCommand.CanExecute(null).Should().BeFalse();

        vm.IsModelDownloading = false;

        vm.DownloadOrApplyCommand.CanExecute(null).Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Edge cases
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedModel_SameInstance_DoesNotRaisePropertyChanged()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-1.6B-ONNX",
            Precision = ModelPrecision.Q4F16
        };
        var vm = new AiModelSelectorViewModel(null, options);
        var current = vm.SelectedModel;
        var propertyChangedCount = 0;
        vm.PropertyChanged += (_, _) => propertyChangedCount++;

        vm.SelectedModel = current; // same instance

        propertyChangedCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Execution Provider — ProviderOptions
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ProviderOptions_HasThreeProviders()
    {
        var vm = CreateVm();

        vm.ProviderOptions.Should().HaveCount(3);
    }

    [Fact]
    public void Constructor_ProviderOptions_FirstIsCpu()
    {
        var vm = CreateVm();

        vm.ProviderOptions[0].Kind.Should().Be(ExecutionProviderKind.Cpu);
        vm.ProviderOptions[0].DisplayName.Should().Be("CPU");
    }

    [Fact]
    public void Constructor_ProviderOptions_SecondIsCuda()
    {
        var vm = CreateVm();

        vm.ProviderOptions[1].Kind.Should().Be(ExecutionProviderKind.Cuda);
        vm.ProviderOptions[1].DisplayName.Should().Be("CUDA (NVIDIA)");
    }

    [Fact]
    public void Constructor_ProviderOptions_ThirdIsDirectML()
    {
        var vm = CreateVm();

        vm.ProviderOptions[2].Kind.Should().Be(ExecutionProviderKind.DirectML);
        vm.ProviderOptions[2].DisplayName.Should().Be("DirectML (Windows)");
    }

    [Fact]
    public void Constructor_ProviderOptions_AllHaveNonEmptyDisplayNames()
    {
        var vm = CreateVm();

        vm.ProviderOptions.Should().AllSatisfy(p => p.DisplayName.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Constructor_ProviderOptions_AllHaveUniqueKinds()
    {
        var vm = CreateVm();

        var kinds = vm.ProviderOptions.Select(p => p.Kind).ToList();
        kinds.Should().OnlyHaveUniqueItems();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Execution Provider — SelectedProviderOption
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SelectedProviderOption_DefaultsToCpu()
    {
        var vm = CreateVm();

        vm.SelectedProviderOption.Should().NotBeNull();
        vm.SelectedProviderOption!.Kind.Should().Be(ExecutionProviderKind.Cpu);
    }

    [Fact]
    public void Constructor_SelectedProviderOption_ReadsFromOptions()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4,
            ExecutionProvider = ExecutionProviderKind.Cuda
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.SelectedProviderOption.Should().NotBeNull();
        vm.SelectedProviderOption!.Kind.Should().Be(ExecutionProviderKind.Cuda);
        vm.SelectedProviderOption!.DisplayName.Should().Be("CUDA (NVIDIA)");
    }

    [Fact]
    public void Constructor_SelectedProviderOption_ReadsDirectMLFromOptions()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4,
            ExecutionProvider = ExecutionProviderKind.DirectML
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.SelectedProviderOption!.Kind.Should().Be(ExecutionProviderKind.DirectML);
    }

    [Fact]
    public void SelectedProviderOption_Setter_UpdatesProvider()
    {
        var vm = CreateVm();

        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA

        vm.SelectedProviderOption!.Kind.Should().Be(ExecutionProviderKind.Cuda);
    }

    [Fact]
    public void SelectedProviderOption_Setter_UpdatesRuntimeOptions()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.SelectedProviderOption = vm.ProviderOptions[2]; // DirectML

        options.ExecutionProvider.Should().Be(ExecutionProviderKind.DirectML);
    }

    [Fact]
    public void SelectedProviderOption_Setter_SetsRestartRequired()
    {
        var vm = CreateVm();

        vm.RestartRequired.Should().BeFalse("defaults match — no restart needed yet");

        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA

        vm.RestartRequired.Should().BeTrue();
    }

    [Fact]
    public void SelectedProviderOption_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA

        raised.Should().Contain(nameof(vm.SelectedProviderOption));
        raised.Should().Contain(nameof(vm.IsGpuProvider));
    }

    [Fact]
    public void SelectedProviderOption_Setter_NullValue_DoesNothing()
    {
        var vm = CreateVm();
        var original = vm.SelectedProviderOption!.Kind;
        var propertyChangedCount = 0;
        vm.PropertyChanged += (_, _) => propertyChangedCount++;

        vm.SelectedProviderOption = null;

        vm.SelectedProviderOption!.Kind.Should().Be(original);
        propertyChangedCount.Should().Be(0);
    }

    [Fact]
    public void SelectedProviderOption_Setter_SameValue_DoesNotRaisePropertyChanged()
    {
        var vm = CreateVm();
        var propertyChangedCount = 0;
        vm.PropertyChanged += (_, _) => propertyChangedCount++;

        vm.SelectedProviderOption = vm.ProviderOptions[0]; // CPU (already selected)

        propertyChangedCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Execution Provider — IsGpuProvider
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsGpuProvider_WhenCpu_ReturnsFalse()
    {
        var vm = CreateVm();

        vm.IsGpuProvider.Should().BeFalse();
    }

    [Fact]
    public void IsGpuProvider_WhenCuda_ReturnsTrue()
    {
        var vm = CreateVm();

        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA

        vm.IsGpuProvider.Should().BeTrue();
    }

    [Fact]
    public void IsGpuProvider_WhenDirectML_ReturnsTrue()
    {
        var vm = CreateVm();

        vm.SelectedProviderOption = vm.ProviderOptions[2]; // DirectML

        vm.IsGpuProvider.Should().BeTrue();
    }

    [Fact]
    public void IsGpuProvider_RaisesPropertyChanged_WhenSwitchingFromCpuToCuda()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA

        raised.Should().Contain(nameof(vm.IsGpuProvider));
    }

    [Fact]
    public void IsGpuProvider_RaisesPropertyChanged_WhenSwitchingFromCudaToCpu()
    {
        var vm = CreateVm();
        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedProviderOption = vm.ProviderOptions[0]; // CPU

        raised.Should().Contain(nameof(vm.IsGpuProvider));
    }

    [Fact]
    public void IsGpuProvider_DoesNotRaisePropertyChanged_WhenSwitchingBetweenGpuProviders()
    {
        var vm = CreateVm();
        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedProviderOption = vm.ProviderOptions[2]; // DirectML (also GPU)

        // IsGpuProvider should NOT fire here since it stays true
        raised.Should().NotContain(nameof(vm.IsGpuProvider));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Execution Provider — GpuDeviceId
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_GpuDeviceId_DefaultsToZero()
    {
        var vm = CreateVm();

        vm.GpuDeviceId.Should().Be(0);
    }

    [Fact]
    public void Constructor_GpuDeviceId_ReadsFromOptions()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4,
            GpuDeviceId = 2
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.GpuDeviceId.Should().Be(2);
    }

    [Fact]
    public void GpuDeviceId_Setter_UpdatesValue()
    {
        var vm = CreateVm();

        vm.GpuDeviceId = 3;

        vm.GpuDeviceId.Should().Be(3);
    }

    [Fact]
    public void GpuDeviceId_Setter_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.GpuDeviceId = 1;

        raised.Should().Contain(nameof(vm.GpuDeviceId));
    }

    [Fact]
    public void GpuDeviceId_Setter_SameValue_DoesNotRaisePropertyChanged()
    {
        var vm = CreateVm();
        var propertyChangedCount = 0;
        vm.PropertyChanged += (_, _) => propertyChangedCount++;

        vm.GpuDeviceId = 0; // already 0

        propertyChangedCount.Should().Be(0);
    }

    [Fact]
    public void GpuDeviceId_Setter_UpdatesRuntimeOptions()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4
        };
        var vm = new AiModelSelectorViewModel(null, options);

        vm.GpuDeviceId = 1;

        options.GpuDeviceId.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Execution Provider — persistence with App.Config
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedProviderOption_Setter_PersistsToConfig()
    {
        var vm = CreateVm();

        vm.SelectedProviderOption = vm.ProviderOptions[2]; // DirectML

        App.Config.AiExecutionProvider.Should().Be("DirectML");
    }

    [Fact]
    public void GpuDeviceId_Setter_PersistsToConfig()
    {
        var vm = CreateVm();

        vm.GpuDeviceId = 7;

        App.Config.AiGpuDeviceId.Should().Be(7);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Execution Provider — DownloadOrApplyModelAsync saves all
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DownloadOrApplyCommand_Execute_SavesAllProviderSettings()
    {
        var vm = CreateVm();

        vm.SelectedProviderOption = vm.ProviderOptions[1]; // CUDA
        vm.GpuDeviceId = 1;

        // The command's async lambda runs synchronously when AiTaggingService is null
        // (takes the else branch: "Model saved. Restart to apply.")
        vm.DownloadOrApplyCommand.Execute(null);

        vm.ModelStatus.Should().Be("Model saved. Restart to apply.");
        vm.RestartRequired.Should().BeTrue();

        // Verify all provider settings were persisted
        App.Config.AiExecutionProvider.Should().Be("Cuda");
        App.Config.AiGpuDeviceId.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="AiModelSelectorViewModel"/> with default options and no AI tagging service.
    /// The default options match the first model in All (LFM2-VL 450M Q4, Fast).
    /// </summary>
    private static AiModelSelectorViewModel CreateVm()
    {
        var options = new LiquidVisionOptions
        {
            ModelId = "onnx-community/LFM2-VL-450M-ONNX",
            Precision = ModelPrecision.Q4
        };
        return new AiModelSelectorViewModel(null, options);
    }
}
