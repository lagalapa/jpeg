using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace JPEG
{
    internal class Program
	{
		private const int CompressionQuality = 70;
        private const int DctSize = 8;
        private static readonly int[,] QuantizationMatrix;

        static Program()
        {
            QuantizationMatrix = GetQuantizationMatrix(CompressionQuality); 
        }

        private static void Main() 
        {
            Process.GetCurrentProcess().ProcessorAffinity = (IntPtr) 3;
            Console.WriteLine(IntPtr.Size == 8 ? "64-bit version" : "32-bit version");

            var fileName = "sample.bmp";
            //var fileName = "earth.bmp";
            //var fileName = "marbles.bmp";
            //var fileName = "marbles2.bmp";
            fileName = fileName.Insert(0, @"Images\");
            var compressedFileName = fileName + ".compressed." + CompressionQuality;
            var uncompressedFileName = fileName + ".uncompressed." + CompressionQuality + ".bmp";

            var sw = Stopwatch.StartNew();
            using (var fileStream = File.OpenRead(fileName))
            using (var bmp = (Bitmap)Image.FromStream(fileStream, false, false))
            {
                var imageMatrix = (Matrix)bmp;
                sw.Stop();
                Console.WriteLine("getPixel: {0}", sw.ElapsedMilliseconds);
                Console.WriteLine($"{bmp.Width}x{bmp.Height} - {fileStream.Length / (1024.0 * 1024):F2} MB");

                sw.Start();
                var compressionResult = Compress(imageMatrix);
                sw.Stop();
                Console.WriteLine("Compression: " + sw.Elapsed);

                compressionResult.Save(compressedFileName);
            }

            var compressedImage = CompressedImage.Load(compressedFileName);

            sw.Restart();
            var uncompressedImage = Uncompress(compressedImage);
            Console.WriteLine("Decompression: " + sw.Elapsed);

            sw.Restart();
            var resultBmp = (Bitmap)uncompressedImage;
            Console.WriteLine("setPixel: {0}", sw.ElapsedMilliseconds);
            resultBmp.Save(uncompressedFileName, ImageFormat.Bmp);

            Console.WriteLine($"Peak commit size: {MemoryMeter.PeakPrivateBytes() / (1024.0 * 1024):F2} MB");
            Console.WriteLine($"Peak working set: {MemoryMeter.PeakWorkingSet() / (1024.0 * 1024):F2} MB");
        }

        private static CompressedImage Compress(Matrix matrix)
        {
            var allQuantizedBytes = new byte[matrix.Height * matrix.Width * 3];


            Parallel.For(0, matrix.Height/DctSize, h =>
            {
                Parallel.For(0, matrix.Width / DctSize, w =>
                {
                    var counter = 0;
                    foreach (var selector in new Func<Pixel, double>[]
                    {
                        p => 16.0 + (65.738 * p.C1 + 129.057 * p.C2 + 24.064 * p.C3) / 256.0,
                        p => 128.0 + (-37.945 * p.C1 - 74.494 * p.C2 + 112.439 * p.C3) / 256.0,
                        p => 128.0 + (112.439 * p.C1 - 94.154 * p.C2 - 18.285 * p.C3) / 256.0
                    })
                    {
                        var subMatrix = GetSubMatrix(matrix, h * DctSize, DctSize, w * DctSize, DctSize, selector);
                        ShiftMatrixValues(subMatrix, -128);
                        var channelFreqs = FastDct.Dct(subMatrix);
                        var quantizedFreqs = Quantize(channelFreqs);
                        var quantizedBytes = ZigZagScan(quantizedFreqs);
                        Array.Copy(quantizedBytes, 0, allQuantizedBytes,
                            h * 8 * matrix.Width * 3 + w * 64 * 3 + 64 * counter++, 64);
                    }
                });
            });

            var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out var decodeTable, out var bitsCount);

            return new CompressedImage
            {
                Quality = CompressionQuality,
                CompressedBytes = compressedBytes,
                BitsCount = bitsCount,
                DecodeTable = decodeTable,
                Height = matrix.Height,
                Width = matrix.Width
            };
        }


        private static Matrix Uncompress(CompressedImage image)
		{
            var result = new Matrix(image.Height, image.Width);
			using (var allQuantizedBytes =
				new MemoryStream(HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount)))
			{
				for (var y = 0; y < image.Height; y += DctSize)
				{
					for (var x = 0; x < image.Width; x += DctSize)
					{
						var _y = new double[DctSize, DctSize];
						var cb = new double[DctSize, DctSize];
						var cr = new double[DctSize, DctSize];
						foreach (var channel in new []{_y, cb, cr})
						{
							var quantizedBytes = new byte[DctSize * DctSize];
							allQuantizedBytes.ReadAsync(quantizedBytes, 0, quantizedBytes.Length).Wait();
							var quantizedFreqs = ZigZagUnScan(quantizedBytes);
                            var channelFreqs = DeQuantize(quantizedFreqs);
                            FastDct.InverseDct(channelFreqs, channel);
                            ShiftMatrixValues(channel, 128);
						}
						SetPixels(result, _y, cb, cr, y, x);
					}
				}
			}

			return result;
		}

		private static void ShiftMatrixValues(double[,] subMatrix, int shiftValue)
		{
			var height = subMatrix.GetLength(0);
			var width = subMatrix.GetLength(1);
			
			for(var y = 0; y < height; y++)
				for(var x = 0; x < width; x++)
					subMatrix[y, x] = subMatrix[y, x] + shiftValue;
		}

        private static void SetPixels(Matrix matrix, double[,] a, double[,] b, double[,] c, int yOffset, int xOffset)
        {
            var height = a.GetLength(0);
            var width = a.GetLength(1);

            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    matrix.Pixels[yOffset + y, xOffset + x].C1 = ToByte((298.082 * a[y, x] + 408.583 * c[y, x]) / 256.0 - 222.921);
                    matrix.Pixels[yOffset + y, xOffset + x].C2 =
                        ToByte((298.082 * a[y, x] - 100.291 * b[y, x] - 208.120 * c[y, x]) / 256.0 + 135.576);
                    matrix.Pixels[yOffset + y, xOffset + x].C3 = ToByte((298.082 * a[y, x] + 516.412 * b[y, x]) / 256.0 - 276.836);
                }
        }

        public static byte ToByte(double d)
        {
            var val = (int)d;
            if (val > byte.MaxValue)
                return byte.MaxValue;
            if (val < byte.MinValue)
                return byte.MinValue;

            return (byte)val;
        }

        private static double[,] GetSubMatrix(Matrix matrix, int yOffset, int yLength, int xOffset, int xLength, Func<Pixel, double> componentSelector)
		{
			var result = new double[yLength, xLength];
			for(var j = 0; j < yLength; j++)
				for(var i = 0; i < xLength; i++)
					result[j, i] = componentSelector(matrix.Pixels[yOffset + j, xOffset + i]);

			return result;
		}

        private static byte[] ZigZagScan(byte[,] channelFreqs)
        {
			return new[]
			{
				channelFreqs[0, 0], channelFreqs[0, 1], channelFreqs[1, 0], channelFreqs[2, 0], channelFreqs[1, 1], channelFreqs[0, 2], channelFreqs[0, 3], channelFreqs[1, 2],
				channelFreqs[2, 1], channelFreqs[3, 0], channelFreqs[4, 0], channelFreqs[3, 1], channelFreqs[2, 2], channelFreqs[1, 3],  channelFreqs[0, 4], channelFreqs[0, 5],
				channelFreqs[1, 4], channelFreqs[2, 3], channelFreqs[3, 2], channelFreqs[4, 1], channelFreqs[5, 0], channelFreqs[6, 0], channelFreqs[5, 1], channelFreqs[4, 2],
				channelFreqs[3, 3], channelFreqs[2, 4], channelFreqs[1, 5],  channelFreqs[0, 6], channelFreqs[0, 7], channelFreqs[1, 6], channelFreqs[2, 5], channelFreqs[3, 4],
				channelFreqs[4, 3], channelFreqs[5, 2], channelFreqs[6, 1], channelFreqs[7, 0], channelFreqs[7, 1], channelFreqs[6, 2], channelFreqs[5, 3], channelFreqs[4, 4],
				channelFreqs[3, 5], channelFreqs[2, 6], channelFreqs[1, 7], channelFreqs[2, 7], channelFreqs[3, 6], channelFreqs[4, 5], channelFreqs[5, 4], channelFreqs[6, 3],
				channelFreqs[7, 2], channelFreqs[7, 3], channelFreqs[6, 4], channelFreqs[5, 5], channelFreqs[4, 6], channelFreqs[3, 7], channelFreqs[4, 7], channelFreqs[5, 6],
				channelFreqs[6, 5], channelFreqs[7, 4], channelFreqs[7, 5], channelFreqs[6, 6], channelFreqs[5, 7], channelFreqs[6, 7], channelFreqs[7, 6], channelFreqs[7, 7]
			};
		}

        private static byte[,] ZigZagUnScan(byte[] quantizedBytes)
        {
			return new[,]
			{
				{ quantizedBytes[0], quantizedBytes[1], quantizedBytes[5], quantizedBytes[6], quantizedBytes[14], quantizedBytes[15], quantizedBytes[27], quantizedBytes[28] },
				{ quantizedBytes[2], quantizedBytes[4], quantizedBytes[7], quantizedBytes[13], quantizedBytes[16], quantizedBytes[26], quantizedBytes[29], quantizedBytes[42] },
				{ quantizedBytes[3], quantizedBytes[8], quantizedBytes[12], quantizedBytes[17], quantizedBytes[25], quantizedBytes[30], quantizedBytes[41], quantizedBytes[43] },
				{ quantizedBytes[9], quantizedBytes[11], quantizedBytes[18], quantizedBytes[24], quantizedBytes[31], quantizedBytes[40], quantizedBytes[44], quantizedBytes[53] },
				{ quantizedBytes[10], quantizedBytes[19], quantizedBytes[23], quantizedBytes[32], quantizedBytes[39], quantizedBytes[45], quantizedBytes[52], quantizedBytes[54] },
				{ quantizedBytes[20], quantizedBytes[22], quantizedBytes[33], quantizedBytes[38], quantizedBytes[46], quantizedBytes[51], quantizedBytes[55], quantizedBytes[60] },
				{ quantizedBytes[21], quantizedBytes[34], quantizedBytes[37], quantizedBytes[47], quantizedBytes[50], quantizedBytes[56], quantizedBytes[59], quantizedBytes[61] },
				{ quantizedBytes[35], quantizedBytes[36], quantizedBytes[48], quantizedBytes[49], quantizedBytes[57], quantizedBytes[58], quantizedBytes[62], quantizedBytes[63] }
			};
		}

		private static byte[,] Quantize(double[,] channelFreqs)
        {
            var height = channelFreqs.GetLength(0);
            var width = channelFreqs.GetLength(1);
            var result = new byte[height, width];

			for(int y = 0; y < height; y++)
			{
				for(int x = 0; x < width; x++)
				{
					result[y, x] = (byte)(channelFreqs[y, x] / QuantizationMatrix[y, x]);
				}
			}

			return result;
		}

		private static double[,] DeQuantize(byte[,] quantizedBytes)
        {
            var height = quantizedBytes.GetLength(0);
            var width = quantizedBytes.GetLength(1);
            var result = new double[height, width];

            for (int y = 0; y < height; y++)
			{
				for(int x = 0; x < width; x++)
				{
					result[y, x] = (sbyte)quantizedBytes[y, x] * QuantizationMatrix[y, x];//NOTE cast to sbyte not to loose negative numbers
				}
			}

			return result;
		}

		private static int[,] GetQuantizationMatrix(int quality)
		{
			if(quality < 1 || quality > 99)
				throw new ArgumentException("quality must be in [1,99] interval");

			var multiplier = quality < 50 ? 5000 / quality : 200 - 2 * quality;

			var result = new[,]
			{
				{16, 11, 10, 16, 24, 40, 51, 61},
				{12, 12, 14, 19, 26, 58, 60, 55},
				{14, 13, 16, 24, 40, 57, 69, 56},
				{14, 17, 22, 29, 51, 87, 80, 62},
				{18, 22, 37, 56, 68, 109, 103, 77},
				{24, 35, 55, 64, 81, 104, 113, 92},
				{49, 64, 78, 87, 103, 121, 120, 101},
				{72, 92, 95, 98, 112, 100, 103, 99}
			};

			for(int y = 0; y < result.GetLength(0); y++)
			{
				for(int x = 0; x < result.GetLength(1); x++)
				{
					result[y, x] = (multiplier * result[y, x] + 50) / 100;
				}
			}
			return result;
		}
    }
}
