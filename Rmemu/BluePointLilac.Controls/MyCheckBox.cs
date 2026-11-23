using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    [DefaultProperty("Checked")]
    public class MyCheckBox : PictureBox
    {
        public MyCheckBox()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            SizeMode = PictureBoxSizeMode.Normal;
            Size = new Size(44, 28).DpiZoom();
            AccessibleRole = AccessibleRole.CheckButton;
        }

        private bool? _Checked;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _Checked == true;
            set
            {
                if(_Checked == value) return;
                if(_Checked == null)
                {
                    SetCheckedSilently(value);
                    return;
                }
                if(PreCheckChanging != null && !PreCheckChanging.Invoke()) return;
                CheckChanging?.Invoke();
                if(PreCheckChanged != null && !PreCheckChanged.Invoke()) return;
                SetCheckedSilently(value);
                CheckChanged?.Invoke();
            }
        }

        public void SetCheckedSilently(bool value)
        {
            _Checked = value;
            Image = value ? TurnOnImage : TurnOffImage;
            Invalidate();
            if(IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }

        public Func<bool> PreCheckChanging;
        public Func<bool> PreCheckChanged;
        public Action CheckChanging;
        public Action CheckChanged;

        private Image turnOnImage;
        private Image turnOffImage;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image TurnOnImage
        {
            get => turnOnImage;
            set { turnOnImage = value; if(Checked) { Image = value; Invalidate(); } }
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image TurnOffImage
        {
            get => turnOffImage;
            set { turnOffImage = value; if(!Checked) { Image = value; Invalidate(); } }
        }

        protected virtual void OnUserToggle() { if(Enabled) Checked = !Checked; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button != MouseButtons.Left) return;
            Focus();
            OnUserToggle();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OnUserToggle();
                e.Handled = e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        internal void ApplyTheme() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(Image == null)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int trackWidth = Math.Min(40.DpiZoom(), Math.Max(4, Width - 4.DpiZoom()));
                int trackHeight = Math.Min(20.DpiZoom(), Math.Max(4, Height - 4.DpiZoom()));
                Rectangle track = new Rectangle((Width - trackWidth) / 2, (Height - trackHeight) / 2, trackWidth, trackHeight);
                int diameter = Math.Max(1, Math.Min(track.Width, track.Height) - 1);
                Color fill = !Enabled ? FluentTheme.Border : Checked ? FluentTheme.Accent : BackColor;
                Color outline = Checked && Enabled ? FluentTheme.Accent : FluentTheme.Muted;
                using(GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(track.Left, track.Top, diameter, diameter, 90, 180);
                    path.AddArc(track.Right - diameter, track.Top, diameter, diameter, 270, 180);
                    path.CloseFigure();
                    using(Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                    using(Pen pen = new Pen(outline)) e.Graphics.DrawPath(pen, path);
                }
                int inset = 4.DpiZoom();
                int knob = Math.Max(1, track.Height - inset * 2);
                Color knobColor = Checked && Enabled
                    ? SystemInformation.HighContrast ? SystemColors.HighlightText : FluentTheme.IsDark ? FluentTheme.Background : Color.White
                    : Enabled ? FluentTheme.Foreground : FluentTheme.Muted;
                using(Brush brush = new SolidBrush(knobColor))
                    e.Graphics.FillEllipse(brush, Checked ? track.Right - knob - inset : track.Left + inset,
                        track.Top + (track.Height - knob) / 2, knob, knob);
            }
            if(Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), FluentTheme.Foreground, BackColor);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibleObject(this);
        private sealed class SwitchAccessibleObject : ControlAccessibleObject
        {
            private readonly MyCheckBox owner;
            public SwitchAccessibleObject(MyCheckBox owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role => AccessibleRole.CheckButton;
            public override string DefaultAction => AppString.W("Toggle");
            public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
            public override void DoDefaultAction() { if(owner.Enabled) owner.OnUserToggle(); }
        }
    }
}
