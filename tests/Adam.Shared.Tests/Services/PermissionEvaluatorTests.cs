using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for <see cref="PermissionEvaluator"/> — the client-side role-based permission checker.
/// </summary>
public sealed class PermissionEvaluatorTests
{
    // ─── Administrator ───────────────────────────────────────────────

    [Fact]
    public void Administrator_HasAssetRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "asset:read").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasAssetCreate_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "asset:create").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasAssetUpdate_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "asset:update").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasAssetDelete_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "asset:delete").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasCollectionRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "collection:read").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasUserRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "user:read").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasUserCreate_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "user:create").Should().BeTrue();
    }

    [Fact]
    public void Administrator_HasAuditRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "audit:read").Should().BeTrue();
    }

    [Fact]
    public void Administrator_AnyUnknownPermission_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Administrator", "some:unknown:permission").Should().BeTrue();
    }

    // ─── Editor ──────────────────────────────────────────────────────

    [Fact]
    public void Editor_HasAssetRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Editor", "asset:read").Should().BeTrue();
    }

    [Fact]
    public void Editor_HasAssetCreate_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Editor", "asset:create").Should().BeTrue();
    }

    [Fact]
    public void Editor_HasAssetUpdate_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Editor", "asset:update").Should().BeTrue();
    }

    [Fact]
    public void Editor_HasCollectionRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Editor", "collection:read").Should().BeTrue();
    }

    [Fact]
    public void Editor_HasCollectionUpdate_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Editor", "collection:update").Should().BeTrue();
    }

    [Fact]
    public void Editor_DoesNotHaveAssetDelete_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Editor", "asset:delete").Should().BeFalse();
    }

    [Fact]
    public void Editor_DoesNotHaveUserRead_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Editor", "user:read").Should().BeFalse();
    }

    [Fact]
    public void Editor_DoesNotHaveAuditRead_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Editor", "audit:read").Should().BeFalse();
    }

    // ─── Viewer ──────────────────────────────────────────────────────

    [Fact]
    public void Viewer_HasAssetRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Viewer", "asset:read").Should().BeTrue();
    }

    [Fact]
    public void Viewer_HasCollectionRead_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("Viewer", "collection:read").Should().BeTrue();
    }

    [Fact]
    public void Viewer_DoesNotHaveAssetCreate_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Viewer", "asset:create").Should().BeFalse();
    }

    [Fact]
    public void Viewer_DoesNotHaveAssetUpdate_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Viewer", "asset:update").Should().BeFalse();
    }

    [Fact]
    public void Viewer_DoesNotHaveAssetDelete_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Viewer", "asset:delete").Should().BeFalse();
    }

    [Fact]
    public void Viewer_DoesNotHaveCollectionUpdate_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Viewer", "collection:update").Should().BeFalse();
    }

    [Fact]
    public void Viewer_DoesNotHaveUserRead_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Viewer", "user:read").Should().BeFalse();
    }

    [Fact]
    public void Viewer_DoesNotHaveAuditRead_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Viewer", "audit:read").Should().BeFalse();
    }

    // ─── Edge Cases ──────────────────────────────────────────────────

    [Fact]
    public void UnknownRole_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("UnknownRole", "asset:read").Should().BeFalse();
    }

    [Fact]
    public void EmptyRoleName_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("", "asset:read").Should().BeFalse();
    }

    [Fact]
    public void NullRoleName_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission(null!, "asset:read").Should().BeFalse();
    }

    [Fact]
    public void EmptyRequiredPermission_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Administrator", "").Should().BeFalse();
    }

    [Fact]
    public void NullRequiredPermission_ReturnsFalse()
    {
        PermissionEvaluator.HasPermission("Administrator", null!).Should().BeFalse();
    }

    [Fact]
    public void RoleNameIsCaseInsensitive_ReturnsTrue()
    {
        PermissionEvaluator.HasPermission("administrator", "asset:read").Should().BeTrue();
        PermissionEvaluator.HasPermission("ADMINISTRATOR", "asset:read").Should().BeTrue();
        PermissionEvaluator.HasPermission("editor", "asset:read").Should().BeTrue();
        PermissionEvaluator.HasPermission("viewer", "asset:read").Should().BeTrue();
    }

    [Fact]
    public void GetPermissions_Administrator_ReturnsFivePermissions()
    {
        var perms = PermissionEvaluator.GetPermissions("Administrator");
        perms.Should().HaveCount(5);
        perms.Should().Contain(["asset:*", "collection:*", "user:*", "role:*", "audit:read"]);
    }

    [Fact]
    public void GetPermissions_Editor_ReturnsFivePermissions()
    {
        var perms = PermissionEvaluator.GetPermissions("Editor");
        perms.Should().HaveCount(5);
        perms.Should().Contain(["asset:read", "asset:create", "asset:update", "collection:read", "collection:update"]);
    }

    [Fact]
    public void GetPermissions_Viewer_ReturnsTwoPermissions()
    {
        var perms = PermissionEvaluator.GetPermissions("Viewer");
        perms.Should().HaveCount(2);
        perms.Should().Contain(["asset:read", "collection:read"]);
    }

    [Fact]
    public void GetPermissions_UnknownRole_ReturnsEmptyArray()
    {
        var perms = PermissionEvaluator.GetPermissions("Bogus");
        perms.Should().BeEmpty();
    }

    [Fact]
    public void KnownRoles_ContainsAllThreeRoles()
    {
        PermissionEvaluator.KnownRoles.Should().HaveCount(3);
        PermissionEvaluator.KnownRoles.Should().Contain(["Viewer", "Editor", "Administrator"]);
    }
}
