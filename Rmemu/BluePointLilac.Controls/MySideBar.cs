using BluePointLilac.Methods;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public sealed class MySideBar : Panel
    {
        public MySideBar()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            TabStop = true;
            Dock = DockStyle.Left;
            ItemHeight = 36.DpiZoom();
            ApplyTheme();
        }

        private string[] itemNames;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string[] ItemNames
        {
            get => itemNames;
            set
            {
                itemNames = value;
                if(value != null && !IsFixedWidth)
                {
                    int maxWidth = 0;
                    Array.ForEach(value, str => maxWidth = Math.Max(maxWidth, GetItemWidth(str)));
                    Width = maxWidth;
                }
                SelectedIndex = -1;
                HoveredIndex = -1;
                Invalidate();
            }
        }
        private int itemHeight;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ItemHeight
        {
            get => itemHeight;
            set { itemHeight = Math.Max(1, value); Invalidate(); }
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int TopSpace { get; set; } = 8.DpiZoom();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int HorizontalSpace { get; set; } = 20.DpiZoom();
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsFixedWidth { get; set; } = true;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color SeparatorColor { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color SelectedBackColor { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color HoveredBackColor { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color SelectedForeColor { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color HoveredForeColor { get; set; }
        public int GetItemWidth(string str) => TextRenderer.MeasureText(str, Font).Width + 2 * HorizontalSpace;

        internal void ApplyTheme()
        {
            BackColor = FluentTheme.Background;
            ForeColor = FluentTheme.Foreground;
            SeparatorColor = FluentTheme.Border;
            SelectedBackColor = HoveredBackColor = FluentTheme.Hover;
            SelectedForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : FluentTheme.Foreground;
            HoveredForeColor = SelectedForeColor;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(itemNames == null) return;
            for(int i = 0; i < itemNames.Length; i++)
            {
                Rectangle row = new Rectangle(6.DpiZoom(), TopSpace + i * ItemHeight, Math.Max(0, Width - 12.DpiZoom()), ItemHeight - 2.DpiZoom());
                if(row.Bottom < e.ClipRectangle.Top || row.Top > e.ClipRectangle.Bottom) continue;
                if(string.IsNullOrEmpty(itemNames[i]))
                {
                    using(Pen pen = new Pen(SeparatorColor))
                        e.Graphics.DrawLine(pen, HorizontalSpace, row.Top + row.Height / 2, Width - HorizontalSpace, row.Top + row.Height / 2);
                    continue;
                }
                bool selected = i == SelectedIndex;
                bool hovered = i == HoveredIndex;
                if(selected || hovered)
                {
                    using(Brush brush = new SolidBrush(selected ? SelectedBackColor : HoveredBackColor))
                        e.Graphics.FillRectangle(brush, row);
                }
                if(selected)
                {
                    using(Brush brush = new SolidBrush(SystemInformation.HighContrast ? SystemColors.HighlightText : FluentTheme.Accent))
                        e.Graphics.FillRectangle(brush, row.Left, row.Top + 8.DpiZoom(), 3.DpiZoom(), Math.Max(1, row.Height - 16.DpiZoom()));
                }
                Rectangle textBounds = new Rectangle(HorizontalSpace, row.Top, Math.Max(0, Width - HorizontalSpace * 2), row.Height);
                TextRenderer.DrawText(e.Graphics, itemNames[i], Font, textBounds,
                    selected ? SelectedForeColor : hovered ? HoveredForeColor : ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if(selected && Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(row, -2, -2), ForeColor, BackColor);
            }
            using(Pen pen = new Pen(SeparatorColor)) e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }
        private int HitIndex(int y)
        {
            if(itemNames == null || y < TopSpace) return -1;
            int index = (y - TopSpace) / ItemHeight;
            return index < itemNames.Length && !string.IsNullOrEmpty(itemNames[index]) ? index : -1;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            HoveredIndex = HitIndex(e.Y);
            Cursor = HoveredIndex < 0 ? Cursors.Default : Cursors.Hand;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button != MouseButtons.Left) return;
            Focus();
            int index = HitIndex(e.Y);
            if(index >= 0) SelectedIndex = index;
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); HoveredIndex = -1; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(itemNames != null && itemNames.Length > 0 && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Home || e.KeyCode == Keys.End))
            {
                int direction = e.KeyCode == Keys.Up || e.KeyCode == Keys.End ? -1 : 1;
                int index = e.KeyCode == Keys.Home ? -1 : e.KeyCode == Keys.End ? itemNames.Length : SelectedIndex;
                if(index < 0 && direction < 0) index = itemNames.Length;
                for(index += direction; index >= 0 && index < itemNames.Length; index += direction)
                    if(!string.IsNullOrEmpty(itemNames[index])) { SelectedIndex = index; break; }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        public event EventHandler SelectIndexChanged;
        public event EventHandler HoverIndexChanged;
        private int selectIndex = -1;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => selectIndex;
            set
            {
                if(value < -1 || value >= (itemNames?.Length ?? 0) || selectIndex == value) return;
                selectIndex = value;
                HoveredIndex = value;
                AccessibleName = value >= 0 ? itemNames[value] : null;
                Invalidate();
                SelectIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        private int hoverIndex = -1;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int HoveredIndex
        {
            get => hoverIndex;
            set
            {
                if(hoverIndex == value) return;
                hoverIndex = value;
                Invalidate();
                HoverIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
