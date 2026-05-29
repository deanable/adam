using Adam.CatalogBrowser.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAssertions;

namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// Headless smoke / brightness tests for the <see cref="DragGhostWindow"/>.
/// Verifies the window construction, visual tree structure, show/hide lifecycle,
/// position tracking, count badge behavior, and reusability.
/// </summary>
public class DragGhostWindowSmokeTests : IDisposable
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(
        () => HeadlessUnitTestSession.StartNew(typeof(TestCatalogBrowserApp)));

    private Task DispatchAsync(Action action)
        => Session.Value.Dispatch(action, CancellationToken.None);

    private Task<T> DispatchAsync<T>(Func<T> func)
        => Session.Value.Dispatch(func, CancellationToken.None);

    // ──────────────────────────────────────────────
    //  Constructor — smoke check
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Constructor_SetsExpectedDefaults()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            ghost.WindowDecorations.Should().Be(WindowDecorations.None);
            ghost.ShowInTaskbar.Should().BeFalse();
            ghost.Topmost.Should().BeTrue();
            ghost.CanResize.Should().BeFalse();
            ghost.Width.Should().Be(80);
            ghost.Height.Should().Be(80);
            ghost.IsHitTestVisible.Should().BeFalse();
            ghost.Background.Should().Be(Brushes.Transparent);
            ghost.IsDragging.Should().BeFalse("not dragging yet");
        });
    }

    // ──────────────────────────────────────────────
    //  Visual tree structure
    // ──────────────────────────────────────────────

    [Fact]
    public async Task VisualTree_HasExpectedStructure()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            // Root content is a Border (preview border)
            var previewBorder = ghost.Content.Should().BeOfType<Border>().Which;
            previewBorder.Width.Should().Be(72);
            previewBorder.Height.Should().Be(72);
            previewBorder.CornerRadius.Should().Be(new CornerRadius(8));
            previewBorder.BorderThickness.Should().Be(new Thickness(1));

            // Preview border child is a Grid (inner grid)
            var innerGrid = previewBorder.Child.Should().BeOfType<Grid>().Which;
            innerGrid.Children.Count.Should().Be(2, "image container + count badge");

            // First child: image container (Border)
            var imageContainer = innerGrid.Children[0].Should().BeOfType<Border>().Which;
            imageContainer.Width.Should().Be(56);
            imageContainer.Height.Should().Be(56);
            imageContainer.CornerRadius.Should().Be(new CornerRadius(4));
            imageContainer.Child.Should().BeOfType<TextBlock>()
                .Which.Text.Should().Be("\U0001F4C1", "emoji placeholder initially");

            // Second child: count badge (TextBlock)
            var countBadge = innerGrid.Children[1].Should().BeOfType<TextBlock>().Which;
            countBadge.FontSize.Should().Be(11);
            countBadge.FontWeight.Should().Be(FontWeight.Bold);
            countBadge.Foreground.Should().Be(Brushes.White);
            // IsVisible is controlled by ShowGhost (hidden when count <= 1)
        });
    }

    // ──────────────────────────────────────────────
    //  ShowGhost — state and positioning
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ShowGhost_SetsIsDraggingAndShowsWindow()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();
            var screenPos = new PixelPoint(500, 300);

            ghost.ShowGhost(screenPos, count: 1);

            ghost.IsDragging.Should().BeTrue("ShowGhost sets IsDragging");

            // Position should be offset by (+16, +16)
            ghost.Position.Should().Be(new PixelPoint(516, 316));
            ghost.IsVisible.Should().BeTrue("window should be shown");

            // Cleanup
            ghost.HideGhost();
        });
    }

    [Fact]
    public async Task ShowGhost_WithMultipleItems_ShowsCountBadge()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            ghost.ShowGhost(new PixelPoint(100, 100), count: 5);

            // Find the count badge text block (second child of inner grid)
            var innerGrid = ((Border)ghost.Content!).Child.Should().BeOfType<Grid>().Which;
            var countBadge = innerGrid.Children[1].Should().BeOfType<TextBlock>().Which;

            countBadge.IsVisible.Should().BeTrue("badge visible for count > 1");
            countBadge.Text.Should().Be("5", "badge shows the item count");

            ghost.HideGhost();
        });
    }

    [Fact]
    public async Task ShowGhost_WithSingleItem_HidesCountBadge()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            ghost.ShowGhost(new PixelPoint(100, 100), count: 1);

            var innerGrid = ((Border)ghost.Content!).Child.Should().BeOfType<Grid>().Which;
            var countBadge = innerGrid.Children[1].Should().BeOfType<TextBlock>().Which;

            countBadge.IsVisible.Should().BeFalse("badge hidden for count = 1");
            countBadge.Text.Should().BeEmpty("badge text empty for count = 1");

            ghost.HideGhost();
        });
    }

    [Fact]
    public async Task ShowGhost_WithoutThumbnail_KeepsPlaceholder()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            // No thumbnail path provided
            ghost.ShowGhost(new PixelPoint(100, 100), count: 1);

            var innerGrid = ((Border)ghost.Content!).Child.Should().BeOfType<Grid>().Which;
            var imageContainer = innerGrid.Children[0].Should().BeOfType<Border>().Which;

            // Container should still have the emoji TextBlock placeholder
            imageContainer.Child.Should().BeOfType<TextBlock>()
                .Which.Text.Should().Be("\U0001F4C1");

            ghost.HideGhost();
        });
    }

    [Fact]
    public async Task ShowGhost_WithMissingThumbnail_KeepsPlaceholder()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            // Non-existent file path — should silently fall back
            ghost.ShowGhost(new PixelPoint(100, 100), count: 1,
                thumbnailPath: "C:\\nonexistent\\thumbnail.png");

            var innerGrid = ((Border)ghost.Content!).Child.Should().BeOfType<Grid>().Which;
            var imageContainer = innerGrid.Children[0].Should().BeOfType<Border>().Which;

            imageContainer.Child.Should().BeOfType<TextBlock>()
                .Which.Text.Should().Be("\U0001F4C1", "placeholder restored after failed load");

            ghost.HideGhost();
        });
    }

    // ──────────────────────────────────────────────
    //  UpdatePosition
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UpdatePosition_MovesWindow()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            ghost.ShowGhost(new PixelPoint(100, 100), count: 1);
            ghost.Position.Should().Be(new PixelPoint(116, 116));

            ghost.UpdatePosition(new PixelPoint(200, 150));
            ghost.Position.Should().Be(new PixelPoint(216, 166), "position offset by (+16, +16)");

            ghost.HideGhost();
        });
    }

    [Fact]
    public async Task UpdatePosition_WhenNotDragging_DoesNotMove()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();
            ghost.ShowGhost(new PixelPoint(100, 100), count: 1);
            ghost.HideGhost();

            // After hide, IsDragging is false — UpdatePosition should be a no-op
            ghost.UpdatePosition(new PixelPoint(999, 999));
            ghost.Position.Should().NotBe(new PixelPoint(1015, 1015),
                "position should not change when not dragging");
        });
    }

    // ──────────────────────────────────────────────
    //  HideGhost
    // ──────────────────────────────────────────────

    [Fact]
    public async Task HideGhost_ClearsStateAndRestoresPlaceholder()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();
            ghost.ShowGhost(new PixelPoint(100, 100), count: 1);

            ghost.HideGhost();

            ghost.IsDragging.Should().BeFalse("HideGhost clears IsDragging");
            ghost.IsVisible.Should().BeFalse("window should be hidden");

            // Placeholder should be restored
            var innerGrid = ((Border)ghost.Content!).Child.Should().BeOfType<Grid>().Which;
            var imageContainer = innerGrid.Children[0].Should().BeOfType<Border>().Which;
            imageContainer.Child.Should().BeOfType<TextBlock>()
                .Which.Text.Should().Be("\U0001F4C1", "emoji placeholder restored");
            var restoredBrush = imageContainer.Background.Should().BeOfType<SolidColorBrush>().Which;
            restoredBrush.Color.A.Should().Be(100, "semi-transparent alpha");
            restoredBrush.Color.R.Should().Be(0);
            restoredBrush.Color.G.Should().Be(0);
            restoredBrush.Color.B.Should().Be(0);
        });
    }

    // ──────────────────────────────────────────────
    //  Reusability — show / hide / show cycle
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ShowHide_Cycle_WorksCorrectly()
    {
        await DispatchAsync(() =>
        {
            var ghost = new DragGhostWindow();

            // First show → hide
            ghost.ShowGhost(new PixelPoint(100, 100), count: 3);
            ghost.IsDragging.Should().BeTrue();
            ghost.IsVisible.Should().BeTrue();

            ghost.HideGhost();
            ghost.IsDragging.Should().BeFalse();
            ghost.IsVisible.Should().BeFalse();

            // Second show → hide (same instance)
            ghost.ShowGhost(new PixelPoint(200, 200), count: 7);

            ghost.IsDragging.Should().BeTrue("re-shown after hide");
            ghost.Position.Should().Be(new PixelPoint(216, 216),
                "new position applied on re-show");
            ghost.IsVisible.Should().BeTrue();

            // Verify badge updated for new count
            var innerGrid = ((Border)ghost.Content!).Child.Should().BeOfType<Grid>().Which;
            var countBadge = innerGrid.Children[1].Should().BeOfType<TextBlock>().Which;
            countBadge.Text.Should().Be("7", "badge updated to new count");
            countBadge.IsVisible.Should().BeTrue();

            ghost.HideGhost();
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
