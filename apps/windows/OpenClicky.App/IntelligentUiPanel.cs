using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>Native, fixed-component rendering for a validated rich assistant response.</summary>
internal sealed class IntelligentUiPanel : UserControl
{
    private readonly Func<string, Task>? _submitFollowUp;
    private readonly FlowLayoutPanel _content = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(14),
        BackColor = Color.White,
    };

    public IntelligentUiPanel(Func<string, Task>? submitFollowUp = null)
    {
        _submitFollowUp = submitFollowUp;
        AccessibleName = "Interactive assistant answer";
        BackColor = Color.White;
        MinimumSize = new Size(260, 96);
        Controls.Add(_content);
        Visible = false;
        _content.ClientSizeChanged += (_, _) => FitRows();
    }

    public void ShowDocument(IntelligentUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _content.SuspendLayout();
        DisposeRows();

        AddRow(AppTheme.Label(document.Title, 16, true));
        if (document.Summary.Length > 0) AddRow(AppTheme.Label(document.Summary, 10));
        if (document.Cards.Count > 0) AddCards(document.Cards);
        if (document.Nodes.Count > 0) AddFlow(document.Nodes, document.Edges);
        if (document.Actions.Count > 0) AddActions(document.Actions);

        _content.ResumeLayout(true);
        Visible = true;
        FitRows();
        _content.AutoScrollPosition = Point.Empty;
    }

    public void ClearDocument()
    {
        DisposeRows();
        Visible = false;
    }

    private void AddCards(IReadOnlyList<IntelligentUiCard> cards)
    {
        AddRow(AppTheme.Label("At a glance", 10, true));
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.White,
        };
        foreach (var card in cards)
        {
            var surface = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(180, 0),
                ColumnCount = 1,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 10, 10),
                BackColor = card.Tone switch
                {
                    "good" => Color.FromArgb(232, 245, 235),
                    "warning" => Color.FromArgb(255, 244, 221),
                    _ => AppTheme.Background,
                },
                AccessibleName = $"{card.Title}: {card.Value}. {card.Detail}",
            };
            surface.Controls.Add(CardLabel(card.Title, 9, AppTheme.Muted));
            surface.Controls.Add(CardLabel(card.Value, 15, AppTheme.Ink, true));
            if (card.Detail.Length > 0) surface.Controls.Add(CardLabel(card.Detail, 8, AppTheme.Muted));
            row.Controls.Add(surface);
        }
        AddRow(row);
    }

    private void AddFlow(IReadOnlyList<IntelligentUiNode> nodes, IReadOnlyList<IntelligentUiEdge> edges)
    {
        AddRow(AppTheme.Label("Flow", 10, true));
        var detail = AppTheme.Label("Select a step to see its detail.", 9);
        detail.AutoSize = false;
        detail.Height = 42;
        detail.AccessibleName = "Selected flow step detail";

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Color.White,
        };
        var buttons = new List<Button>();
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var button = AppTheme.Button($"{StatusMark(node.Status)}  {node.Title}");
            button.AccessibleDescription = node.Detail;
            button.Tag = node;
            button.Click += (_, _) =>
            {
                foreach (var other in buttons) other.BackColor = Color.White;
                button.BackColor = AppTheme.Tint;
                detail.Text = node.Detail.Length == 0 ? node.Title : node.Detail;
            };
            buttons.Add(button);
            row.Controls.Add(button);

            if (index < nodes.Count - 1)
            {
                var edge = edges.FirstOrDefault(item => item.From == node.Id && item.To == nodes[index + 1].Id);
                var arrow = CardLabel(edge is null || edge.Label.Length == 0 ? "→" : $"→  {edge.Label}  →", 9, AppTheme.Muted);
                arrow.Margin = new Padding(5, 11, 8, 0);
                row.Controls.Add(arrow);
            }
        }
        AddRow(row);

        if (edges.Count > 0)
        {
            var names = nodes.ToDictionary(node => node.Id, node => node.Title, StringComparer.Ordinal);
            AddRow(AppTheme.Label("Connections: " + string.Join("  ·  ", edges.Select(edge =>
                $"{names[edge.From]} → {names[edge.To]}{(edge.Label.Length == 0 ? "" : $" ({edge.Label})")}")), 8));
        }
        AddRow(detail);
    }

    private void AddActions(IReadOnlyList<IntelligentUiAction> actions)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 4, 0, 0),
            BackColor = Color.White,
        };
        foreach (var action in actions)
        {
            var button = AppTheme.Button(action.Label);
            button.Enabled = _submitFollowUp is not null;
            button.Click += async (_, _) =>
            {
                if (_submitFollowUp is null) return;
                button.Enabled = false;
                try { await _submitFollowUp(action.Prompt); }
                catch (Exception ex)
                {
                    if (!button.IsDisposed) MessageBox.Show(FindForm(), ex.Message, "Could not prepare follow-up");
                }
                finally { if (!button.IsDisposed) button.Enabled = true; }
            };
            row.Controls.Add(button);
        }
        AddRow(row);
    }

    private void AddRow(Control control)
    {
        control.Margin = new Padding(0, control.Margin.Top, 0, control.Margin.Bottom);
        _content.Controls.Add(control);
    }

    private void DisposeRows()
    {
        while (_content.Controls.Count > 0) _content.Controls[0].Dispose();
        _content.AutoScrollMinSize = Size.Empty;
    }

    private void FitRows()
    {
        var width = Math.Max(180, _content.ClientSize.Width - _content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        _content.SuspendLayout();
        foreach (Control control in _content.Controls)
        {
            if (control is FlowLayoutPanel row)
            {
                row.MinimumSize = Size.Empty;
                row.MaximumSize = new Size(width, 0);
                row.MinimumSize = new Size(width, 0);
                row.Width = width;
                row.PerformLayout();
            }
            else if (control is Label { AutoSize: true } label)
            {
                label.MaximumSize = new Size(width, 0);
            }
            else control.Width = width;
        }
        _content.ResumeLayout(true);
        _content.PerformLayout();
        _content.AutoScrollMinSize = new Size(0,
            _content.Padding.Vertical + _content.Controls.Cast<Control>().Sum(control => control.Height + control.Margin.Vertical));
    }

    private static Label CardLabel(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(154, 0),
        Margin = new Padding(0, 0, 0, 5),
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color,
    };

    private static string StatusMark(string status) => status switch
    {
        "done" => "✓",
        "current" => "●",
        "blocked" => "!",
        _ => "○",
    };
}
