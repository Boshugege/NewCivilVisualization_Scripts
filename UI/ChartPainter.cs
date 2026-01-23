using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Random = UnityEngine.Random;
using OxyPlot.SkiaSharp;

public class ChartPainter
{
    // public const int width = 300;
    // public const int height = 800;

    public class PlotData
    {
        public string title;
        public float dt;
        public int width;
        public int height;

        public string[] name;
        public float[] yBottom;
        public float[] yTop;
        public float[][] values;

        public PlotData(string title, int numSeries, float dt, int width = 300, int height = 800)
        {
            this.title = title;
            this.dt = dt;
            this.width = width;
            this.height = height;

            this.name = new string[numSeries];
            this.yBottom = new float[numSeries];
            this.yTop = new float[numSeries];
            this.values = new float[numSeries][];
        }
    }

    public static PlotModel DataToPlotModel(PlotData data)
    {
        var plotModel = new PlotModel
        {
            Title = data.title,
            TitleFontSize = 26
        };

        // plotModel.Padding = new OxyThickness(0, 0, 0, 0);
        // plotModel.PlotMargins = new OxyThickness(50, 50, 0, 50);
        // plotModel.TitlePadding
        // plotModel.Background = OxyColor.FromArgb(255, 255, 255, 255); // White background

        plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "DisplacementAxis",
        });

        int numSeries = data.name.Length;
        for (int i = 0; i < numSeries; i++)
        {
            var lineSeries = new LineSeries
            {
                Color = GetRandomOxyColor(),
                YAxisKey = $"YAxis{i}"
            };

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = data.name[i],
                Key = $"YAxis{i}",
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                StartPosition = data.yBottom[i],
                EndPosition = data.yTop[i],
                TitleFontSize = 16,
            };

            var values = data.values[i];
            for (int j = 0; j < values.Length; j++)
            {
                lineSeries.Points.Add(new DataPoint(data.dt * j, values[j]));
            }

            plotModel.Series.Add(lineSeries);
            plotModel.Axes.Add(yAxis);
        }

        return plotModel;
    }

    private static PlotModel CreateAxisOnlyModel(PlotData data)
    {
        var plotModel = new PlotModel
        {
            Title = data.title,
            TitleFontSize = 26,
            Background = OxyColors.White,
        };

        plotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "DisplacementAxis",
            Minimum = 0,
            Maximum = data.dt * (data.values[0].Length - 1),
            FontSize = 15,
        });

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        int numSeries = data.name.Length;

        for (int i = 0; i < numSeries; i++)
        {
            minY = Math.Min(minY, data.values[i].Min());
            maxY = Math.Max(maxY, data.values[i].Max());
        }

        for (int i = 0; i < numSeries; i++)
        {
            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = data.name[i],
                Key = $"YAxis{i}",
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                StartPosition = data.yBottom[i],
                EndPosition = data.yTop[i],
                TitleFontSize = 22,
                FontSize = 15,
                Minimum = minY,
                Maximum = maxY,
            };

            plotModel.Axes.Add(yAxis);
        }

        return plotModel;
    }

    // 创建只有数据线的模型
    private static PlotModel CreateSeriesOnlyModel(PlotData data)
    {
        var plotModel = new PlotModel
        {
            Title = "",
            TitlePadding = 0,
            TitleFontSize = 0,
            Background = OxyColors.Transparent,
            Padding = new OxyThickness(0, 0, 0, 0),
            PlotMargins = new OxyThickness(0, 0, 0, 0),
            PlotAreaBorderThickness = new OxyThickness(0, 0, 0, 0),
        };

        int numSeries = data.name.Length;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < numSeries; i++)
        {
            minY = Math.Min(minY, data.values[i].Min());
            maxY = Math.Max(maxY, data.values[i].Max());
        }

        for (int i = 0; i < numSeries; i++)
        {
            var lineSeries = new LineSeries
            {
                Color = GetRandomOxyColor(),
                YAxisKey = $"YAxis{i}"
            };

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Key = $"YAxis{i}",
                IsAxisVisible = false,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                StartPosition = data.yBottom[i],
                EndPosition = data.yTop[i],
                TickStyle = TickStyle.None,
                Minimum = minY,
                Maximum = maxY,
            };

            var values = data.values[i];
            for (int j = 0; j < values.Length; j++)
            {
                lineSeries.Points.Add(new DataPoint(data.dt * j, values[j]));
            }

            plotModel.Series.Add(lineSeries);
            plotModel.Axes.Add(yAxis);
        }

        return plotModel;
    }

    // Image path: "Assets/Resources/Sensor.png"
    // plotRect: position of (top, bottom, left, right) (pixels)
    public static void DataToPng(PlotData data, out Vector4 plotRect)
    {
        var plotModel = DataToPlotModel(data);
        PngExporter.Export(plotModel, "Assets/Resources/Sensor.png", data.width, data.height);

        var plotArea = plotModel.PlotArea;
        plotRect = new Vector4((float)plotArea.Top, (float)plotArea.Bottom, (float)plotArea.Left, (float)plotArea.Right);
    }

    // plotRect: position of (top, bottom, left, right) (pixels)
    public static void DataToTexture(PlotData data, out Texture2D axis, out Texture2D series, out Vector4 plotRect)
    {
        {
            MemoryStream stream = new MemoryStream();
            var plotModel = CreateAxisOnlyModel(data);
            PngExporter.Export(plotModel, stream, data.width, data.height);
            var texture = new Texture2D(data.width, data.height);
            texture.LoadImage(stream.ToArray());
            axis = texture;

            var plotArea = plotModel.PlotArea;
            plotRect = new Vector4((float)plotArea.Top, (float)plotArea.Bottom, (float)plotArea.Left, (float)plotArea.Right);
        }

        {
            MemoryStream stream = new MemoryStream();
            var plotModel = CreateSeriesOnlyModel(data);
            PngExporter.Export(plotModel, stream, data.width, data.height);
            var texture = new Texture2D(data.width, data.height);
            texture.LoadImage(stream.ToArray());
            series = texture;
        }
    }

    private static OxyColor GetRandomOxyColor()
    {
        int r = Random.Range(0, 256);
        int g = Random.Range(0, 256);
        int b = Random.Range(0, 256);
        return OxyColor.FromRgb((byte)r, (byte)g, (byte)b);
    }
}
