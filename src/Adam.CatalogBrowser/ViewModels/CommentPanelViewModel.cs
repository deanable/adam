using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the comment panel on the right side of MainWindow.
/// Displays threaded comments for the currently selected asset.
/// </summary>
public sealed class CommentPanelViewModel : INotifyPropertyChanged
{
    private readonly CommentService _commentService;
    private readonly ModeManager _modeManager;
    private readonly ToastService _toastService;
    private readonly ILogger<CommentPanelViewModel> _logger;
    private readonly IUiDispatcher _dispatcher;
    private Guid _currentAssetId;
    private bool _isLoading;
    private bool _isExpanded = true;
    private string _newCommentText = string.Empty;
    private string? _replyTargetId;
    private string? _editTargetId;
    private string? _editText;
    private int _totalCommentCount;

    public CommentPanelViewModel(
        CommentService commentService,
        ModeManager modeManager,
        ToastService toastService,
        ILogger<CommentPanelViewModel>? logger = null,
        IUiDispatcher? dispatcher = null)
    {
        _commentService = commentService;
        _modeManager = modeManager;
        _toastService = toastService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CommentPanelViewModel>.Instance;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        // Wire broker notification for live updates (multi-user)
        if (modeManager.BrokerClient != null)
        {
            modeManager.BrokerClient.NotificationReceived += (_, _) =>
            {
                _dispatcher.Post(async () =>
                {
                    if (_currentAssetId != Guid.Empty)
                        await LoadCommentsAsync(_currentAssetId);
                });
            };
        }

        // Initialize commands
        AddCommentCommand = new RelayCommand(async _ => await AddCommentAsync(), _ => !string.IsNullOrWhiteSpace(NewCommentText));
        RefreshCommand = new RelayCommand(async _ => { if (_currentAssetId != Guid.Empty) await LoadCommentsAsync(_currentAssetId); });
        CancelReplyCommand = new RelayCommand(_ => CancelReply());
        EditCommand = new RelayCommand(async p => await EditCommentAsync(p?.ToString() ?? string.Empty));
        DeleteCommand = new RelayCommand(async p => await DeleteCommentAsync(p?.ToString() ?? string.Empty));
        ReplyCommand = new RelayCommand(p => StartReply(p?.ToString() ?? string.Empty));
    }

    public ObservableCollection<CommentThread> Threads { get; } = [];

    public int TotalCommentCount
    {
        get => _totalCommentCount;
        set { _totalCommentCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderText)); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>Header text like "Comments (5)" or "Comments".</summary>
    public string HeaderText => TotalCommentCount > 0 ? $"Comments ({TotalCommentCount})" : "Comments";

    /// <summary>Whether there are any comments to display.</summary>
    public bool HasComments => Threads.Count > 0;

    public string NewCommentText
    {
        get => _newCommentText;
        set { _newCommentText = value; OnPropertyChanged(); (AddCommentCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public string? ReplyTargetId
    {
        get => _replyTargetId;
        set { _replyTargetId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReplying)); OnPropertyChanged(nameof(ReplyPlaceholder)); }
    }

    public bool IsReplying => _replyTargetId != null;

    public string ReplyPlaceholder => "Write a reply...";

    public string? EditTargetId
    {
        get => _editTargetId;
        set { _editTargetId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEditing)); }
    }

    public bool IsEditing => _editTargetId != null;

    public string? EditText
    {
        get => _editText;
        set { _editText = value; OnPropertyChanged(); }
    }

    // ─── Commands ──────────────────────────────────────────────

    public ICommand AddCommentCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CancelReplyCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ReplyCommand { get; }

    /// <summary>
    /// Loads comments for the specified asset. Called when asset selection changes.
    /// </summary>
    public async Task LoadCommentsAsync(Guid assetId)
    {
        _currentAssetId = assetId;
        IsLoading = true;

        try
        {
            var comments = await _commentService.ListCommentsAsync(assetId);

            await _dispatcher.InvokeAsync(() =>
            {
                Threads.Clear();

                // Group into top-level comments with replies
                var topLevel = comments.Where(c => c.ParentCommentId == null).ToList();
                foreach (var c in topLevel)
                {
                    var thread = new CommentThread(c);
                    thread.Replies.AddRange(
                        comments
                            .Where(r => r.ParentCommentId == c.Id)
                            .OrderBy(r => r.CreatedAtUnix)
                            .Select(r => new CommentItem(r)));
                    Threads.Add(thread);
                }

                TotalCommentCount = comments.Count;
                OnPropertyChanged(nameof(HasComments));
                CancelReply();
                CancelEdit();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load comments for asset {AssetId}", assetId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Posts a new top-level comment.
    /// </summary>
    public async Task AddCommentAsync()
    {
        var text = NewCommentText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || _currentAssetId == Guid.Empty) return;

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return;

        try
        {
            await _commentService.CreateCommentAsync(_currentAssetId, null, text, userId);
            NewCommentText = string.Empty;
            await LoadCommentsAsync(_currentAssetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add comment");
            _toastService.Show("Failed to add comment", Services.ToastLevel.Error);
        }
    }

    /// <summary>
    /// Posts a reply to the specified comment.
    /// </summary>
    public async Task ReplyToAsync(string parentCommentId)
    {
        var text = NewCommentText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || _currentAssetId == Guid.Empty) return;

        if (!Guid.TryParse(parentCommentId, out var parentGuid)) return;

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return;

        try
        {
            await _commentService.CreateCommentAsync(_currentAssetId, parentGuid, text, userId);
            NewCommentText = string.Empty;
            ReplyTargetId = null;
            await LoadCommentsAsync(_currentAssetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reply to comment");
            _toastService.Show("Failed to reply", Services.ToastLevel.Error);
        }
    }

    /// <summary>
    /// Edits a comment body.
    /// </summary>
    public async Task EditCommentAsync(string commentId)
    {
        var text = EditText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!Guid.TryParse(commentId, out var commentGuid)) return;

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return;

        try
        {
            await _commentService.UpdateCommentAsync(commentGuid, text, userId);
            EditTargetId = null;
            EditText = null;
            await LoadCommentsAsync(_currentAssetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit comment");
            _toastService.Show("Failed to edit comment", Services.ToastLevel.Error);
        }
    }

    /// <summary>
    /// Deletes a comment (soft-delete).
    /// </summary>
    public async Task DeleteCommentAsync(string commentId)
    {
        if (!Guid.TryParse(commentId, out var commentGuid)) return;

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return;

        try
        {
            var ok = await _commentService.DeleteCommentAsync(commentGuid, userId);
            if (ok)
                await LoadCommentsAsync(_currentAssetId);
            else
                _toastService.Show("Could not delete comment — permission denied", Services.ToastLevel.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete comment");
            _toastService.Show("Failed to delete comment", Services.ToastLevel.Error);
        }
    }

    /// <summary>
    /// Starts a reply to the given comment.
    /// </summary>
    public void StartReply(string commentId)
    {
        ReplyTargetId = commentId;
        EditTargetId = null;
    }

    /// <summary>
    /// Starts editing the given comment.
    /// </summary>
    public void StartEdit(string commentId, string currentBody)
    {
        EditTargetId = commentId;
        EditText = currentBody;
        ReplyTargetId = null;
    }

    public void CancelReply()
    {
        ReplyTargetId = null;
    }

    public void CancelEdit()
    {
        EditTargetId = null;
        EditText = null;
    }

    // Consistent local user ID for standalone mode
    private static readonly Guid StandaloneUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Guid GetCurrentUserId()
    {
        if (_modeManager.IsStandalone)
            return StandaloneUserId;

        var auth = _modeManager.AuthSession;
        if (auth?.CurrentUser?.Id is { Length: > 0 } idStr && Guid.TryParse(idStr, out var uid))
            return uid;

        return Guid.Empty;
    }

    // ─── PropertyChanged ───────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// A top-level comment with its replies. Used for UI binding.
/// </summary>
public sealed class CommentThread
{
    public CommentItem Comment { get; }
    public List<CommentItem> Replies { get; } = [];
    public bool HasReplies => Replies.Count > 0;
    public string ReplyCountText => HasReplies ? $"▶ {Replies.Count} repl{(Replies.Count == 1 ? "y" : "ies")}" : string.Empty;
    public CommentThread(CommentDto dto) => Comment = new CommentItem(dto);
}

/// <summary>
/// A single comment item (top-level or reply). Used for UI binding.
/// </summary>
public sealed class CommentItem
{
    public CommentDto Dto { get; }
    public CommentItem(CommentDto dto) => Dto = dto;

    public string Id => Dto.Id;
    public string Body => Dto.Body;
    public string Username => Dto.Username;
    public long CreatedAtUnix => Dto.CreatedAtUnix;
    public string CreatedAtText => FormatRelativeTime(Dto.CreatedAtUnix);
    public string? EditedAtText => Dto.EditedAtUnix.HasValue ? $"edited {FormatRelativeTime(Dto.EditedAtUnix.Value)}" : null;
    public bool CanEdit => Dto.CanEdit;
    public bool CanDelete => Dto.CanDelete;
    public bool IsDeleted => Dto.Body == "[deleted]";

    private static string FormatRelativeTime(long unixSeconds)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var diff = DateTimeOffset.UtcNow - dt;

        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 2) return "1 min ago";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 2) return "1 hour ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} hours ago";
        if (diff.TotalDays < 2) return "1 day ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} days ago";

        return dt.ToString("MMM d, yyyy");
    }
}

/// <summary>
/// Simple RelayCommand for internal use.
/// </summary>

