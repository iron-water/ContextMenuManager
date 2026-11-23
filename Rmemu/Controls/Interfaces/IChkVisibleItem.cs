using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.Windows.Forms;

namespace Rmemu.Controls.Interfaces
{
    interface IChkVisibleItem
    {
        bool ItemVisible { get; set; }
        VisibleCheckBox ChkVisible { get; set; }
    }

    sealed class VisibleCheckBox : MyCheckBox
    {
        private readonly IChkVisibleItem item;
        private readonly MyListItem listItem;
        private bool changing;

        public VisibleCheckBox(IChkVisibleItem item)
        {
            this.item = item;
            listItem = (MyListItem)item;
            listItem.AddCtr(this);
            AccessibleRole = AccessibleRole.CheckButton;
            listItem.ParentChanged += (sender, e) =>
            {
                if(!listItem.IsDisposed && listItem.Parent != null) Synchronize();
            };
            listItem.TextChanged += (sender, e) => UpdateDescription();
        }

        // Assigning a bound state must never execute a backend mutation or a protection prompt.
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new bool Checked
        {
            get => base.Checked;
            set
            {
                SetCheckedSilently(value);
                UpdateDescription();
            }
        }

        public void Synchronize()
        {
            if(IsDisposed || listItem.IsDisposed) return;
            try
            {
                bool actual = ChangeHistory.ReadVisibility(item);
                SetCheckedSilently(actual);
                Enabled = true;
                UpdateDescription();
                ApplyVisibilityFilter();
            }
            catch(Exception ex)
            {
                Enabled = false;
                string message = AppString.W("ReadVisibilityFailed", ex.Message);
                AccessibleName = listItem.Text + AppString.W("StateUnknownSuffix");
                AccessibleDescription = message;
                listItem.AccessibleDescription = message;
                ToolTipBox.SetToolTip(this, message);
            }
        }

        protected override void OnUserToggle()
        {
            if(changing || !Enabled) return;
            changing = true;
            try
            {
                // Refresh first: another application or undo may have changed this registration.
                bool before = ChangeHistory.ReadVisibility(item);
                SetCheckedSilently(before);
                base.Checked = !before;
                if(base.Checked == before) return; // An existing protection callback declined.
                bool requested = base.Checked;
                bool success = ChangeHistory.TryChangeVisibility(item, requested,
                    out bool actual, out string error);
                SetCheckedSilently(actual);
                Synchronize();
                if(!success) AppMessageBox.Show(error);
            }
            catch(Exception ex)
            {
                Synchronize();
                AppMessageBox.Show(AppString.W("ChangeVisibilityFailed") + ex.Message);
            }
            finally
            {
                changing = false;
            }
        }

        private void UpdateDescription()
        {
            string state = base.Checked ? AppString.W("ItemEnabled") : AppString.W("ItemDisabled");
            string action = base.Checked ? AppString.W("DisableItem") : AppString.W("EnableItem");
            AccessibleName = listItem.Text + "：" + state;
            AccessibleDescription = AppString.W("ToggleHint", state, action);
            listItem.AccessibleDescription = AppString.W("VisibilityState", state);
            ToolTipBox.SetToolTip(this, state + " · " + action);
        }

        private void ApplyVisibilityFilter()
        {
            if(listItem is FoldSubItem subItem && subItem.FoldGroupItem != null) return;
            if(listItem.FindForm() is ShellStoreDialog.ShellStoreForm) return;
            if(AppConfig.HideDisabledItems) listItem.Visible = base.Checked;
        }
    }
}
