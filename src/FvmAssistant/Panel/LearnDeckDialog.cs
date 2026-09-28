using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>Shows the cards in the room's deck bar and lets the user name the ones to remember.</summary>
    internal sealed class LearnDeckDialog : Form
    {
        private readonly List<(int slot, ComboBox name)> _rows = new List<(int, ComboBox)>();

        public LearnDeckDialog(StageFrame roomFrame, IReadOnlyList<int> filledSlots, IReadOnlyList<string> knownNames)
        {
            Text = "学习当前卡组";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var layout = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(new Label
            {
                Text = "给要记住的卡片起个名字（之后在面板里按名字选卡），不需要的留空。\n" +
                       "同名会覆盖旧图标；星级不同的同一种卡请起不同的名字。",
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Margin = new Padding(0, 0, 0, 10),
            }, 0, 0);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 0), 3);

            int row = 1;
            foreach (var slot in filledSlots)
            {
                var icon = roomFrame.Crop(DeckBuilder.DeckSlot(slot)).ToBitmap();
                layout.Controls.Add(new PictureBox { Image = icon, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(63, 75), Margin = new Padding(0, 2, 8, 2) }, 0, row);
                layout.Controls.Add(new Label { Text = $"第 {slot} 槽", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 1, row);
                var name = new ComboBox { Width = 180, Anchor = AnchorStyles.Left };
                name.Items.AddRange(knownNames.Cast<object>().ToArray());
                layout.Controls.Add(name, 2, row);
                _rows.Add((slot, name));
                row++;
            }

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, row);
            layout.SetColumnSpan(buttons, 3);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public IReadOnlyList<(int slot, string name)> Named =>
            _rows.Select(r => (r.slot, name: r.name.Text.Trim())).Where(r => r.name.Length > 0).ToList();
    }
}
