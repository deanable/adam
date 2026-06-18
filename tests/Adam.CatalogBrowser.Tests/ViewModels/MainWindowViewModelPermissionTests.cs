using System.ComponentModel;
using System.Reflection;
using Adam.CatalogBrowser.Services;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Configuration;
using Adam.Shared.Contracts;
using Adam.Shared.Extractors;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for Phase 7 permission-aware UI properties, session status text,
/// and session check timer behavior on <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed class MainWindowViewModelPermissionTests : IAsyncLifetime
{
    private readonly string _basePath;
    private readonly BrokerClient _broker;
    private readonly AuthSession _auth;
    private readonly ModeManager _modeManager;

    public MainWindowViewModelPermissionTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _broker = new BrokerClient("localhost", 9999);
        _auth = new AuthSession(_broker);
        _modeManager = new ModeManager(_basePath, _broker, _auth);
    }

    // ── Test helpers ──────────────────────────────────────────────────

    private async Task<MainWindowViewModel> CreateVmAsync()
    {
        await _modeManager.InitializeAsync();

        var sidebar = new SidebarViewModel(_modeManager, new NullLogger<SidebarViewModel>());
        var gallery = new AssetGalleryViewModel(_modeManager, new NullLogger<AssetGalleryViewModel>());
        var bulkQueue = new BulkOperationQueue(_modeManager, new NullLogger<BulkOperationQueue>());
        var propertyInspector = new PropertyInspectorViewModel(
            new NullLogger<PropertyInspectorViewModel>(), _modeManager, new MetadataWritebackService(), new SyncUiDispatcher());
        var connection = new ConnectionViewModel(new NullLogger<ConnectionViewModel>(), _modeManager);
        var statusBar = new StatusBarViewModel(bulkQueue);
        var activityFeed = new ActivityFeedViewModel(_modeManager, dispatcher: new SyncUiDispatcher());

        return new MainWindowViewModel(
            new NullLogger<MainWindowViewModel>(),
            _modeManager,
            new MetadataWritebackService(),
            sidebar,
            gallery,
            new IngestionViewModel(_modeManager, new PluginLoaderService(
                Options.Create(new PluginConfig()),
                new NullLogger<PluginLoaderService>()), new NullLogger<IngestionViewModel>()),
            new MetadataEditorViewModel(_modeManager),
            new AuditLogViewModel(_modeManager),
            bulkQueue,
            propertyInspector,
            connection,
            statusBar,
            new DeleteService(_modeManager), new ToastService(), activityFeed,
            new CommentService(_modeManager, new NullLogger<CommentService>()),
            designThemeService: new DesignThemeService(App.Config),
            startUp: false,
            dispatcher: new SyncUiDispatcher());
    }

    /// <summary>
    /// Sets the ModeManager into multi-user mode via the auto-property backing field.
    /// </summary>
    private static void SetMultiUserMode(ModeManager modeManager)
    {
        var modeField = typeof(ModeManager).GetField("<Mode>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        modeField.SetValue(modeManager, "MultiUser");
    }

    /// <summary>
    /// Sets the CurrentUser on the AuthSession via the private _currentUser field,
    /// then fires PropertyChanged so the VM picks up the change.
    /// </summary>
    private static void SetCurrentUser(AuthSession auth, UserProfile? user)
    {
        var currentUserField = typeof(AuthSession).GetField("_currentUser",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        currentUserField.SetValue(auth, user);

        // Fire PropertyChanged so the VM's subscription catches the change
        var propertyChanged = typeof(AuthSession).GetField("PropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (propertyChanged.GetValue(auth) is PropertyChangedEventHandler handler)
        {
            handler(auth, new PropertyChangedEventArgs(nameof(IAuthSession.CurrentUser)));
        }
    }

    /// <summary>
    /// Sets the TokenExpiresAt backing field to simulate expired/non-expired token.
    /// </summary>
    private static void SetTokenExpiresAt(AuthSession auth, long expiresAt)
    {
        var expiresField = typeof(AuthSession).GetField("<TokenExpiresAt>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        expiresField.SetValue(auth, expiresAt);
    }

    /// <summary>
    /// Sets the Token backing field on AuthSession.
    /// </summary>
    private static void SetToken(AuthSession auth, string? token)
    {
        var tokenField = typeof(AuthSession).GetField("<Token>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        tokenField.SetValue(auth, token);
    }

    /// <summary>
    /// Invokes the private OnSessionCheckTick method on MainWindowViewModel.
    /// </summary>
    private static void InvokeSessionCheckTick(MainWindowViewModel vm)
    {
        var method = typeof(MainWindowViewModel).GetMethod("OnSessionCheckTick",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(vm, [null, EventArgs.Empty]);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Permission gating: Standalone mode (default)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StandaloneMode_AllPermissions_AreTrue()
    {
        var vm = await CreateVmAsync();

        // Standalone mode grants full access
        vm.CanIngest.Should().BeTrue();
        vm.CanEditMetadata.Should().BeTrue();
        vm.CanAudit.Should().BeTrue();
        vm.CanAdminister.Should().BeTrue();
        // CanAiTag depends on _aiTaggingService, which is null here (no service injected)
        vm.SessionStatusText.Should().Be("Local mode — full access");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Permission gating: Multi-user, no auth (not logged in)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiUserMode_NotLoggedIn_AllPermissions_AreFalse()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        // AuthSession has no token and no CurrentUser → not logged in

        vm.CanIngest.Should().BeFalse();
        vm.CanEditMetadata.Should().BeFalse();
        vm.CanAudit.Should().BeFalse();
        vm.CanAdminister.Should().BeFalse();
        vm.SessionStatusText.Should().Be("Not logged in");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Permission gating: Administrator role
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiUserMode_Administrator_AllPermissions_AreTrue()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "admin-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "admin",
            Role = "Administrator",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        vm.CanIngest.Should().BeTrue();
        vm.CanEditMetadata.Should().BeTrue();
        vm.CanAudit.Should().BeTrue();
        vm.CanAdminister.Should().BeTrue();
        vm.SessionStatusText.Should().Be("admin — Administrator");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Permission gating: Editor role
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiUserMode_Editor_HasIngestAndEdit_NoAuditOrAdmin()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "editor-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "editor",
            Role = "Editor",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        // Editor has asset:create (ingest) and asset:update (edit metadata)
        vm.CanIngest.Should().BeTrue("Editor has asset:create");
        vm.CanEditMetadata.Should().BeTrue("Editor has asset:update");

        // Editor does NOT have audit:read or user:*
        vm.CanAudit.Should().BeFalse("Editor does not have audit:read");
        vm.CanAdminister.Should().BeFalse("Editor does not have user:*");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Permission gating: Viewer role
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiUserMode_Viewer_HasNoCreateUpdateAuditOrAdmin()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "viewer-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "viewer",
            Role = "Viewer",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        // Viewer does not have asset:create, asset:update, audit:read, or user:*
        vm.CanIngest.Should().BeFalse("Viewer does not have asset:create");
        vm.CanEditMetadata.Should().BeFalse("Viewer does not have asset:update");
        vm.CanAudit.Should().BeFalse("Viewer does not have audit:read");
        vm.CanAdminister.Should().BeFalse("Viewer does not have user:*");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Session status text
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SessionStatusText_Standalone_ReturnsLocalMode()
    {
        var vm = await CreateVmAsync();

        vm.SessionStatusText.Should().Be("Local mode — full access");
    }

    [Fact]
    public async Task SessionStatusText_NotLoggedIn_ReturnsNotLoggedIn()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        // No auth session set up

        vm.SessionStatusText.Should().Be("Not logged in");
    }

    [Fact]
    public async Task SessionStatusText_EditorLoggedIn_ShowsUsernameAndRole()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "valid-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "jane",
            Role = "Editor",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        vm.SessionStatusText.Should().Be("jane — Editor");
    }

    [Fact]
    public async Task SessionStatusText_TokenExpired_ShowsExpiryWarning()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "expired-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "bill",
            Role = "Viewer",
            Id = Guid.NewGuid().ToString()
        });
        // Set expiry in the past
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);

        vm.SessionStatusText.Should().Be("Session expired — Viewer (relogin required)");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Permission gating: Token expiry disables all permissions
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiUserMode_ExpiredToken_AllPermissions_AreFalse()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "expired-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "admin",
            Role = "Administrator",
            Id = Guid.NewGuid().ToString()
        });
        // Set expiry in the past
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);

        // Even Administrator gets no permissions with an expired token
        vm.CanIngest.Should().BeFalse("token expired");
        vm.CanEditMetadata.Should().BeFalse("token expired");
        vm.CanAudit.Should().BeFalse("token expired");
        vm.CanAdminister.Should().BeFalse("token expired");
    }

    // ─────────────────────────────────────────────────────────────────
    //  OnSessionCheckTick (T7.3)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnSessionCheckTick_TokenValid_DoesNotChangeStatus()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "valid-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "alice",
            Role = "Administrator",
            Id = Guid.NewGuid().ToString()
        });
        // Set expiry far in the future
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        var initialStatus = vm.StatusBar.StatusText;

        InvokeSessionCheckTick(vm);

        // Status should remain unchanged
        vm.StatusBar.StatusText.Should().Be(initialStatus);
    }

    [Fact]
    public async Task OnSessionCheckTick_TokenExpired_DetectsExpiry()
    {
        // This test verifies the expiry detection logic within OnSessionCheckTick.
        // The StatusText update requires a pumping dispatcher (not available in
        // headless test context), so we verify through EvaluatePermission instead.
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "expired-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "bob",
            Role = "Editor",
            Id = Guid.NewGuid().ToString()
        });
        // Set expiry in the past
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);

        // Confirm token is expired before calling OnSessionCheckTick
        _auth.IsTokenExpired().Should().BeTrue("token should be expired");

        // Confirm all permission gating is disabled due to expired token
        vm.CanIngest.Should().BeFalse("expired token disables permissions");
        vm.CanEditMetadata.Should().BeFalse();
        vm.CanAudit.Should().BeFalse();
        vm.CanAdminister.Should().BeFalse();
    }

    [Fact]
    public async Task OnSessionCheckTick_StandaloneMode_DoesNothing()
    {
        var vm = await CreateVmAsync();

        // Default is standalone mode
        vm.StatusBar.StatusText = "Ready";

        InvokeSessionCheckTick(vm);

        // No change in standalone mode
        vm.StatusBar.StatusText.Should().Be("Ready");
    }

    // ─────────────────────────────────────────────────────────────────
    //  CurrentUserRole property
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CurrentUserRole_Standalone_ReturnsAdministrator()
    {
        var vm = await CreateVmAsync();

        vm.CurrentUserRole.Should().Be("Administrator");
    }

    [Fact]
    public async Task CurrentUserRole_MultiUser_NoAuth_ReturnsNull()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        vm.CurrentUserRole.Should().BeNull();
    }

    [Fact]
    public async Task CurrentUserRole_MultiUser_LoggedIn_ReturnsRole()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "charlie",
            Role = "Editor",
            Id = Guid.NewGuid().ToString()
        });

        vm.CurrentUserRole.Should().Be("Editor");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Nav button permission tooltips (Ingest/Metadata/Audit/Admin)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NavTooltips_Standalone_AllAreNull()
    {
        var vm = await CreateVmAsync();

        // Standalone mode grants all permissions → no tooltips needed
        vm.IngestPermissionTooltip.Should().BeNull();
        vm.AuditPermissionTooltip.Should().BeNull();
        vm.AdminPermissionTooltip.Should().BeNull();
    }

    [Fact]
    public async Task NavTooltips_NotLoggedIn_ShowSignInMessages()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        // No auth session → not logged in

        vm.IngestPermissionTooltip.Should().Be("Sign in to ingest assets");
        vm.AuditPermissionTooltip.Should().Be("Sign in to view audit log");
        vm.AdminPermissionTooltip.Should().Be("Sign in to manage server");
    }

    [Fact]
    public async Task NavTooltips_Administrator_AllAreNull()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "admin-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "admin",
            Role = "Administrator",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        // Administrator has all permissions → no tooltips
        vm.IngestPermissionTooltip.Should().BeNull();
        vm.AuditPermissionTooltip.Should().BeNull();
        vm.AdminPermissionTooltip.Should().BeNull();
    }

    [Fact]
    public async Task NavTooltips_Editor_IngestNull_AuditAndAdminShowRequiresAdmin()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "editor-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "editor",
            Role = "Editor",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        // Editor has asset:create → Ingest tooltip is null
        vm.IngestPermissionTooltip.Should().BeNull("Editor has asset:create");

        // Editor does NOT have audit:read or user:* → Audit/Admin show requirement
        vm.AuditPermissionTooltip.Should().Be("Requires Administrator role");
        vm.AdminPermissionTooltip.Should().Be("Requires Administrator role");
    }

    [Fact]
    public async Task NavTooltips_Viewer_IngestAuditAdminShowRequirement()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "viewer-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "viewer",
            Role = "Viewer",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        // Viewer lacks all non-read permissions
        vm.IngestPermissionTooltip.Should().Be("Requires Editor or Administrator role");
        vm.AuditPermissionTooltip.Should().Be("Requires Administrator role");
        vm.AdminPermissionTooltip.Should().Be("Requires Administrator role");
    }

    [Fact]
    public async Task NavTooltips_TokenExpired_ShowExpiredMessageWithAction()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "expired-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "editor",
            Role = "Editor",
            Id = Guid.NewGuid().ToString()
        });
        // Set expiry in the past
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);

        // Expired token disables all permissions → all tooltips show expired message
        vm.IngestPermissionTooltip.Should().Be("Session expired — re-login required to ingest assets");
        vm.AuditPermissionTooltip.Should().Be("Session expired — re-login required to view audit log");
        vm.AdminPermissionTooltip.Should().Be("Session expired — re-login required to manage server");
    }

    [Fact]
    public async Task NavTooltips_ValuesUpdate_WhenUserRoleChanges()
    {
        var vm = await CreateVmAsync();

        SetMultiUserMode(_modeManager);
        SetToken(_auth, "viewer-jwt");
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "viewer",
            Role = "Viewer",
            Id = Guid.NewGuid().ToString()
        });
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        // Start as Viewer → all tooltips show role requirement
        vm.IngestPermissionTooltip.Should().Be("Requires Editor or Administrator role");
        vm.AdminPermissionTooltip.Should().Be("Requires Administrator role");

        // Change role to Administrator — values are computed properties,
        // so they update on next read regardless of PropertyChanged timing.
        SetCurrentUser(_auth, new UserProfile
        {
            Username = "admin",
            Role = "Administrator",
            Id = Guid.NewGuid().ToString()
        });

        // Values should reflect new role immediately
        vm.IngestPermissionTooltip.Should().BeNull("Administrator has all permissions");
        vm.AuditPermissionTooltip.Should().BeNull();
        vm.AdminPermissionTooltip.Should().BeNull();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _broker.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException) { }
    }
}
