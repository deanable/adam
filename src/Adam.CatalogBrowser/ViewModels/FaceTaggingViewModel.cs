using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the face tagging view: browse known persons, name unknown faces,
/// confirm/reject suggestions, and manage face assignments.
/// </summary>
public sealed class FaceTaggingViewModel : INotifyPropertyChanged
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FaceMatcherService _matcher;
    private bool _isLoading;
    private PersonGroup? _selectedPerson;
    private bool _hasUnknownFaces;

    public FaceTaggingViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        FaceMatcherService matcher)
    {
        _dbFactory = dbFactory;
        _matcher = matcher;

        RefreshCommand = new RelayCommand(async _ => await LoadAsync());
        SuggestNamesCommand = new RelayCommand(async _ => await SuggestNamesAsync());
        ConfirmFaceCommand = new RelayCommand(async item => await ConfirmFaceAsync((AssetFaceItem)item!));
        RejectFaceCommand = new RelayCommand(async item => await RejectFaceAsync((AssetFaceItem)item!));
        OpenPersonGalleryCommand = new RelayCommand(p => OpenPersonGallery((PersonGroup)p!));
    }

    public ObservableCollection<PersonGroup> Persons { get; } = [];
    public ObservableCollection<AssetFaceItem> UnknownFaces { get; } = [];

    public PersonGroup? SelectedPerson
    {
        get => _selectedPerson;
        set { _selectedPerson = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool HasUnknownFaces
    {
        get => _hasUnknownFaces;
        set { _hasUnknownFaces = value; OnPropertyChanged(); }
    }

    public int TotalFaceCount { get; private set; }

    public ICommand RefreshCommand { get; }
    public ICommand SuggestNamesCommand { get; }
    public ICommand ConfirmFaceCommand { get; }
    public ICommand RejectFaceCommand { get; }
    public ICommand OpenPersonGalleryCommand { get; }

    public event Action? NavigateToPersonGallery;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync()
    {
        IsLoading = true;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var persons = await db.Persons
            .Include(p => p.Faces)
            .OrderBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync();

        Persons.Clear();
        foreach (var person in persons)
        {
            Persons.Add(new PersonGroup
            {
                PersonId = person.Id,
                Name = person.Name,
                FaceCount = person.Faces.Count,
                Thumbnail = person.ThumbnailImage,
                CreatedAt = person.CreatedAt,
                Faces = person.Faces.Select(f => new AssetFaceItem
                {
                    FaceId = f.Id,
                    AssetId = f.AssetId,
                    Thumbnail = f.ThumbnailImage,
                    MatchingConfidence = f.MatchingConfidence,
                    MatchType = f.IsAutoAssigned ? FaceMatchType.AutoAssigned : FaceMatchType.Unknown
                }).ToList()
            });
        }

        // Load unknown faces
        var unknownFaces = await db.AssetFaces
            .Where(f => f.PersonId == null)
            .AsNoTracking()
            .ToListAsync();

        UnknownFaces.Clear();
        foreach (var face in unknownFaces)
        {
            UnknownFaces.Add(new AssetFaceItem
            {
                FaceId = face.Id,
                AssetId = face.AssetId,
                Thumbnail = face.ThumbnailImage,
                MatchingConfidence = face.MatchingConfidence,
                MatchType = FaceMatchType.Unknown
            });
        }

        HasUnknownFaces = UnknownFaces.Count > 0;
        TotalFaceCount = Persons.Sum(p => p.FaceCount) + UnknownFaces.Count;
        IsLoading = false;
    }

    public async Task SuggestNamesAsync()
    {
        IsLoading = true;

        var clusters = await _matcher.ClusterUnknownFacesAsync();

        foreach (var cluster in clusters)
        {
            // Create a new person for each cluster
            await using var db = await _dbFactory.CreateDbContextAsync();

            var person = new Person
            {
                Id = Guid.NewGuid(),
                Name = cluster.SuggestedName,
                CentroidEmbedding = cluster.CentroidEmbedding,
                EmbeddingModelVersion = "arcface-onnx-v1",
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };
            db.Persons.Add(person);

            // Link faces to the new person
            var faces = await db.AssetFaces
                .Where(f => cluster.AssetFaceIds.Contains(f.Id))
                .ToListAsync();

            foreach (var face in faces)
            {
                face.PersonId = person.Id;
                face.MatchingConfidence = cluster.AvgConfidence;
                face.IsAutoAssigned = false; // suggested, not auto-assigned
            }

            await db.SaveChangesAsync();
        }

        await LoadAsync();
        IsLoading = false;
    }

    public async Task NameFaceAsync(Guid faceId, string personName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Find or create person
        var person = await db.Persons
            .FirstOrDefaultAsync(p => p.Name == personName);

        if (person == null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                Name = personName.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };
            db.Persons.Add(person);
        }

        // Link face
        var face = await db.AssetFaces.FirstOrDefaultAsync(f => f.Id == faceId);
        if (face != null)
        {
            face.PersonId = person.Id;
            face.IsAutoAssigned = false;
        }

        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task ConfirmFaceAsync(AssetFaceItem item)
    {
        if (item.MatchType != FaceMatchType.Suggested) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var face = await db.AssetFaces.FirstOrDefaultAsync(f => f.Id == item.FaceId);
        if (face != null)
        {
            face.IsAutoAssigned = true;
            await db.SaveChangesAsync();
        }

        await LoadAsync();
    }

    private async Task RejectFaceAsync(AssetFaceItem item)
    {
        if (item.MatchType != FaceMatchType.Suggested) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var face = await db.AssetFaces.FirstOrDefaultAsync(f => f.Id == item.FaceId);
        if (face != null)
        {
            face.PersonId = null;
            face.MatchingConfidence = 0;
            face.IsAutoAssigned = false;
            await db.SaveChangesAsync();
        }

        await LoadAsync();
    }

    private void OpenPersonGallery(PersonGroup person)
    {
        NavigateToPersonGallery?.Invoke();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Represents a person group with their detected faces.
/// </summary>
public sealed record PersonGroup
{
    public Guid PersonId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int FaceCount { get; init; }
    public byte[]? Thumbnail { get; init; }
    public IReadOnlyList<AssetFaceItem> Faces { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Represents a detected face item in the tagging view.
/// </summary>
public sealed record AssetFaceItem
{
    public Guid FaceId { get; init; }
    public Guid AssetId { get; init; }
    public byte[]? Thumbnail { get; init; }
    public float MatchingConfidence { get; init; }
    public FaceMatchType MatchType { get; init; }
    public string? AssetFileName { get; init; }
}
