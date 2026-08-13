using System;
using System.Collections;
using System.Windows.Forms;

namespace YmapDeconflicter
{
    public class ListViewColumnSorter : IComparer
    {
        public int SortColumn { get; set; } = 0;
        public SortOrder Order { get; set; } = SortOrder.Ascending;

        public int Compare(object? x, object? y)
        {
            if (x is not ListViewItem itemX || y is not ListViewItem itemY)
                return 0;

            string valueX = itemX.SubItems.Count > SortColumn ? itemX.SubItems[SortColumn].Text : "";
            string valueY = itemY.SubItems.Count > SortColumn ? itemY.SubItems[SortColumn].Text : "";

            // Try to parse as numbers for numeric columns
            int result;
            if (int.TryParse(valueX, out int numX) && int.TryParse(valueY, out int numY))
            {
                result = numX.CompareTo(numY);
            }
            else
            {
                result = string.Compare(valueX, valueY, StringComparison.OrdinalIgnoreCase);
            }

            return Order == SortOrder.Ascending ? result : -result;
        }
    }
}
