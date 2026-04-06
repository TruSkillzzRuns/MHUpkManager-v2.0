using System.Text.RegularExpressions;

namespace MHUpkManager.UI;

internal static class WorkspaceUiStyle
{
    public const int ButtonHeight = 46;
    public const int SectionHeaderHeight = 42;
    public const int StandardInset = 8;
    private static readonly Color[] WorkflowStepColors =
    [
        Color.FromArgb(0, 102, 204),
        Color.FromArgb(0, 140, 95),
        Color.FromArgb(198, 112, 0),
        Color.FromArgb(173, 58, 84),
        Color.FromArgb(118, 83, 196),
        Color.FromArgb(0, 128, 128),
        Color.FromArgb(140, 87, 0),
        Color.FromArgb(96, 96, 96)
    ];

    public static Button CreateActionButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            UseVisualStyleBackColor = true,
            MinimumSize = new Size(0, ButtonHeight)
        };
    }

    public static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Height = SectionHeaderHeight,
            Dock = DockStyle.Top,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            Padding = new Padding(0, 6, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    public static Control CreateWorkflowSectionHeader(int step, string text)
    {
        Color stepColor = GetWorkflowStepColor(step);
        Panel panel = new()
        {
            Dock = DockStyle.Top,
            Height = SectionHeaderHeight
        };

        Label badgeLabel = new()
        {
            Text = step.ToString(),
            Dock = DockStyle.Left,
            Width = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            ForeColor = stepColor,
            Tag = "workflow-step-number"
        };

        Label textLabel = CreateSectionLabel(text);
        textLabel.Dock = DockStyle.Fill;

        panel.Controls.Add(textLabel);
        panel.Controls.Add(badgeLabel);
        return panel;
    }

    public static Label CreateValueLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = text
        };
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 44);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 28);
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    public static RichTextBox CreateReadOnlyDetailsTextBox(string text)
    {
        RichTextBox box = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            DetectUrls = false,
            Text = text
        };
        box.TextChanged += (_, _) => FormatWorkflowDetails(box);
        FormatWorkflowDetails(box);
        return box;
    }

    public static void RefreshWorkflowDetailsColors(RichTextBox box)
    {
        FormatWorkflowDetails(box);
    }

    public static string BuildWorkflowText(string title, params string[] steps)
    {
        List<string> lines = [title, string.Empty];
        for (int i = 0; i < steps.Length; i++)
            lines.Add($"{i + 1}. {steps[i]}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildSelectionText(IEnumerable<string> details, string nextAction)
    {
        List<string> lines = ["Current Selection"];
        lines.AddRange(details);
        lines.Add(string.Empty);
        lines.Add("Next");
        lines.Add(nextAction);
        return string.Join(Environment.NewLine, lines);
    }

    private static void FormatWorkflowDetails(RichTextBox box)
    {
        int selectionStart = box.SelectionStart;
        int selectionLength = box.SelectionLength;
        box.SuspendLayout();
        try
        {
            box.SelectAll();
            box.SelectionColor = box.ForeColor;
            box.SelectionFont = SystemFonts.MessageBoxFont;

            MatchCollection matches = Regex.Matches(box.Text, @"(?m)^(\d+)\.");
            foreach (Match match in matches)
            {
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int step))
                    continue;

                box.Select(match.Groups[1].Index, match.Groups[1].Length);
                box.SelectionColor = GetWorkflowStepColor(step);
                box.SelectionFont = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            }
        }
        finally
        {
            box.Select(selectionStart, selectionLength);
            box.SelectionColor = box.ForeColor;
            box.ResumeLayout();
        }
    }

    private static Color GetWorkflowStepColor(int step)
    {
        int index = Math.Max(1, step) - 1;
        if (index >= WorkflowStepColors.Length)
            index %= WorkflowStepColors.Length;

        return WorkflowStepColors[index];
    }
}
