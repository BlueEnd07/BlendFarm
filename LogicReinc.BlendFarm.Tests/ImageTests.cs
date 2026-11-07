using LogicReinc.BlendFarm.Client.ImageTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Tile composition and image saving with SkiaSharp, no Blender needed
    /// </summary>
    [TestClass]
    public class ImageTests
    {
        private static byte[] CreatePngTile(int width, int height, SKColor color)
        {
            using (SKBitmap tile = TileCompositor.CreateResult(width, height))
            {
                tile.Erase(color);
                return ImageEncoder.Encode(tile, SKEncodedImageFormat.Png);
            }
        }

        private static void AssertColor(SKColor expected, SKColor actual, int tolerance = 0)
        {
            bool close = Math.Abs(expected.Red - actual.Red) <= tolerance
                && Math.Abs(expected.Green - actual.Green) <= tolerance
                && Math.Abs(expected.Blue - actual.Blue) <= tolerance
                && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance;
            Assert.IsTrue(close, $"Expected {expected} but got {actual}");
        }

        [TestMethod]
        public void StitchTiles_PlacesTilesAndKeepsAlpha()
        {
            const int width = 8;
            const int height = 4;
            SKColor opaqueRed = new SKColor(255, 0, 0, 255);
            SKColor halfBlue = new SKColor(0, 0, 255, 128);

            //Tiles arrive as PNG bytes from the nodes, Blender's Y axis starts at the bottom
            byte[] bottomLeft = CreatePngTile(width / 2, height / 2, opaqueRed);
            byte[] topRight = CreatePngTile(width / 2, height / 2, halfBlue);

            using (SKBitmap result = TileCompositor.CreateResult(width, height))
            using (SKCanvas canvas = new SKCanvas(result))
            {
                using (SKBitmap tile = ImageConverter.Convert(bottomLeft, ""))
                    TileCompositor.DrawTile(canvas, tile, TileCompositor.GetTileRect(0, 0.5m, 0, 0.5m, width, height));
                using (SKBitmap tile = ImageConverter.Convert(topRight, "PNG"))
                    TileCompositor.DrawTile(canvas, tile, TileCompositor.GetTileRect(0.5m, 1, 0.5m, 1, width, height));
                canvas.Flush();

                Assert.AreEqual(SKAlphaType.Premul, result.AlphaType);
                //Bottom left
                AssertColor(opaqueRed, result.GetPixel(0, height - 1));
                AssertColor(opaqueRed, result.GetPixel(width / 2 - 1, height / 2));
                //Top right, half transparent alpha is kept
                AssertColor(halfBlue, result.GetPixel(width - 1, 0), 1);
                AssertColor(halfBlue, result.GetPixel(width / 2, height / 2 - 1), 1);
                //Not rendered areas stay transparent
                Assert.AreEqual(0, result.GetPixel(0, 0).Alpha);
                Assert.AreEqual(0, result.GetPixel(width - 1, height - 1).Alpha);

                //Alpha survives saving as PNG
                using (SKBitmap reloaded = ImageConverter.Convert(ImageEncoder.Encode(result, SKEncodedImageFormat.Png), "PNG"))
                {
                    AssertColor(halfBlue, reloaded.GetPixel(width - 1, 0), 1);
                    Assert.AreEqual(0, reloaded.GetPixel(0, 0).Alpha);
                }
            }
        }

        [TestMethod]
        public void StitchTiles_OverlapReplacesInsteadOfBlending()
        {
            SKColor halfGreen = new SKColor(0, 255, 0, 128);
            using (SKBitmap result = TileCompositor.CreateResult(4, 1))
            using (SKCanvas canvas = new SKCanvas(result))
            using (SKBitmap tile = ImageConverter.Convert(CreatePngTile(3, 1, halfGreen), ""))
            {
                //Two overlapping tiles with the same transparent content
                TileCompositor.DrawTile(canvas, tile, SKRectI.Create(0, 0, 3, 1));
                TileCompositor.DrawTile(canvas, tile, SKRectI.Create(1, 0, 3, 1));
                canvas.Flush();

                AssertColor(halfGreen, result.GetPixel(1, 0), 1);
                AssertColor(halfGreen, result.GetPixel(2, 0), 1);
            }
        }

        [TestMethod]
        public void SaveImage_UsesFormatOfExtension()
        {
            string dir = Path.Combine(Path.GetTempPath(), "BlendFarmImageTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                SKColor color = new SKColor(200, 100, 50, 255);
                using (SKBitmap image = TileCompositor.CreateResult(16, 16))
                {
                    image.Erase(color);
                    image.SetPixel(0, 0, new SKColor(10, 20, 30, 0));

                    string png = Path.Combine(dir, "out.png");
                    string jpg = Path.Combine(dir, "out.jpg");
                    string jpeg = Path.Combine(dir, "out.JPEG");
                    string webp = Path.Combine(dir, "out.webp");
                    string bmp = Path.Combine(dir, "out.bmp");
                    string tga = Path.Combine(dir, "out.tga");
                    string tgaOriginal = Path.Combine(dir, "original.tga");
                    ImageEncoder.Save(image, png);
                    ImageEncoder.Save(image, jpg);
                    ImageEncoder.Save(image, jpeg);
                    ImageEncoder.Save(image, webp);
                    ImageEncoder.Save(image, bmp);
                    ImageEncoder.Save(image, tga);
                    byte[] original = new byte[] { 1, 2, 3, 4 };
                    ImageEncoder.Save(image, tgaOriginal, original);

                    Assert.IsTrue(File.ReadAllBytes(png).Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "PNG signature");
                    Assert.IsTrue(File.ReadAllBytes(jpg).Take(3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }), "JPEG signature");
                    Assert.IsTrue(File.ReadAllBytes(jpeg).Take(3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }), "JPEG signature (.JPEG)");
                    Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(webp), 0, 4), "WEBP signature");
                    Assert.AreEqual("BM", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(bmp), 0, 2), "BMP signature");
                    //Skia can't encode TGA: PNG data, or the node's original data if available
                    Assert.IsTrue(File.ReadAllBytes(tga).Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "TGA falls back to PNG");
                    CollectionAssert.AreEqual(original, File.ReadAllBytes(tgaOriginal));

                    //PNG is lossless and keeps alpha
                    using (SKBitmap reloaded = ImageConverter.Convert(File.ReadAllBytes(png), "PNG"))
                    {
                        AssertColor(color, reloaded.GetPixel(8, 8));
                        Assert.AreEqual(0, reloaded.GetPixel(0, 0).Alpha);
                    }
                    //JPEG is lossy, close enough at the default quality
                    using (SKBitmap reloaded = ImageConverter.Convert(File.ReadAllBytes(jpg), "JPEG"))
                        AssertColor(color, reloaded.GetPixel(8, 8), 6);
                    //BMP is written as 32-bit with alpha and readable again
                    using (SKBitmap reloaded = ImageConverter.Convert(File.ReadAllBytes(bmp), "BMP"))
                    {
                        Assert.AreEqual(16, reloaded.Width);
                        Assert.AreEqual(16, reloaded.Height);
                        AssertColor(color, reloaded.GetPixel(8, 8));
                        AssertColor(color, reloaded.GetPixel(15, 15));
                    }
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Convert_UndecodableFormat_ReturnsNull()
        {
            Assert.IsNull(ImageConverter.Convert(new byte[] { 1, 2, 3, 4 }, "PNG"));
            Assert.IsNull(ImageConverter.Convert(CreatePngTile(2, 2, SKColors.Red), "OPEN_EXR"));
        }
    }
}
