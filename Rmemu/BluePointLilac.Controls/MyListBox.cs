using BluePointLilac.Methods;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public class MyListBox : Panel
    {
        public MyListBox()
        {
            AutoScroll = true;
            BackColor = FluentTheme.Background;
            Font = FluentTheme.BodyFont;
            DoubleBuffered = true;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(new MouseEventArgs(e.Button, e.Clicks, e.X, e.Y, Math.Sign(e.Delta) * 50.DpiZoom()));
        }
    }

    public class MyList : FlowLayoutPanel
    {
        private MyListBox resizeOwner;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MyListBox Owner
        {
            get => Parent as MyListBox;
            set => Parent = value;
        }
        public MyList(MyListBox owner) : this() { Owner = owner; }
        public MyList()
        {
            AutoSize = true;
            WrapContents = false;
            FlowDirection = FlowDirection.TopDown;
            Dock = DockStyle.Top;
            DoubleBuffered = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = FluentTheme.Background;
            Font = FluentTheme.BodyFont;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            if(resizeOwner != null) resizeOwner.Resize -= OnOwnerResize;
            base.OnParentChanged(e);
            resizeOwner = Owner;
            if(resizeOwner != null) resizeOwner.Resize += OnOwnerResize;
            ResizeItems();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing && resizeOwner != null)
            {
                resizeOwner.Resize -= OnOwnerResize;
                resizeOwner = null;
            }
            base.Dispose(disposing);
        }
        private void OnOwnerResize(object sender, EventArgs e) => ResizeItems();
        private void ResizeItems()
        {
            if(Owner == null) return;
            foreach(Control item in Controls)
                item.Width = Math.Max(0, Owner.ClientSize.Width - Owner.Padding.Horizontal - Padding.Horizontal - item.Margin.Horizontal);
        }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if(e.Control is MyListItem item) item.MouseEnter += OnItemMouseEnter;
            ResizeItems();
        }
        protected override void OnControlRemoved(ControlEventArgs e)
        {
            if(e.Control is MyListItem item) item.MouseEnter -= OnItemMouseEnter;
            if(hoveredItem == e.Control) HoveredItem = null;
            base.OnControlRemoved(e);
        }
        private void OnItemMouseEnter(object sender, EventArgs e) => HoveredItem = sender as MyListItem;
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            foreach(Control item in Controls) item.ContextMenuStrip?.Close();
            base.OnMouseWheel(e);
        }

        private MyListItem hoveredItem;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MyListItem HoveredItem
        {
            get => hoveredItem;
            set
            {
                if(hoveredItem == value) return;
                MyListItem previous = hoveredItem;
                hoveredItem = value;
                previous?.ApplyTheme();
                hoveredItem?.ApplyTheme();
                HoveredItemChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler HoveredItemChanged;

        public void AddItem(MyListItem item)
        {
            if(item == null) return;
            Controls.Add(item);
        }
        public void AddItems(MyListItem[] items) => Array.ForEach(items, AddItem);
        public void AddItems(List<MyListItem> items) => items.ForEach(AddItem);
        public void SetItemIndex(MyListItem item, int newIndex) => Controls.SetChildIndex(item, newIndex);
        public int GetItemIndex(MyListItem item) => Controls.GetChildIndex(item);
        public void InsertItem(MyListItem item, int index)
        {
            if(item == null) return;
            AddItem(item);
            SetItemIndex(item, index);
        }
        public virtual void ClearItems()
        {
            SuspendLayout();
            try
            {
                HoveredItem = null;
                while(Controls.Count > 0)
                {
                    Control item = Controls[Controls.Count - 1];
                    Controls.Remove(item);
                    item.Dispose();
                }
            }
            finally { ResumeLayout(); }
        }
        public void SortItemByText()
        {
            List<MyListItem> items = new List<MyListItem>();
            foreach(MyListItem item in Controls) items.Add(item);
            items.Sort(new TextComparer());
            SuspendLayout();
            for(int i = 0; i < items.Count; i++) Controls.SetChildIndex(items[i], i);
            ResumeLayout();
        }
        public class TextComparer : IComparer<MyListItem>
        {
            public int Compare(MyListItem x, MyListItem y)
                => StringComparer.CurrentCultureIgnoreCase.Compare(x?.Text, y?.Text);
        }
    }

    public class MyListItem : Panel
    {
        public MyListItem()
        {
            SuspendLayout();
            Font = FluentTheme.BodyFont;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            HasImage = true;
            DoubleBuffered = true;
            Height = 52.DpiZoom();
            Margin = new Padding(0, 0, 0, 1).DpiZoom();
            ForeColor = FluentTheme.Foreground;
            BackColor = FluentTheme.Surface;
            Controls.AddRange(new Control[] { lblSeparator, flpControls, lblText, picImage });
            flpControls.MouseClick += (sender, e) => OnMouseClick(e);
            flpControls.MouseEnter += (sender, e) => OnMouseEnter(e);
            flpControls.MouseLeave += (sender, e) => OnMouseLeave(e);
            flpControls.MouseDown += (sender, e) => OnMouseDown(e);
            flpControls.ControlAdded += (sender, e) => PerformLayout();
            flpControls.ControlRemoved += (sender, e) => PerformLayout();
            lblSeparator.SetEnabled(false);
            lblText.MouseDown += (sender, e) => OnMouseDown(e);
            lblText.MouseClick += (sender, e) => OnMouseClick(e);
            lblText.DoubleClick += (sender, e) => OnDoubleClick(e);
            lblText.MouseEnter += (sender, e) => OnMouseEnter(e);
            lblText.MouseLeave += (sender, e) => OnMouseLeave(e);
            AddCtr(pnlScrollbar, 0);
            ResumeLayout();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image Image
        {
            get => picImage.Image;
            set { picImage.Image = value; PerformLayout(); }
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public override string Text
        {
            get => base.Text;
            set
            {
                if(lblText != null) lblText.Text = value;
                AccessibleName = value;
                base.Text = value;
                PerformLayout();
            }
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new Font Font
        {
            get => lblText.Font;
            set { base.Font = value; lblText.Font = value; PerformLayout(); }
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new Color ForeColor
        {
            get => lblText.ForeColor;
            set { base.ForeColor = value; lblText.ForeColor = value; }
        }
        private bool hasImage;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool HasImage
        {
            get => hasImage;
            set { hasImage = value; picImage.Visible = value; PerformLayout(); }
        }
        private bool wrapText;
        private bool arranging;
        private bool actionsStacked;
        private int unstackedHeight;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool WrapText
        {
            get => wrapText;
            set { wrapText = value; lblText.AutoEllipsis = !value; PerformLayout(); }
        }
        private readonly Label lblText = new Label
        {
            AutoEllipsis = true,
            UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Name = "Text"
        };
        private readonly PictureBox picImage = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(24, 24).DpiZoom(),
            Enabled = false,
            Name = "Image"
        };
        private readonly FlowLayoutPanel flpControls = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Name = "Controls",
            TabStop = false
        };
        private readonly Label lblSeparator = new Label
        {
            BackColor = FluentTheme.Border,
            Dock = DockStyle.Bottom,
            Name = "Separator",
            Height = 1,
            Visible = false
        };
        private readonly Panel pnlScrollbar = new Panel
        {
            Width = 8.DpiZoom(),
            Height = 1,
            Enabled = false
        };

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if(arranging || flpControls == null || lblText == null || picImage == null) return;
            arranging = true;
            try
            {
                int inset = 16.DpiZoom();
                int gap = 12.DpiZoom();
                int actionWidth = 0;
                int actionHeight = 0;
                foreach(Control action in flpControls.Controls)
                {
                    if(!action.Visible && Visible) continue;
                    actionWidth += action.Width + action.Margin.Horizontal;
                    actionHeight = Math.Max(actionHeight, action.Height);
                }
                bool showImage = HasImage && Image != null;
                int contentLeft = inset + Padding.Left;
                int textLeft = showImage ? contentLeft + picImage.Width + gap : contentLeft;
                bool stacked = actionWidth > 0 && ClientSize.Width - actionWidth - textLeft - gap < 180.DpiZoom();
                int textWidth = Math.Max(1, ClientSize.Width - textLeft - Padding.Right - (stacked ? inset : actionWidth + gap));
                int textHeight = WrapText
                    ? TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height
                    : Font.Height;
                int contentHeight = Math.Max(showImage ? picImage.Height : 0, textHeight);
                if(!stacked) contentHeight = Math.Max(contentHeight, actionHeight);
                int requiredHeight = Math.Max(52.DpiZoom(), contentHeight + 24.DpiZoom()
                    + (stacked ? actionHeight + gap : 0));
                if(!WrapText && stacked && !actionsStacked) unstackedHeight = Height;
                if(WrapText || stacked) Height = requiredHeight;
                else if(actionsStacked) Height = unstackedHeight;
                actionsStacked = stacked;
                int mainHeight = stacked ? Height - actionHeight - gap : Height;
                picImage.Visible = showImage;
                picImage.Location = new Point(contentLeft, Math.Max(0, (mainHeight - picImage.Height) / 2));
                lblText.SetBounds(textLeft, 8.DpiZoom(), textWidth, Math.Max(1, mainHeight - 16.DpiZoom()));
                flpControls.SetBounds(Math.Max(0, ClientSize.Width - actionWidth), stacked ? mainHeight : 0,
                    actionWidth, stacked ? actionHeight : Height);
                foreach(Control action in flpControls.Controls)
                {
                    int top = Math.Max(0, (flpControls.Height - action.Height) / 2);
                    Padding margin = new Padding(0, top, action.Margin.Right, 0);
                    if(action.Margin != margin) action.Margin = margin;
                }
                base.OnLayout(levent);
            }
            finally { arranging = false; }
        }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if(lblText != null) { lblText.Font = base.Font; PerformLayout(); }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            OnMouseEnter(EventArgs.Empty);
            if(e.Button == MouseButtons.Left) Focus();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Apps || e.Shift && e.KeyCode == Keys.F10)
            {
                ContextMenuStrip?.Show(this, new Point(18.DpiZoom(), Height));
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if(!ClientRectangle.Contains(PointToClient(Cursor.Position)) && Parent is MyList list && list.HoveredItem == this)
                list.HoveredItem = null;
        }
        internal void ApplyTheme()
        {
            bool selected = (Parent as MyList)?.HoveredItem == this;
            BackColor = selected ? FluentTheme.Hover : FluentTheme.Surface;
            ForeColor = !Enabled ? FluentTheme.Muted : selected && SystemInformation.HighContrast ? SystemColors.HighlightText : FluentTheme.Foreground;
            lblText.BackColor = BackColor;
            picImage.BackColor = BackColor;
            flpControls.BackColor = BackColor;
            pnlScrollbar.BackColor = BackColor;
            lblSeparator.BackColor = FluentTheme.Border;
            foreach(Control action in flpControls.Controls) action.BackColor = BackColor;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using(Pen pen = new Pen(FluentTheme.Border))
                e.Graphics.DrawLine(pen, 16.DpiZoom(), bounds.Bottom, bounds.Right - 16.DpiZoom(), bounds.Bottom);
            if(Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3), FluentTheme.Foreground, BackColor);
        }
        public void AddCtr(Control ctr) => AddCtr(ctr, 12.DpiZoom());
        public void AddCtr(Control ctr, int space)
        {
            ctr.Margin = new Padding(0, 0, space, 0);
            ctr.MouseEnter += OnActionMouseEnter;
            ctr.MouseDown += OnActionMouseDown;
            ctr.MouseLeave += OnActionMouseLeave;
            ctr.SizeChanged += OnActionSizeChanged;
            ctr.VisibleChanged += OnActionSizeChanged;
            flpControls.Controls.Add(ctr);
            if(string.IsNullOrEmpty(ctr.AccessibleName) && ctr is MyCheckBox) ctr.AccessibleName = Text;
            PerformLayout();
        }
        private void OnActionMouseEnter(object sender, EventArgs e) => OnMouseEnter(e);
        private void OnActionMouseDown(object sender, MouseEventArgs e) => OnMouseEnter(e);
        private void OnActionMouseLeave(object sender, EventArgs e) => OnMouseLeave(e);
        private void OnActionSizeChanged(object sender, EventArgs e) => PerformLayout();
        public void AddCtrs(Control[] ctrs) => Array.ForEach(ctrs, AddCtr);
        public void RemoveCtrAt(int index)
        {
            if(index < 0 || index + 1 >= flpControls.Controls.Count) return;
            Control control = flpControls.Controls[index + 1];
            control.MouseEnter -= OnActionMouseEnter;
            control.MouseDown -= OnActionMouseDown;
            control.MouseLeave -= OnActionMouseLeave;
            control.SizeChanged -= OnActionSizeChanged;
            control.VisibleChanged -= OnActionSizeChanged;
            flpControls.Controls.Remove(control);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                ContextMenuStrip menu = ContextMenuStrip;
                ContextMenuStrip = null;
                menu?.Dispose();
            }
            base.Dispose(disposing);
        }
        public int GetCtrIndex(Control ctr) => flpControls.Controls.GetChildIndex(ctr, true) - 1;
        public void SetCtrIndex(Control ctr, int newIndex) => flpControls.Controls.SetChildIndex(ctr, newIndex + 1);
    }
}
