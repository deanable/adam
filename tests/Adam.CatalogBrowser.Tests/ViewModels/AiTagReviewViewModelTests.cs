using System.ComponentModel;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="AiTagReviewViewModel"/> — covers initial state, confidence threshold,
/// keyword/category toggling, count recalculation, filter commands, and property change
/// notification wiring (Phase 14, T14.6).
/// </summary>
public sealed class AiTagReviewViewModelTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a simple <see cref="AiTagResult"/> with mixed-confidence keywords and categories.
    /// </summary>
    private static AiTagResult CreateSampleResult()
    {
        return new AiTagResult
        {
            Description = "A scenic mountain landscape",
            Keywords =
            [
                new KeywordScore { Name = "mountain", Confidence = 0.95 },
                new KeywordScore { Name = "landscape", Confidence = 0.82 },
                new KeywordScore { Name = "sunset", Confidence = 0.65 },
                new KeywordScore { Name = "clouds", Confidence = 0.41 },
                new KeywordScore { Name = "reflection", Confidence = 0.22 },
            ],
            Categories =
            [
                new CategoryScore { Name = "Nature", Confidence = 0.91 },
                new CategoryScore { Name = "Travel", Confidence = 0.55 },
            ],
            ProcessingTimeMs = 1420,
            ModelVersion = "LFM2-VL-v1"
        };
    }

    /// <summary>
    /// Creates an empty <see cref="AiTagResult"/> with no keywords or categories.
    /// </summary>
    private static AiTagResult CreateEmptyResult()
    {
        return new AiTagResult
        {
            Description = null,
            Keywords = [],
            Categories = [],
            ProcessingTimeMs = 0,
            ModelVersion = string.Empty
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  Constructor & default state
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultConfidenceThreshold_Is60Percent()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold.Should().Be(0.6);
        vm.ConfidenceThresholdPercent.Should().Be("60%");
    }

    [Fact]
    public void Constructor_PopulatesKeywordScores_SortedByConfidenceDescending()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.KeywordScores.Should().HaveCount(5);
        vm.KeywordScores[0].Name.Should().Be("mountain");   // 0.95
        vm.KeywordScores[1].Name.Should().Be("landscape");  // 0.82
        vm.KeywordScores[2].Name.Should().Be("sunset");     // 0.65
        vm.KeywordScores[3].Name.Should().Be("clouds");     // 0.41
        vm.KeywordScores[4].Name.Should().Be("reflection"); // 0.22
    }

    [Fact]
    public void Constructor_PopulatesCategoryScores_SortedByConfidenceDescending()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.CategoryScores.Should().HaveCount(2);
        vm.CategoryScores[0].Name.Should().Be("Nature"); // 0.91
        vm.CategoryScores[1].Name.Should().Be("Travel"); // 0.55
    }

    [Fact]
    public void Constructor_AcceptsItemsAboveThresholdByDefault()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // Threshold is 0.6: mountain (0.95), landscape (0.82), sunset (0.65) accepted
        vm.KeywordScores[0].IsAccepted.Should().BeTrue();  // mountain 0.95
        vm.KeywordScores[1].IsAccepted.Should().BeTrue();  // landscape 0.82
        vm.KeywordScores[2].IsAccepted.Should().BeTrue();  // sunset 0.65
        vm.KeywordScores[3].IsAccepted.Should().BeFalse(); // clouds 0.41
        vm.KeywordScores[4].IsAccepted.Should().BeFalse(); // reflection 0.22

        // Nature (0.91) accepted, Travel (0.55) rejected
        vm.CategoryScores[0].IsAccepted.Should().BeTrue();  // Nature 0.91
        vm.CategoryScores[1].IsAccepted.Should().BeFalse(); // Travel 0.55
    }

    [Fact]
    public void Constructor_CalculatesInitialCounts()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // 3 keywords + 1 category accepted out of 5 + 2 = 7 total
        vm.AcceptedCount.Should().Be(4);
        vm.TotalCount.Should().Be(7);
        vm.AcceptanceSummary.Should().Be("Accepted 4 of 7 tags");
    }

    [Fact]
    public void Constructor_WithEmptyResult_HasZeroCounts()
    {
        var result = CreateEmptyResult();
        var vm = new AiTagReviewViewModel(result);

        vm.KeywordScores.Should().BeEmpty();
        vm.CategoryScores.Should().BeEmpty();
        vm.AcceptedCount.Should().Be(0);
        vm.TotalCount.Should().Be(0);
        vm.AcceptanceSummary.Should().Be("Accepted 0 of 0 tags");
    }

    [Fact]
    public void Constructor_Commands_AreNotNull()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.AcceptAllAboveThresholdCommand.Should().NotBeNull();
        vm.ApplyCommand.Should().NotBeNull();
        vm.CancelCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_DialogResult_IsNull()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.DialogResult.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────
    //  ConfidenceThreshold
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ConfidenceThreshold_SettingNewValue_UpdatesCounts()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // Start: 4 accepted (threshold 0.6) — 3 keywords + 1 category
        vm.AcceptedCount.Should().Be(4);

        // Lower to 0.4: clouds (0.41) and Travel (0.55) become accepted → 6
        vm.ConfidenceThreshold = 0.4;
        vm.AcceptedCount.Should().Be(6);
        vm.KeywordScores[3].IsAccepted.Should().BeTrue();  // clouds now accepted
        vm.CategoryScores[1].IsAccepted.Should().BeTrue();  // Travel now accepted
    }

    [Fact]
    public void ConfidenceThreshold_RaisingThreshold_RejectsItems()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // Raise to 0.85: landscape (0.82) and sunset (0.65) become rejected → 2 accepted
        vm.ConfidenceThreshold = 0.85;
        vm.AcceptedCount.Should().Be(2);
        vm.KeywordScores[1].IsAccepted.Should().BeFalse(); // landscape rejected
        vm.KeywordScores[2].IsAccepted.Should().BeFalse(); // sunset rejected
    }

    [Fact]
    public void ConfidenceThreshold_SettingToZero_AcceptsAll()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 0;
        vm.AcceptedCount.Should().Be(7); // all 5 keywords + 2 categories
    }

    [Fact]
    public void ConfidenceThreshold_SettingToOne_RejectsAll()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 1.0;
        vm.AcceptedCount.Should().Be(0);
    }

    [Fact]
    public void ConfidenceThreshold_SameValue_DoesNotTriggerRecalculation()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // Trigger count
        var propertyChangedCount = 0;
        vm.PropertyChanged += (_, _) => propertyChangedCount++;

        vm.ConfidenceThreshold = 0.6; // same as default

        propertyChangedCount.Should().Be(0);
        vm.AcceptedCount.Should().Be(4); // unchanged
    }

    [Fact]
    public void ConfidenceThreshold_SmallChangeWithinEpsilon_IsIgnored()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 0.603; // less than 0.01 difference → ignored
        vm.AcceptedCount.Should().Be(4); // unchanged
    }

    [Fact]
    public void ConfidenceThreshold_RoundsToTwoDecimals()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 0.6666;

        vm.ConfidenceThreshold.Should().Be(0.67);
        vm.ConfidenceThresholdPercent.Should().Be("67%");
    }

    [Fact]
    public void ConfidenceThreshold_Setter_RaisesPropertyChanged()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ConfidenceThreshold = 0.8;

        raised.Should().Contain(nameof(vm.ConfidenceThreshold));
    }

    [Fact]
    public void ConfidenceThreshold_Setter_RaisesConfidenceThresholdPercentChanged()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ConfidenceThreshold = 0.8;

        raised.Should().Contain(nameof(vm.ConfidenceThresholdPercent));
    }

    // ─────────────────────────────────────────────────────────────────
    //  ToggleKeyword / ToggleCategory
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleKeyword_FlipsIsAccepted()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        var kw = vm.KeywordScores[0]; // mountain, initially accepted
        kw.IsAccepted.Should().BeTrue();

        vm.ToggleKeyword(kw);

        kw.IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void ToggleKeyword_UpdatesCounts()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.AcceptedCount.Should().Be(4); // initial

        vm.ToggleKeyword(vm.KeywordScores[3]); // clouds: false → true

        vm.AcceptedCount.Should().Be(5);
        vm.AcceptanceSummary.Should().Be("Accepted 5 of 7 tags");
    }

    [Fact]
    public void ToggleCategory_FlipsIsAcceptedAndUpdatesCounts()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        var cat = vm.CategoryScores[1]; // Travel, initially rejected
        cat.IsAccepted.Should().BeFalse();

        vm.ToggleCategory(cat);

        cat.IsAccepted.Should().BeTrue();
        vm.AcceptedCount.Should().Be(5); // was 4, now 5
    }

    // ─────────────────────────────────────────────────────────────────
    //  INPC wiring — checkbox toggles trigger recalculation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void KeywordScore_IsAcceptedChanged_ViaBinding_FiresRecalculation()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.AcceptedCount.Should().Be(4);

        // Simulate checkbox binding setting IsAccepted on the model directly
        vm.KeywordScores[3].IsAccepted = true; // clouds: 0.41, was rejected

        vm.AcceptedCount.Should().Be(5);
    }

    [Fact]
    public void CategoryScore_IsAcceptedChanged_ViaBinding_FiresRecalculation()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.AcceptedCount.Should().Be(4);

        vm.CategoryScores[1].IsAccepted = true; // Travel: 0.55, was rejected

        vm.AcceptedCount.Should().Be(5);
    }

    [Fact]
    public void KeywordScore_IsAcceptedChanged_RaisesPropertyChangedOnScore()
    {
        var kw = new KeywordScore { Name = "test", Confidence = 0.8 };
        var raised = new List<string?>();
        kw.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        kw.IsAccepted = false;

        raised.Should().Contain(nameof(KeywordScore.IsAccepted));
    }

    [Fact]
    public void CategoryScore_IsAcceptedChanged_RaisesPropertyChangedOnScore()
    {
        var cat = new CategoryScore { Name = "test", Confidence = 0.8 };
        var raised = new List<string?>();
        cat.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        cat.IsAccepted = false;

        raised.Should().Contain(nameof(CategoryScore.IsAccepted));
    }

    // ─────────────────────────────────────────────────────────────────
    //  AcceptAllAboveThreshold command
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AcceptAllAboveThresholdCommand_AppliesCurrentThreshold()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // Start with 4 accepted (3 kw + 1 cat). Manually toggle a keyword to mess up the state.
        vm.ToggleKeyword(vm.KeywordScores[0]); // mountain: true → false, now 3 accepted

        vm.AcceptAllAboveThresholdCommand.Execute(null);

        // Should reset to threshold: mountain/landscape/sunset accepted, clouds/reflection rejected
        vm.KeywordScores[0].IsAccepted.Should().BeTrue();  // mountain 0.95 >= 0.6
        vm.KeywordScores[3].IsAccepted.Should().BeFalse(); // clouds 0.41 < 0.6
        vm.CategoryScores[0].IsAccepted.Should().BeTrue();  // Nature 0.91 >= 0.6
        vm.CategoryScores[1].IsAccepted.Should().BeFalse(); // Travel 0.55 < 0.6
        vm.AcceptedCount.Should().Be(4);
    }

    [Fact]
    public void AcceptAllAboveThresholdCommand_AfterThresholdChange_UsesNewThreshold()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 0.3;

        // Manually uncheck sunset
        vm.ToggleKeyword(vm.KeywordScores[2]);

        vm.AcceptAllAboveThresholdCommand.Execute(null);

        vm.KeywordScores[3].IsAccepted.Should().BeTrue();  // clouds 0.41 >= 0.3
        vm.KeywordScores[4].IsAccepted.Should().BeFalse(); // reflection 0.22 < 0.3
        vm.CategoryScores[1].IsAccepted.Should().BeTrue();  // Travel 0.55 >= 0.3
    }

    // ─────────────────────────────────────────────────────────────────
    //  ApplyCommand / CancelCommand
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyCommand_SetsDialogResultTrue()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ApplyCommand.Execute(null);

        vm.DialogResult.Should().BeTrue();
    }

    [Fact]
    public void ApplyCommand_RaisesCloseRequested_WithTrue()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var closeValue = false;
        var closeRaised = false;
        vm.CloseRequested += val => { closeRaised = true; closeValue = val; };

        vm.ApplyCommand.Execute(null);

        closeRaised.Should().BeTrue();
        closeValue.Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_SetsDialogResultFalse()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.CancelCommand.Execute(null);

        vm.DialogResult.Should().BeFalse();
    }

    [Fact]
    public void CancelCommand_RaisesCloseRequested_WithFalse()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var closeRaised = false;
        var closeValue = true; // default
        vm.CloseRequested += val => { closeRaised = true; closeValue = val; };

        vm.CancelCommand.Execute(null);

        closeRaised.Should().BeTrue();
        closeValue.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  GetAcceptedKeywords / GetAcceptedCategories
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAcceptedKeywords_WithThreshold_ReturnsOnlyAccepted()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        var accepted = vm.GetAcceptedKeywords();

        accepted.Should().BeEquivalentTo(["mountain", "landscape", "sunset"]);
    }

    [Fact]
    public void GetAcceptedKeywords_AfterToggle_ReflectsChanges()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        // Initial accepted: mountain, landscape, sunset (Nature as cat)
        vm.ToggleKeyword(vm.KeywordScores[2]); // sunset: accepted → rejected
        vm.ToggleKeyword(vm.KeywordScores[3]); // clouds: rejected → accepted

        var accepted = vm.GetAcceptedKeywords();

        accepted.Should().BeEquivalentTo(["mountain", "landscape", "clouds"]);
    }

    [Fact]
    public void GetAcceptedCategories_WithThreshold_ReturnsOnlyAccepted()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        var accepted = vm.GetAcceptedCategories();

        accepted.Should().BeEquivalentTo(["Nature"]);
    }

    [Fact]
    public void GetAcceptedCategories_AfterToggle_ReflectsChanges()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ToggleCategory(vm.CategoryScores[1]); // Travel: rejected → accepted

        var accepted = vm.GetAcceptedCategories();

        accepted.Should().BeEquivalentTo(["Nature", "Travel"]);
    }

    [Fact]
    public void GetAcceptedKeywords_WithEmptyResult_ReturnsEmpty()
    {
        var result = CreateEmptyResult();
        var vm = new AiTagReviewViewModel(result);

        vm.GetAcceptedKeywords().Should().BeEmpty();
        vm.GetAcceptedCategories().Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    //  PropertyChanged notifications
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleKeyword_RaisesAcceptedCountPropertyChanged()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ToggleKeyword(vm.KeywordScores[3]);

        raised.Should().Contain(nameof(vm.AcceptedCount));
        raised.Should().Contain(nameof(vm.AcceptanceSummary));
    }

    [Fact]
    public void ToggleCategory_RaisesAcceptedCountPropertyChanged()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ToggleCategory(vm.CategoryScores[1]);

        raised.Should().Contain(nameof(vm.AcceptedCount));
        raised.Should().Contain(nameof(vm.AcceptanceSummary));
    }

    [Fact]
    public void AcceptAllAboveThresholdCommand_RaisesPropertyChanged()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // First toggle something to make it meaningful
        vm.ToggleKeyword(vm.KeywordScores[0]);

        vm.AcceptAllAboveThresholdCommand.Execute(null);

        raised.Should().Contain(nameof(vm.AcceptedCount));
        raised.Should().Contain(nameof(vm.AcceptanceSummary));
    }

    // ─────────────────────────────────────────────────────────────────
    //  AcceptanceSummary
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AcceptanceSummary_UpdatesAfterThresholdChange()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 0.9;

        vm.AcceptanceSummary.Should().Be("Accepted 2 of 7 tags");
    }

    [Fact]
    public void AcceptanceSummary_WithAllRejected_ShowsZero()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 1.0;

        vm.AcceptanceSummary.Should().Be("Accepted 0 of 7 tags");
    }

    [Fact]
    public void AcceptanceSummary_WithAllAccepted_ShowsTotal()
    {
        var result = CreateSampleResult();
        var vm = new AiTagReviewViewModel(result);

        vm.ConfidenceThreshold = 0.0;

        vm.AcceptanceSummary.Should().Be("Accepted 7 of 7 tags");
    }

    [Fact]
    public void AcceptanceSummary_WithEmptyResult_ShowsZero()
    {
        var result = CreateEmptyResult();
        var vm = new AiTagReviewViewModel(result);

        vm.AcceptanceSummary.Should().Be("Accepted 0 of 0 tags");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Edge cases — single-item collections
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithSingleKeyword_WorksCorrectly()
    {
        var result = new AiTagResult
        {
            Keywords = [new KeywordScore { Name = "solitude", Confidence = 0.5 }],
            Categories = []
        };
        var vm = new AiTagReviewViewModel(result);

        vm.KeywordScores.Should().HaveCount(1);
        vm.KeywordScores[0].Name.Should().Be("solitude");
        vm.KeywordScores[0].IsAccepted.Should().BeFalse(); // 0.5 < 0.6
        vm.TotalCount.Should().Be(1);
        vm.AcceptedCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithSingleCategory_WorksCorrectly()
    {
        var result = new AiTagResult
        {
            Keywords = [],
            Categories = [new CategoryScore { Name = "Art", Confidence = 0.75 }]
        };
        var vm = new AiTagReviewViewModel(result);

        vm.CategoryScores.Should().HaveCount(1);
        vm.CategoryScores[0].Name.Should().Be("Art");
        vm.CategoryScores[0].IsAccepted.Should().BeTrue(); // 0.75 >= 0.6
        vm.TotalCount.Should().Be(1);
        vm.AcceptedCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_WithTiedConfidences_MaintainsOrder()
    {
        var result = new AiTagResult
        {
            Keywords =
            [
                new KeywordScore { Name = "a", Confidence = 0.9 },
                new KeywordScore { Name = "b", Confidence = 0.9 },
                new KeywordScore { Name = "c", Confidence = 0.7 },
            ],
            Categories = []
        };
        var vm = new AiTagReviewViewModel(result);

        // Stability of OrderByDescending for equal values is not guaranteed,
        // but all three should be present and sorted descending by confidence
        vm.KeywordScores.Should().HaveCount(3);
        vm.KeywordScores[0].Confidence.Should().Be(0.9);
        vm.KeywordScores[1].Confidence.Should().Be(0.9);
        vm.KeywordScores[2].Confidence.Should().Be(0.7);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Static helpers
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ConfidenceToColor_HighConfidence_ReturnsGreen()
    {
        AiTagReviewViewModel.ConfidenceToColor(0.95).Should().Be("#2E7D32");
        AiTagReviewViewModel.ConfidenceToColor(0.80).Should().Be("#2E7D32");
    }

    [Fact]
    public void ConfidenceToColor_MediumConfidence_ReturnsAmber()
    {
        AiTagReviewViewModel.ConfidenceToColor(0.79).Should().Be("#F57F17");
        AiTagReviewViewModel.ConfidenceToColor(0.50).Should().Be("#F57F17");
    }

    [Fact]
    public void ConfidenceToColor_LowConfidence_ReturnsRed()
    {
        AiTagReviewViewModel.ConfidenceToColor(0.49).Should().Be("#C62828");
        AiTagReviewViewModel.ConfidenceToColor(0.0).Should().Be("#C62828");
    }

    [Fact]
    public void ConfidenceToBarWidth_AtZero_ReturnsMinimum()
    {
        // The converter clamps to 20 minimum
        var width = AiTagReviewViewModel.ConfidenceToBarWidth(0);
        width.Should().Be(0); // This is the static helper, not the converter
    }

    [Fact]
    public void ConfidenceToBarWidth_AtOne_Returns100()
    {
        AiTagReviewViewModel.ConfidenceToBarWidth(1.0).Should().Be(100);
    }

    [Fact]
    public void ConfidenceToBarWidth_AtMidRange_ReturnsProportional()
    {
        AiTagReviewViewModel.ConfidenceToBarWidth(0.5).Should().Be(50);
    }
}
