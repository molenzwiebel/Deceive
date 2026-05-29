using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Deceive.Properties;

namespace Deceive;

// Modal window used to configure friend status notifications. A dedicated, searchable, scrollable
// window is used instead of a tray submenu because a roster can hold hundreds of friends.
internal sealed class FriendNotificationsForm : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private const int EM_SETCUEBANNER = 0x1501;

    // Palette (loosely GitHub-inspired) used for the status dots and text.
    private static readonly Color TextColor = Color.FromArgb(36, 41, 47);
    private static readonly Color MutedColor = Color.FromArgb(101, 109, 118);
    private static readonly Color PanelColor = Color.FromArgb(246, 248, 250);
    private static readonly Color BorderColor = Color.FromArgb(208, 215, 222);

    // Each known status and the colour of its dot / text.
    private static readonly (string Status, Color Color)[] StatusPalette =
    {
        ("In Game", Color.FromArgb(219, 109, 40)),
        ("In Champion Select", Color.FromArgb(88, 166, 255)),
        ("In Queue", Color.FromArgb(88, 166, 255)),
        ("In Lobby", Color.FromArgb(58, 150, 221)),
        ("Spectating", Color.FromArgb(163, 113, 247)),
        ("Online", Color.FromArgb(35, 169, 75)),
        ("Away", Color.FromArgb(210, 153, 34)),
        ("Busy", Color.FromArgb(218, 54, 51)),
        ("Mobile", Color.FromArgb(35, 169, 75)),
        ("Offline", Color.FromArgb(110, 118, 129)),
    };

    private sealed class FriendItem
    {
        public string RiotId { get; }
        public string Status { get; }
        public bool IsOffline => string.Equals(Status, "Offline", StringComparison.OrdinalIgnoreCase);

        public FriendItem(string riotId, string status)
        {
            RiotId = riotId;
            Status = status;
        }
    }

    private readonly List<FriendItem> _allFriends;
    private readonly HashSet<string> _tracked;
    private readonly Action<string, bool> _setTracked;
    private readonly Action<bool> _setEnabled;

    private readonly CheckBox _enabledCheckBox;
    private readonly TextBox _searchBox;
    private readonly ListView _listView;
    private readonly ColumnHeader _friendColumn;
    private readonly ColumnHeader _statusColumn;
    private readonly ImageList _statusIcons;
    private readonly Label _countLabel;

    private bool _populating;

    public FriendNotificationsForm(
        IEnumerable<KeyValuePair<string, string>> friends, // "name#tag" -> current status
        HashSet<string> tracked,
        bool enabled,
        Action<bool> setEnabled,
        Action<string, bool> setTracked)
    {
        _allFriends = friends.Select(friend => new FriendItem(friend.Key, friend.Value)).ToList();
        _tracked = tracked;
        _setEnabled = setEnabled;
        _setTracked = setTracked;

        Text = "Friend Notifications";
        try
        {
            Icon = Resources.DeceiveIcon;
        }
        catch
        {
            // ignored; the icon is purely cosmetic
        }

        Font = new Font("Segoe UI", 9.75f);
        BackColor = Color.White;
        ForeColor = TextColor;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = true;
        ClientSize = new Size(468, 568);
        MinimumSize = new Size(380, 380);

        var titleLabel = new Label
        {
            Text = "Friend Notifications",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(18, 16),
            AutoSize = true
        };

        var subtitleLabel = new Label
        {
            Text = "Get a sound and a popup whenever a checked friend's status changes, for example when they finish a game.",
            ForeColor = MutedColor,
            Location = new Point(20, 50),
            Size = new Size(430, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _enabledCheckBox = new CheckBox
        {
            Text = "Enable notifications",
            Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
            Location = new Point(18, 90),
            AutoSize = true,
            Checked = enabled
        };
        _enabledCheckBox.CheckedChanged += (_, _) => ApplyEnabledState(_enabledCheckBox.Checked);

        _searchBox = new TextBox
        {
            Location = new Point(20, 120),
            Size = new Size(428, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Enabled = enabled
        };
        _searchBox.TextChanged += (_, _) => Populate();

        _statusIcons = new ImageList { ImageSize = new Size(12, 12), ColorDepth = ColorDepth.Depth32Bit };
        foreach (var (status, color) in StatusPalette)
            _statusIcons.Images.Add(status, CreateDot(color));

        _friendColumn = new ColumnHeader { Text = "Friend", Width = 300 };
        _statusColumn = new ColumnHeader { Text = "Status", Width = 140 };

        _listView = new ListView
        {
            Location = new Point(20, 156),
            Size = new Size(428, 358),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            SmallImageList = _statusIcons,
            Enabled = enabled
        };
        _listView.Columns.AddRange(new[] { _friendColumn, _statusColumn });
        _listView.ItemChecked += OnItemChecked;
        _listView.Resize += (_, _) => ResizeColumns();

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = PanelColor
        };
        footerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, 0, footerPanel.Width, 0);
        };

        _countLabel = new Label
        {
            ForeColor = MutedColor,
            AutoSize = true
        };

        var closeButton = new Button
        {
            Text = "Close",
            Size = new Size(84, 30),
            DialogResult = DialogResult.OK
        };

        // Position the footer contents whenever the (docked) panel is resized so we don't depend
        // on its width before it has been laid out.
        footerPanel.Resize += (_, _) =>
        {
            closeButton.Location = new Point(footerPanel.ClientSize.Width - closeButton.Width - 20, (footerPanel.ClientSize.Height - closeButton.Height) / 2);
            _countLabel.Location = new Point(20, (footerPanel.ClientSize.Height - _countLabel.Height) / 2);
        };

        footerPanel.Controls.Add(_countLabel);
        footerPanel.Controls.Add(closeButton);

        AcceptButton = closeButton;
        CancelButton = closeButton;

        Controls.Add(_listView);
        Controls.Add(_searchBox);
        Controls.Add(_enabledCheckBox);
        Controls.Add(subtitleLabel);
        Controls.Add(titleLabel);
        Controls.Add(footerPanel);

        TrySetCueBanner();
        ResizeColumns();
        Populate();
    }

    private void ApplyEnabledState(bool enabled)
    {
        _setEnabled(enabled);
        _searchBox.Enabled = enabled;
        _listView.Enabled = enabled;
    }

    private void Populate()
    {
        _populating = true;
        _listView.BeginUpdate();
        _listView.Items.Clear();

        var filter = _searchBox.Text.Trim();
        var visible = _allFriends
            .Where(friend => filter.Length == 0 || friend.RiotId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(friend => _tracked.Contains(friend.RiotId) ? 0 : 1) // tracked friends first
            .ThenBy(friend => friend.IsOffline ? 1 : 0)                  // offline friends last
            .ThenBy(friend => friend.RiotId, StringComparer.OrdinalIgnoreCase);

        foreach (var friend in visible)
        {
            var item = new ListViewItem(friend.RiotId)
            {
                ImageKey = friend.Status,
                Checked = _tracked.Contains(friend.RiotId),
                UseItemStyleForSubItems = false,
                Tag = friend
            };
            var statusSubItem = item.SubItems.Add(friend.Status);
            statusSubItem.ForeColor = StatusColor(friend.Status);
            if (friend.IsOffline)
                item.ForeColor = MutedColor;
            _listView.Items.Add(item);
        }

        _listView.EndUpdate();
        _populating = false;
        UpdateCountLabel();
    }

    private void OnItemChecked(object sender, ItemCheckedEventArgs e)
    {
        if (_populating)
            return;

        var friend = (FriendItem)e.Item.Tag;
        if (e.Item.Checked)
            _tracked.Add(friend.RiotId);
        else
            _tracked.Remove(friend.RiotId);

        _setTracked(friend.RiotId, e.Item.Checked);
        UpdateCountLabel();
    }

    private void ResizeColumns()
    {
        var available = _listView.ClientSize.Width;
        _statusColumn.Width = 140;
        _friendColumn.Width = Math.Max(120, available - _statusColumn.Width - 4);
    }

    private void TrySetCueBanner()
    {
        try
        {
            SendMessage(_searchBox.Handle, EM_SETCUEBANNER, (IntPtr)1, "Search friends...");
        }
        catch
        {
            // cue banner is a nicety; ignore if the platform refuses it
        }
    }

    private void UpdateCountLabel() => _countLabel.Text = _allFriends.Count == 0
        ? "No friends loaded yet, log in first."
        : $"{_tracked.Count} tracked, {_allFriends.Count} friends";

    private static Color StatusColor(string status)
    {
        foreach (var (knownStatus, color) in StatusPalette)
            if (string.Equals(knownStatus, status, StringComparison.OrdinalIgnoreCase))
                return color;
        return MutedColor;
    }

    private static Bitmap CreateDot(Color color)
    {
        var bitmap = new Bitmap(12, 12);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 1, 2, 9, 9);
        return bitmap;
    }
}
