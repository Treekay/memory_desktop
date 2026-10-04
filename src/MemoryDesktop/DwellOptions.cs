using System;
using Forms = System.Windows.Forms;

namespace MemoryDesktop
{
    public enum PhotoDwell { Short, Medium, Long }

    internal static class DwellOptions
    {
        internal static readonly PhotoDwell[] Presets = { PhotoDwell.Short, PhotoDwell.Medium, PhotoDwell.Long };
        internal static void Bounds(PhotoDwell preset, out double minimum, out double maximum)
        {
            switch (preset)
            {
                case PhotoDwell.Medium: minimum = 9; maximum = 12; break;
                case PhotoDwell.Long: minimum = 11; maximum = 17; break;
                default: minimum = 6; maximum = 8; break;
            }
        }
        internal static string Label(PhotoDwell preset)
        { double minimum, maximum; Bounds(preset, out minimum, out maximum); return minimum + "–" + maximum + " 秒"; }
        internal static Forms.ToolStripMenuItem CreateMenu(Action<PhotoDwell> select)
        {
            var menu = new Forms.ToolStripMenuItem("停留时间");
            foreach (var preset in Presets)
            {
                PhotoDwell choice = preset;
                var item = new Forms.ToolStripMenuItem(Label(choice)) { Tag = choice };
                item.Click += delegate { UpdateChecks(menu, choice); select(choice); };
                menu.DropDownItems.Add(item);
            }
            return menu;
        }
        internal static void UpdateChecks(Forms.ToolStripMenuItem menu, PhotoDwell selected)
        {
            menu.Text = "停留时间（" + Label(selected) + "）";
            foreach (Forms.ToolStripMenuItem item in menu.DropDownItems) item.Checked = (PhotoDwell)item.Tag == selected;
        }
    }
}
