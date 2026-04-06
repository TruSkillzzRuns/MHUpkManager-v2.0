namespace MHUpkManager.UI;

internal static class FixedDetailsSplitLayout
{
    public static void Apply(SplitContainer splitContainer, int detailsPanelWidth, int detailsPanelMinSize = 220, int contentPanelMinSize = 250)
    {
        if (splitContainer.Width <= 0)
            return;

        splitContainer.FixedPanel = FixedPanel.Panel2;

        int target = splitContainer.Width - detailsPanelWidth - splitContainer.SplitterWidth;
        int min = Math.Max(contentPanelMinSize, splitContainer.Panel1MinSize);
        int max = Math.Max(min, splitContainer.Width - detailsPanelMinSize - splitContainer.SplitterWidth);
        splitContainer.SplitterDistance = Math.Clamp(target, min, max);
        splitContainer.Panel2MinSize = detailsPanelMinSize;
    }
}
