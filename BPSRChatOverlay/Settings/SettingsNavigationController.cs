using System.Windows;
using System.Windows.Controls;

namespace BPSRChatOverlay.Settings;

internal sealed record SettingsPageDefinition(
    string Title,
    IReadOnlyList<FrameworkElement> Containers,
    IReadOnlyDictionary<string, FrameworkElement> Sections);

internal sealed class SettingsNavigationController
{
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBlock _pageTitle;
    private readonly IReadOnlyList<FrameworkElement> _allContainers;
    private readonly IReadOnlyDictionary<string, SettingsPageDefinition> _pages;

    public SettingsNavigationController(
        ScrollViewer scrollViewer,
        TextBlock pageTitle,
        IReadOnlyList<FrameworkElement> allContainers,
        IReadOnlyDictionary<string, SettingsPageDefinition> pages)
    {
        _scrollViewer = scrollViewer;
        _pageTitle = pageTitle;
        _allContainers = allContainers;
        _pages = pages;
    }

    public void Navigate(string navigationKey)
    {
        string[] parts = navigationKey.Split(
            ':',
            count: 2,
            StringSplitOptions.TrimEntries);
        if (!_pages.TryGetValue(parts[0], out SettingsPageDefinition? page))
        {
            return;
        }

        foreach (FrameworkElement container in _allContainers)
        {
            container.Visibility = Visibility.Collapsed;
        }

        foreach (FrameworkElement container in page.Containers)
        {
            container.Visibility = Visibility.Visible;
        }

        _pageTitle.Text = page.Title;
        FrameworkElement? section = parts.Length > 1 &&
                                    page.Sections.TryGetValue(
                                        parts[1],
                                        out FrameworkElement? target)
            ? target
            : null;

        _ = _scrollViewer.Dispatcher.BeginInvoke(() =>
        {
            if (section is null)
            {
                _scrollViewer.ScrollToTop();
                return;
            }

            ScrollSectionToTop(section);
        });
    }

    private void ScrollSectionToTop(FrameworkElement section)
    {
        _scrollViewer.UpdateLayout();

        Point sectionPosition = section
            .TransformToAncestor(_scrollViewer)
            .Transform(new Point(0, 0));
        double targetOffset = Math.Clamp(
            _scrollViewer.VerticalOffset + sectionPosition.Y,
            0,
            _scrollViewer.ScrollableHeight);

        _scrollViewer.ScrollToVerticalOffset(targetOffset);
    }
}
