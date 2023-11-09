﻿using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace SSP_assignment.Helpers;

/// <summary>
/// Required packages:
/// - SixLabors.ImageSharp Version="2.1.3
/// - SixLabors.ImageSharp.Drawing Version="1.0.0-beta15"
/// - Microsoft.ML Version="1.7.1
/// </summary>
public class ImageHelper
{
    /// <summary>
    /// Applies a KMeans cluster over the given image rgba data and replaces each pixel with its segment centroid color
    /// </summary>
    /// <param name="imageBytes"></param>
    /// <param name="segments"></param>
    /// <returns>PNG Image bytes, segment colors</returns>
    public static (byte[], string[]) EditImage(byte[] imageBytes, int segments = 3)
    {
        var mlContext = new MLContext(seed: 0);

        using var image = Image.Load<Rgba32>(imageBytes);

        IDataView trainingData = mlContext.Data.LoadFromEnumerable(CreateDataViewFromImage(image));

        var options = new KMeansTrainer.Options
        {
            NumberOfClusters = segments,
            NumberOfThreads = 1
        };

        var pipeline = mlContext.Clustering.Trainers.KMeans(options);

        var model = pipeline.Fit(trainingData);

        VBuffer<float>[] centroids = default;
        model.Model.GetClusterCentroids(ref centroids, out var k);

        var transformedTestData = model.Transform(trainingData);

        var predictions = mlContext.Data.CreateEnumerable<Prediction>(
            transformedTestData, reuseRowObject: false).ToList();

        var resultImage = new Image<Rgba32>(image.Width, image.Height);

        var centroidsArray = centroids
            .Select(x => x.GetValues().ToArray())
            .Select(x => new Rgba32((byte)x[0], (byte)x[1], (byte)x[2], (byte)x[3]))
            .ToArray();

        var pixels = predictions
            .Select(x => centroidsArray[x.PredictedLabel - 1])
            .GetEnumerator();

        for (var x = 0; x < image.Width; x++)
        {
            for (var y = 0; y < image.Height; y++)
            {
                resultImage[x, y] = pixels.Current;
                pixels.MoveNext();
            }
        }

        using var stream = new MemoryStream();
        resultImage.SaveAsPng(stream);
        return (stream.ToArray(), centroidsArray.Select(x => x.ToHex()[0..6]).ToArray());
    }


    public static byte[] AddTextToImage(byte[] imageBytes, params (string text, (float x, float y) position, int fontSize, string colorHex)[] texts)
    {
        var memoryStream = new MemoryStream();

        var image = Image.Load(imageBytes);

        image.Clone(img =>
        {
            foreach (var (text, (x, y), fontSize, colorHex) in texts)
            {
                var font = SystemFonts.CreateFont("Verdana", fontSize);
                var color = Rgba32.ParseHex(colorHex);

                img.DrawText(text, font, color, new PointF(x, y));
            }
        })
        .SaveAsPng(memoryStream);

        return memoryStream.ToArray();
    }

    public static byte[] AddWeatherDataToImage(byte[] imageBytes, string weatherData)
    {
        // Load the image to determine its size
        using var image = Image.Load(imageBytes);

        // Choose a simple font size and color
        int fontSize = 120; // This should be adjusted based on the image size
        string colorHex = "000000"; // Black color for the font

        // Set the position for the text, for example, 10 pixels from the top and left.
        float textX = 10f; // Horizontal position (10 pixels from the left edge)
        float textY = 10f; // Vertical position (10 pixels from the top edge)

        // Call the AddTextToImage method with the calculated position
        return ImageHelper.AddTextToImage(imageBytes, (weatherData, (textX, textY), fontSize, colorHex));
    }


    private static IEnumerable<RgbValue> CreateDataViewFromImage(Image<Rgba32> image)
    {

        for (var x = 0; x < image.Width; x++)
        {
            for (var y = 0; y < image.Height; y++)
            {
                yield return new RgbValue
                {
                    Features = new float[] { image[x, y].R, image[x, y].G, image[x, y].B, image[x, y].A }
                };
            }
        }
    }

    private class RgbValue
    {
        [KeyType(2)]
        public uint Label { get; set; }

        [VectorType(4)]
        public float[] Features { get; set; }
    }

    private class Prediction
    {
        public uint PredictedLabel { get; set; }
    }
}