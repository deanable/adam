using Adam.CatalogBrowser.ViewModels;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;
using Adam.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for CommentPanelViewModel — UI logic, state, commands.
/// </summary>
public sealed class CommentPanelViewModelTests
{
    private readonly string _basePath;
    private readonly ModeManager _modeManager;
    private readonly CommentService _commentService;
    private readonly ToastService _toastService;
    private readonly CommentPanelViewModel _vm;

    public CommentPanelViewModelTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _modeManager = new ModeManager(_basePath);
        _modeManager.InitializeAsync().GetAwaiter().GetResult();

        _commentService = new CommentService(_modeManager, NullLogger<CommentService>.Instance);
        _toastService = new ToastService();
        _vm = new CommentPanelViewModel(_commentService, _modeManager, _toastService,
            NullLogger<CommentPanelViewModel>.Instance, new SyncUiDispatcher());
    }

    [Fact]
    public void Constructor_InitialState_IsCorrect()
    {
        Assert.Empty(_vm.Threads);
        Assert.Equal(0, _vm.TotalCommentCount);
        Assert.Equal("Comments", _vm.HeaderText);
        Assert.False(_vm.HasComments);
        Assert.False(_vm.IsLoading);
    }

    [Fact]
    public void TotalCommentCount_Updates_HeaderText()
    {
        _vm.TotalCommentCount = 5;
        Assert.Equal("Comments (5)", _vm.HeaderText);
    }

    [Fact]
    public void HeaderText_ZeroComments_ShowsPlain()
    {
        _vm.TotalCommentCount = 0;
        Assert.Equal("Comments", _vm.HeaderText);
    }

    [Fact]
    public void IsReplying_Reflects_ReplyTargetId()
    {
        Assert.False(_vm.IsReplying);
        _vm.ReplyTargetId = "some-id";
        Assert.True(_vm.IsReplying);
        _vm.ReplyTargetId = null;
        Assert.False(_vm.IsReplying);
    }

    [Fact]
    public void StartReply_SetsReplyTarget()
    {
        _vm.StartReply("comment-1");
        Assert.True(_vm.IsReplying);
        Assert.Equal("comment-1", _vm.ReplyTargetId);
    }

    [Fact]
    public void CancelReply_ClearsReplyTarget()
    {
        _vm.StartReply("comment-1");
        _vm.CancelReply();
        Assert.Null(_vm.ReplyTargetId);
    }

    [Fact]
    public void StartEdit_SetsEditState()
    {
        _vm.StartEdit("comment-1", "Original body");
        Assert.True(_vm.IsEditing);
        Assert.Equal("comment-1", _vm.EditTargetId);
        Assert.Equal("Original body", _vm.EditText);
    }

    [Fact]
    public void CancelEdit_ClearsEditState()
    {
        _vm.StartEdit("comment-1", "Body");
        _vm.CancelEdit();
        Assert.Null(_vm.EditTargetId);
        Assert.Null(_vm.EditText);
    }

    /// <summary>
    /// Synchronous dispatcher for test use — runs actions inline.
    /// </summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public void Post(Action action) => action();
        public bool CheckAccess() => true;
    }
}
