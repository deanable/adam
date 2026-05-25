using System.Collections.ObjectModel;
using Adam.CatalogBrowser.Controls;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// Tests for <see cref="TagEditorControl"/> — specifically that modifying the
/// <see cref="TagEditorControl.Tags"/> collection in-place (Clear, Add, Remove)
/// triggers <see cref="TagEditorControl.TagOccurrences"/> resynchronisation
/// via the <c>CollectionChanged</c> subscription added in the fix.
///
/// These tests create a control instance and exercise its property setters
/// directly — no Avalonia headless platform is required since no template
/// application or rendering occurs.
/// </summary>
public class TagEditorControlTests
{
    // ──────────────────────────────────────────────
    //  Initial sync on set
    // ──────────────────────────────────────────────

    [Fact]
    public void Tags_Set_InitializesTagOccurrences()
    {
        var control = new TagEditorControl();
        var tags = new ObservableCollection<string> { "Urban", "Summer", "Portrait" };

        control.Tags = tags;

        control.TagOccurrences.Should().NotBeNull();
        control.TagOccurrences.Should().HaveCount(3);
        control.TagOccurrences.Should().Contain(o => o.Name == "Urban" && o.Level == OccurrenceLevel.All);
        control.TagOccurrences.Should().Contain(o => o.Name == "Summer" && o.Level == OccurrenceLevel.All);
        control.TagOccurrences.Should().Contain(o => o.Name == "Portrait" && o.Level == OccurrenceLevel.All);
    }

    [Fact]
    public void Tags_SetToNull_ClearsTagOccurrences()
    {
        var control = new TagEditorControl
        {
            Tags = new ObservableCollection<string> { "Tag1" }
        };

        control.Tags = null;

        control.TagOccurrences.Should().BeNull();
    }

    [Fact]
    public void Tags_SetToEmptyCollection_HasEmptyTagOccurrences()
    {
        var control = new TagEditorControl
        {
            Tags = new ObservableCollection<string>()
        };

        control.TagOccurrences.Should().NotBeNull();
        control.TagOccurrences.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  CollectionChanged: Clear
    // ──────────────────────────────────────────────

    [Fact]
    public void Tags_Clear_ResyncsTagOccurrences()
    {
        var control = new TagEditorControl();
        var tags = new ObservableCollection<string> { "Alpha", "Beta", "Gamma" };
        control.Tags = tags;

        tags.Clear();
        control.ForceSync();

        control.TagOccurrences.Should().NotBeNull();
        control.TagOccurrences.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  CollectionChanged: Add
    // ──────────────────────────────────────────────

    [Fact]
    public void Tags_Add_ResyncsTagOccurrences()
    {
        var control = new TagEditorControl();
        var tags = new ObservableCollection<string>();
        control.Tags = tags;

        tags.Add("NewTag");
        control.ForceSync();

        control.TagOccurrences.Should().HaveCount(1);
        control.TagOccurrences![0].Name.Should().Be("NewTag");
        control.TagOccurrences[0].Level.Should().Be(OccurrenceLevel.All);
    }

    // ──────────────────────────────────────────────
    //  CollectionChanged: Remove
    // ──────────────────────────────────────────────

    [Fact]
    public void Tags_Remove_ResyncsTagOccurrences()
    {
        var control = new TagEditorControl();
        var tags = new ObservableCollection<string> { "Keep", "Remove" };
        control.Tags = tags;

        tags.Remove("Remove");
        control.ForceSync();

        control.TagOccurrences.Should().HaveCount(1);
        control.TagOccurrences![0].Name.Should().Be("Keep");
    }

    // ──────────────────────────────────────────────
    //  CollectionChanged: Replace (Clear + Add in bulk)
    // ──────────────────────────────────────────────

    [Fact]
    public void Tags_ClearAndAdd_ResyncsTagOccurrences()
    {
        // Simulates the ViewModel pattern: reuse the same collection,
        // clear it and repopulate with new asset's tags.
        // ForceSync flushes the coalesced deferred sync so we can observe
        // the final state from a single sync call.
        var control = new TagEditorControl();
        var tags = new ObservableCollection<string> { "Old1", "Old2" };
        control.Tags = tags;

        tags.Clear();
        tags.Add("New1");
        tags.Add("New2");
        tags.Add("New3");
        control.ForceSync();

        control.TagOccurrences.Should().HaveCount(3);
        control.TagOccurrences.Should().ContainSingle(o => o.Name == "New1");
        control.TagOccurrences.Should().ContainSingle(o => o.Name == "New2");
        control.TagOccurrences.Should().ContainSingle(o => o.Name == "New3");
        control.TagOccurrences.Should().NotContain(o => o.Name == "Old1");
        control.TagOccurrences.Should().NotContain(o => o.Name == "Old2");
    }

    // ──────────────────────────────────────────────
    //  Collection reassignment
    // ──────────────────────────────────────────────

    [Fact]
    public void Tags_ReassignedToNewCollection_SubscribesToNew()
    {
        var control = new TagEditorControl();
        var first = new ObservableCollection<string> { "A", "B" };
        control.Tags = first;

        // Assign a new collection
        var second = new ObservableCollection<string> { "X", "Y", "Z" };
        control.Tags = second;

        control.TagOccurrences.Should().HaveCount(3);
        control.TagOccurrences.Should().Contain(o => o.Name == "X");
        control.TagOccurrences.Should().Contain(o => o.Name == "Y");
        control.TagOccurrences.Should().Contain(o => o.Name == "Z");

        // Modifying the new collection should still resync
        second.Add("W");
        control.ForceSync();
        control.TagOccurrences.Should().HaveCount(4);

        // Modifying the OLD collection should NOT affect TagOccurrences
        first.Add("Orphan");
        control.ForceSync();
        control.TagOccurrences.Should().HaveCount(4);
        control.TagOccurrences.Should().NotContain(o => o.Name == "Orphan");
    }

    // ──────────────────────────────────────────────
    //  TagOccurrences property sync
    // ──────────────────────────────────────────────

    [Fact]
    public void TagOccurrences_SetDirectly_DoesNotModifyTags()
    {
        var control = new TagEditorControl();
        var occurrences = new ObservableCollection<TagOccurrence>
        {
            new() { Name = "Direct", Level = OccurrenceLevel.Some }
        };

        control.TagOccurrences = occurrences;

        control.Tags.Should().BeNull();
        control.TagOccurrences.Should().BeSameAs(occurrences);
    }
}
