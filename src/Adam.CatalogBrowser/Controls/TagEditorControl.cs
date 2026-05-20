using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// A custom control for editing a collection of text-based tags.
///
/// Layout:
///   ┌─────────────────────────────────────┐
///   │ [Tag1 ×] [Tag2 ×] [Tag3 ×]          │
///   │ [type to add…                 ▼]    │
///   └─────────────────────────────────────┘
///
/// <para><b>Single-asset mode</b> (default): Bind <see cref="Tags"/> to ObservableCollection{string}.</para>
/// <para><b>Multi-asset mode</b>: Set <see cref="IsMultiAsset"/> = true and bind
/// <see cref="TagOccurrences"/> to ObservableCollection{TagOccurrence}. Each chip's
/// background colour reflects the <see cref="OccurrenceLevel"/>:
/// green (all), orange (some), red (one).</para>
/// </summary>
public class TagEditorControl : TemplatedControl
{
    private const string PartTagInput = "PART_TagInput";

    private AutoCompleteBox? _tagInput;
    private bool _isHandlingSelection;

    // ──────────────────────────────────────────────
    //  Properties
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="Tags"/> property.
    /// </summary>
    public static readonly DirectProperty<TagEditorControl, ObservableCollection<string>?> TagsProperty =
        AvaloniaProperty.RegisterDirect<TagEditorControl, ObservableCollection<string>?>(
            nameof(Tags),
            o => o.Tags,
            (o, v) => o.Tags = v);

    /// <summary>
    /// Defines the <see cref="TagOccurrences"/> property.
    /// </summary>
    public static readonly DirectProperty<TagEditorControl, ObservableCollection<TagOccurrence>?> TagOccurrencesProperty =
        AvaloniaProperty.RegisterDirect<TagEditorControl, ObservableCollection<TagOccurrence>?>(
            nameof(TagOccurrences),
            o => o.TagOccurrences,
            (o, v) => o.TagOccurrences = v);

    /// <summary>
    /// Defines the <see cref="AutoCompleteSource"/> property.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<string>?> AutoCompleteSourceProperty =
        AvaloniaProperty.Register<TagEditorControl, IEnumerable<string>?>(nameof(AutoCompleteSource));

    /// <summary>
    /// Defines the <see cref="PlaceholderText"/> property.
    /// </summary>
    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<TagEditorControl, string>(nameof(PlaceholderText), "Add tag…");

    /// <summary>
    /// Defines the <see cref="IsMultiAsset"/> property.
    /// When true, chips show occurrence-based colours.
    /// </summary>
    public static readonly StyledProperty<bool> IsMultiAssetProperty =
        AvaloniaProperty.Register<TagEditorControl, bool>(nameof(IsMultiAsset));

    private ObservableCollection<string>? _tags;
    private ObservableCollection<TagOccurrence>? _tagOccurrences;

    /// <summary>
    /// The collection of tags to display and edit (single-asset mode).
    /// Each string is internally converted to a <see cref="TagOccurrence"/> with
    /// <see cref="OccurrenceLevel.All"/> for rendering.
    /// </summary>
    public ObservableCollection<string>? Tags
    {
        get => _tags;
        set
        {
            SetAndRaise(TagsProperty, ref _tags, value);
            SyncTagsToOccurrences();
        }
    }

    /// <summary>
    /// The collection of tags with occurrence-level metadata (multi-asset mode).
    /// When set directly, <see cref="Tags"/> is NOT auto-updated — the consumer
    /// manages both collections as needed.
    /// </summary>
    public ObservableCollection<TagOccurrence>? TagOccurrences
    {
        get => _tagOccurrences;
        set
        {
            SetAndRaise(TagOccurrencesProperty, ref _tagOccurrences, value);
            SubscribeToOccurrenceChanges();
        }
    }

    /// <summary>
    /// Source collection of suggestions shown in the autocomplete dropdown.
    /// Typically populated with all existing keywords in the system.
    /// </summary>
    public IEnumerable<string>? AutoCompleteSource
    {
        get => GetValue(AutoCompleteSourceProperty);
        set => SetValue(AutoCompleteSourceProperty, value);
    }

    /// <summary>
    /// Placeholder text displayed in the text entry when empty.
    /// </summary>
    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// When true, the control is in multi-asset mode and tag chips
    /// display occurrence-based background colours (green/orange/red).
    /// The autocomplete text entry is hidden in this mode since editing
    /// across multiple assets requires custom logic.
    /// </summary>
    public bool IsMultiAsset
    {
        get => GetValue(IsMultiAssetProperty);
        set => SetValue(IsMultiAssetProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Sync helpers
    // ──────────────────────────────────────────────

    private void SyncTagsToOccurrences()
    {
        if (_tags == null)
        {
            TagOccurrences = null;
            return;
        }

        var occurrences = new ObservableCollection<TagOccurrence>(
            _tags.Select(t => new TagOccurrence { Name = t, Level = OccurrenceLevel.All }));

        TagOccurrences = occurrences;
    }

    private void SubscribeToOccurrenceChanges()
    {
        // No additional wiring needed — TagOccurrence.Level changes fire
        // PropertyChanged which the binding system picks up automatically.
    }

    // ──────────────────────────────────────────────
    //  RemoveTagCommand (internal)
    // ──────────────────────────────────────────────

    private ICommand? _removeTagCommand;

    /// <summary>
    /// Command that removes a tag from <see cref="Tags"/> or <see cref="TagOccurrences"/>.
    /// Bound to the × button on each tag chip.
    /// </summary>
    public ICommand? RemoveTagCommand =>
        _removeTagCommand ??= new TagEditorRelayCommand(RemoveTag);

    private void RemoveTag(object? parameter)
    {
        switch (parameter)
        {
            case string tag when _tags != null:
                _tags.Remove(tag);
                // Also remove from occurrences if they're synced
                if (_tagOccurrences != null)
                {
                    var match = _tagOccurrences.FirstOrDefault(o =>
                        string.Equals(o.Name, tag, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        _tagOccurrences.Remove(match);
                }
                break;

            case TagOccurrence occurrence when _tagOccurrences != null:
                _tagOccurrences.Remove(occurrence);
                // Also remove from tags if they're in sync
                if (_tags != null)
                    _tags.Remove(occurrence.Name);
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  Template lifecycle
    // ──────────────────────────────────────────────

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_tagInput != null)
        {
            _tagInput.LostFocus -= OnTagInputLostFocus;
            _tagInput.KeyDown -= OnTagInputKeyDown;
            _tagInput.SelectionChanged -= OnTagInputSelectionChanged;
        }

        _tagInput = e.NameScope.Find<AutoCompleteBox>(PartTagInput);

        if (_tagInput != null)
        {
            _tagInput.LostFocus += OnTagInputLostFocus;
            _tagInput.KeyDown += OnTagInputKeyDown;
            _tagInput.SelectionChanged += OnTagInputSelectionChanged;
        }
    }

    // ──────────────────────────────────────────────
    //  Event handlers
    // ──────────────────────────────────────────────

    private void OnTagInputLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitPendingTag();
    }

    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPendingTag();
            e.Handled = true;
        }
    }

    private void OnTagInputSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isHandlingSelection) return;
        _isHandlingSelection = true;

        try
        {
            if (_tagInput == null) return;

            var selected = _tagInput.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                AddTag(selected);
                _tagInput.Text = string.Empty;
                _tagInput.SelectedItem = null;
            }
        }
        finally
        {
            _isHandlingSelection = false;
        }
    }

    // ──────────────────────────────────────────────
    //  Tag management
    // ──────────────────────────────────────────────

    private void CommitPendingTag()
    {
        if (_tagInput == null) return;

        var text = _tagInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        AddTag(text);
        _tagInput.Text = string.Empty;
    }

    /// <summary>
    /// Adds a tag to both <see cref="Tags"/> and <see cref="TagOccurrences"/>.
    /// Avoids duplicates (case-insensitive).
    /// </summary>
    private void AddTag(string tag)
    {
        var trimmed = tag.Trim();
        if (trimmed.Length == 0) return;

        // Check for duplicates in whichever collection is active
        if (_tags != null)
        {
            if (_tags.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
                return;
            _tags.Add(trimmed);
        }

        if (_tagOccurrences != null)
        {
            if (_tagOccurrences.Any(o => string.Equals(o.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                return;
            _tagOccurrences.Add(new TagOccurrence
            {
                Name = trimmed,
                Level = IsMultiAsset ? OccurrenceLevel.One : OccurrenceLevel.All
            });
        }
    }
}

/// <summary>
/// Minimal ICommand implementation for internal use in TagEditorControl.
/// </summary>
internal class TagEditorRelayCommand : ICommand
{
    private readonly Action<object?> _execute;

    public TagEditorRelayCommand(Action<object?> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute(parameter);
}
