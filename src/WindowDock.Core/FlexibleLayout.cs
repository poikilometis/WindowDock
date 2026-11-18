#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;


namespace WindowDock
{
    internal sealed class DividerSpec
    {
        public Rectangle Bounds;
        public string Kind;
        public int Index, First, Second, Minimum;
        public bool Vertical;
        public List<double> Weights;
        public SplitArea Split;
        public int SecondMinimum;
    }
    internal sealed class FlexiblePlan
    {
        public readonly List<Rectangle> Tiles = new List<Rectangle>();
        public readonly List<DividerSpec> Dividers = new List<DividerSpec>();
        public Size Extent;
    }
    internal static class FlexibleLayout
    {
        public static int[] Allocate(int span, int count, int minimum, List<double> weights)
        {
            if (count <= 0) return new int[0];
            span = Math.Max(count, span); minimum = Math.Max(1, Math.Min(minimum, span / count));
            double[] values = new double[count];
            for (int i = 0; i < count; i++) values[i] = weights != null && weights.Count == count && weights[i] > 0 &&
                !Double.IsNaN(weights[i]) && !Double.IsInfinity(weights[i]) ? weights[i] : 1;
            int[] sizes = new int[count]; bool[] fixedSize = new bool[count]; int remaining = span, open = count;
            while (open > 0)
            {
                double sum = 0; for (int i = 0; i < count; i++) if (!fixedSize[i]) sum += values[i];
                bool changed = false;
                for (int i = 0; i < count; i++) if (!fixedSize[i] && remaining * values[i] / sum < minimum)
                { sizes[i] = minimum; fixedSize[i] = true; remaining -= minimum; open--; changed = true; }
                if (changed) continue;
                int last = -1, used = 0;
                for (int i = 0; i < count; i++) if (!fixedSize[i])
                { sizes[i] = Math.Max(minimum, (int)Math.Floor(remaining * values[i] / sum)); used += sizes[i]; last = i; }
                if (last >= 0) sizes[last] += remaining - used;
                break;
            }
            return sizes;
        }
        public static FlexiblePlan Plan(int count, Size viewport, int minWidth, int minHeight, WorkspaceConfig config, int gap)
        {
            FlexiblePlan result = new FlexiblePlan();
            int width = Math.Max(1, viewport.Width), height = Math.Max(1, viewport.Height);
            if (count == 0) { result.Extent = Size.Empty; return result; }
            if (config.Layout == "master" && count > 1 && width >= minWidth * 2 + gap * 3)
            {
                int available = width - gap * 3;
                int first = Math.Max(minWidth, Math.Min(available - minWidth, (int)(available * config.MasterRatio)));
                int second = available - first;
                int mainHeight = Math.Max(minHeight, height - gap * 2);
                result.Tiles.Add(new Rectangle(gap, gap, first, mainHeight));
                int sideHeight = Grid(result, count - 1, new Rectangle(first + gap * 2, gap, second, mainHeight),
                    minWidth, minHeight, config, gap, false);
                int extent = Math.Max(mainHeight, sideHeight) + gap * 2;
                result.Dividers.Add(new DividerSpec { Kind = "master", Vertical = true, First = first, Second = second,
                    Minimum = minWidth, Bounds = new Rectangle(first + gap, gap, gap, extent - gap * 2) });
                result.Extent = new Size(width, extent); return result;
            }
            if (config.Layout == "dual" && count > 2 && width >= minWidth * 2 + gap * 3)
            {
                int firstHeight = Math.Max(minHeight, (int)((height - gap * 3) * config.PrimaryRatio));
                int secondHeight = Math.Max(minHeight, height - gap * 3 - firstHeight);
                int available = width - gap * 3;
                int firstWidth = Math.Max(minWidth, Math.Min(available - minWidth, (int)(available * config.DualRatio)));
                result.Tiles.Add(new Rectangle(gap, gap, firstWidth, firstHeight));
                result.Tiles.Add(new Rectangle(firstWidth + gap * 2, gap, available - firstWidth, firstHeight));
                int bottomHeight = Grid(result, count - 2, new Rectangle(gap, firstHeight + gap * 2, width - gap * 2, secondHeight),
                    minWidth, minHeight, config, gap, true);
                result.Dividers.Add(new DividerSpec { Kind = "dual", Vertical = true, First = firstWidth, Second = available - firstWidth,
                    Minimum = minWidth, Bounds = new Rectangle(firstWidth + gap, gap, gap, firstHeight) });
                result.Dividers.Add(new DividerSpec { Kind = "primary", Vertical = false, First = firstHeight, Second = secondHeight,
                    Minimum = minHeight, Bounds = new Rectangle(gap, firstHeight + gap, width - gap * 2, gap) });
                result.Extent = new Size(width, gap * 3 + firstHeight + bottomHeight); return result;
            }
            int gridHeight = Grid(result, count, new Rectangle(gap, gap, Math.Max(1, width - gap * 2), Math.Max(1, height - gap * 2)),
                minWidth, minHeight, config, gap, true);
            result.Extent = new Size(width, gridHeight + gap * 2); return result;
        }
        static int Grid(FlexiblePlan plan, int count, Rectangle area, int minWidth, int minHeight, WorkspaceConfig config, int gap, bool rowGrips)
        {
            GridPlan basePlan = LayoutEngine.Plan(count, new Size(area.Width + gap * 2, area.Height + gap * 2), minWidth, minHeight, config.Columns, gap);
            int columns = basePlan.Columns, rows = basePlan.Rows;
            int[] widths = Allocate(Math.Max(columns, area.Width - gap * (columns - 1)), columns, minWidth, config.ColumnWeights);
            int span = Math.Max(rows * minHeight, area.Height - gap * (rows - 1));
            int[] heights = Allocate(span, rows, minHeight, config.RowWeights);
            int y = area.Top, extent = heights.Sum() + gap * (rows - 1);
            for (int row = 0; row < rows; row++)
            {
                int x = area.Left;
                for (int col = 0; col < columns; col++)
                {
                    if (row * columns + col < count) plan.Tiles.Add(new Rectangle(x, y, widths[col], heights[row]));
                    x += widths[col] + gap;
                }
                if (rowGrips && row < rows - 1) plan.Dividers.Add(new DividerSpec { Kind = "rows", Index = row,
                    First = heights[row], Second = heights[row + 1], Minimum = minHeight, Vertical = false,
                    Weights = heights.Select(delegate(int v) { return (double)v; }).ToList(),
                    Bounds = new Rectangle(area.Left, y + heights[row], area.Width, gap) });
                y += heights[row] + gap;
            }
            int boundary = area.Left;
            for (int col = 0; col < columns - 1; col++)
            {
                boundary += widths[col];
                plan.Dividers.Add(new DividerSpec { Kind = "columns", Index = col, First = widths[col], Second = widths[col + 1],
                    Minimum = Math.Min(minWidth, (widths[col] + widths[col + 1]) / 2), Vertical = true,
                    Weights = widths.Select(delegate(int v) { return (double)v; }).ToList(),
                    Bounds = new Rectangle(boundary, area.Top, gap, extent) });
                boundary += gap;
            }
            return extent;
        }
        public static void Resize(WorkspaceConfig config, DividerSpec spec, int delta)
        {
            int total = spec.First + spec.Second;
            if (spec.Split is { } split)
            {
                split.Ratio = Math.Clamp(spec.First + delta, spec.Minimum, total - spec.SecondMinimum) / (double)total;
                return;
            }
            int minimum = Math.Min(spec.Minimum, total / 2);
            int first = Math.Max(minimum, Math.Min(total - minimum, spec.First + delta));
            double ratio = first / (double)Math.Max(1, total);
            if (spec.Kind == "master") config.MasterRatio = ratio;
            else if (spec.Kind == "dual") config.DualRatio = ratio;
            else if (spec.Kind == "primary") config.PrimaryRatio = ratio;
            else
            {
                List<double> weights = new List<double>(spec.Weights);
                weights[spec.Index] = first; weights[spec.Index + 1] = total - first;
                if (spec.Kind == "columns") config.ColumnWeights = weights; else config.RowWeights = weights;
            }
        }
    }
}
