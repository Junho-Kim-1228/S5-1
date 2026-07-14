using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CoilInspectionApp.UI
{
    public sealed class StatisticsBar
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public Color Color { get; set; }
    }

    public sealed class StatisticsBarChart : Control
    {
        private List<StatisticsBar> _bars = new List<StatisticsBar>();

        public StatisticsBarChart()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Font = new Font("맑은 고딕", 9F);
        }

        public void SetBars(IEnumerable<StatisticsBar> bars)
        {
            _bars = bars?.ToList() ?? new List<StatisticsBar>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            if (_bars.Count == 0 || _bars.All(bar => bar.Value <= 0))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "표시할 결과가 없습니다.",
                    Font,
                    ClientRectangle,
                    Color.DimGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int maxValue = Math.Max(1, _bars.Max(bar => bar.Value));
            int rowHeight = Math.Max(28, (Height - 12) / Math.Max(1, _bars.Count));
            int labelWidth = Math.Min(90, Math.Max(58, Width / 4));
            int valueWidth = 42;
            int barLeft = labelWidth + 8;
            int barWidth = Math.Max(20, Width - barLeft - valueWidth - 10);

            for (int index = 0; index < _bars.Count; index++)
            {
                StatisticsBar bar = _bars[index];
                int top = 7 + index * rowHeight;
                int height = Math.Min(18, rowHeight - 8);
                var labelRect = new Rectangle(4, top, labelWidth, height);
                var backgroundRect = new Rectangle(barLeft, top, barWidth, height);
                int filledWidth = bar.Value <= 0
                    ? 0
                    : Math.Max(3, (int)Math.Round((double)bar.Value / maxValue * barWidth));

                TextRenderer.DrawText(
                    e.Graphics,
                    bar.Label ?? "-",
                    Font,
                    labelRect,
                    Color.FromArgb(55, 65, 81),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                using (var background = new SolidBrush(Color.FromArgb(237, 241, 245)))
                    e.Graphics.FillRectangle(background, backgroundRect);
                if (filledWidth > 0)
                {
                    using (var fill = new SolidBrush(bar.Color))
                        e.Graphics.FillRectangle(fill, new Rectangle(barLeft, top, filledWidth, height));
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    bar.Value.ToString(),
                    Font,
                    new Rectangle(barLeft + barWidth + 5, top, valueWidth, height),
                    Color.FromArgb(31, 41, 55),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
