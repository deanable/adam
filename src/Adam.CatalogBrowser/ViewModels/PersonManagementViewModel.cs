using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the person management dialog: rename, merge, delete persons.
/// </summary>
public sealed class PersonManagementViewModel : INotifyPropertyChanged
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FaceMatcherService _matcher;
    private PersonItem? _selectedPerson;
    private string? _editName;
    private string? _editNotes;
    private PersonItem? _mergeTarget;
    private bool _isLoading;

    public PersonManagementViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        FaceMatcherService matcher)
    {
        _dbFactory = dbFactory;
        _matcher = matcher;

        RenameCommand = new RelayCommand(async _ => await RenameAsync(), _ => SelectedPerson != null);
        MergeCommand = new RelayCommand(async _ => await MergeAsync(), _ => SelectedPerson != null && MergeTarget != null);
        DeleteCommand = new RelayCommand(async _ => await DeleteAsync(), _ => SelectedPerson != null);
        RefreshCommand = new RelayCommand(async _ => await LoadAsync());
        OpenGalleryCommand = new RelayCommand(_ => OpenGallery());
    }

    public ObservableCollection<PersonItem> Persons { get; } = [];

    public PersonItem? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            _selectedPerson = value;
            OnPropertyChanged();
            EditName = value?.Name;
            EditNotes = value?.Notes;
            ((RelayCommand)RenameCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MergeCommand).RaiseCanExecuteChanged();
        }
    }

    public string? EditName
    {
        get => _editName;
        set { _editName = value; OnPropertyChanged(); }
    }

    public string? EditNotes
    {
        get => _editNotes;
        set { _editNotes = value; OnPropertyChanged(); }
    }

    public PersonItem? MergeTarget
    {
        get => _mergeTarget;
        set
        {
            _mergeTarget = value;
            OnPropertyChanged();
            ((RelayCommand)MergeCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public ICommand RenameCommand { get; }
    public ICommand MergeCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenGalleryCommand { get; }

    public event Action<Guid>? NavigateToPersonGallery;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync()
    {
        IsLoading = true;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Load person data (DateTimeOffset not supported by SQLite in aggregation queries)
        var personsData = await db.Persons
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.ThumbnailImage,
                p.Notes,
                p.CreatedAt,
                FaceCount = p.Faces.Count,
                AvgConfidence = p.Faces.Any() ? p.Faces.Average(f => (double?)f.MatchingConfidence) ?? 0 : 0,
            })
            .OrderBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync();

        // Compute LastSeenAt client-side to avoid SQLite DateTimeOffset limitations
        var personIds = personsData.Select(p => p.Id).ToList();
        var allFaces = await db.AssetFaces
            .Where(f => f.PersonId.HasValue && personIds.Contains(f.PersonId.Value))
            .Include(f => f.Asset)
            .AsNoTracking()
            .ToListAsync();

        var lastSeenMap = allFaces
            .Where(f => f.Asset != null)
            .GroupBy(f => f.PersonId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(f => f.Asset!.CreatedAt));

        Persons.Clear();
        foreach (var p in personsData)
        {
            Persons.Add(new PersonItem
            {
                Id = p.Id,
                Name = p.Name,
                Thumbnail = p.ThumbnailImage,
                Notes = p.Notes,
                FaceCount = p.FaceCount,
                AvgConfidence = (float)p.AvgConfidence,
                CreatedAt = p.CreatedAt,
                LastSeenAt = lastSeenMap.TryGetValue(p.Id, out var lastSeen) ? lastSeen : p.CreatedAt
            });
        }

        IsLoading = false;
    }

    private async Task RenameAsync()
    {
        if (SelectedPerson == null || string.IsNullOrWhiteSpace(EditName)) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var person = await db.Persons.FindAsync(SelectedPerson.Id);
        if (person != null)
        {
            person.Name = EditName.Trim();
            person.Notes = EditNotes?.Trim();
            person.ModifiedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await LoadAsync();
    }

    private async Task MergeAsync()
    {
        if (SelectedPerson == null || MergeTarget == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var source = await db.Persons
            .Include(p => p.Faces)
            .FirstOrDefaultAsync(p => p.Id == SelectedPerson.Id);

        var target = await db.Persons
            .FirstOrDefaultAsync(p => p.Id == MergeTarget.Id);

        if (source == null || target == null) return;

        // Move all faces from source to target
        foreach (var face in source.Faces.ToList())
        {
            face.PersonId = target.Id;
        }

        // Recompute target centroid
        var centroid = await _matcher.ComputeCentroidAsync(target.Id);
        target.CentroidEmbedding = centroid;
        target.ModifiedAt = DateTimeOffset.UtcNow;

        db.Persons.Remove(source);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task DeleteAsync()
    {
        if (SelectedPerson == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var person = await db.Persons
            .Include(p => p.Faces)
            .FirstOrDefaultAsync(p => p.Id == SelectedPerson.Id);

        if (person == null) return;

        // Unlink all faces
        foreach (var face in person.Faces.ToList())
        {
            face.PersonId = null;
            face.IsAutoAssigned = false;
        }

        db.Persons.Remove(person);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private void OpenGallery()
    {
        if (SelectedPerson != null)
            NavigateToPersonGallery?.Invoke(SelectedPerson.Id);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Represents a person item in the management list.
/// </summary>
public sealed record PersonItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public byte[]? Thumbnail { get; init; }
    public string? Notes { get; init; }
    public int FaceCount { get; init; }
    public float AvgConfidence { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
}
