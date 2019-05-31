using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace JPEG
{
    class Matrix
    {
        public readonly Pixel[,] Pixels;
        public readonly int Height;
        public readonly int Width;
				
        public Matrix(int height, int width)
        {
            Height = height;
            Width = width;
			
            Pixels = new Pixel[height, width];
            for (var i = 0; i < height; i++)
                for (var j = 0; j < width; j++)
                    Pixels[i, j] = new Pixel();
        }

        
        public static unsafe explicit operator Matrix(Bitmap bmp)
        {
            int height = bmp.Height - bmp.Height % 8;
            int width = bmp.Width - bmp.Width % 8;
            var matrix = new Matrix(height, width);

            var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var ptr = (int*)bmpData.Scan0;

            for (var j = 0; j < height; j++)
            {
                for (var i = 0; i < width; i++)
                {
                    var b = (byte)(*ptr & 255);
                    *ptr = *ptr >> 8;
                    var g = (byte)(*ptr & 255);
                    *ptr = *ptr >> 8;
                    var r = (byte)*ptr;
                    ptr += 1;

                    matrix.Pixels[j, i].C1 = r;
                    matrix.Pixels[j, i].C2 = g;
                    matrix.Pixels[j, i].C3 = b;
                }

                ptr += bmp.Width % 8;
            }
            bmp.UnlockBits(bmpData);

            return matrix;
        }

        public static unsafe explicit operator Bitmap(Matrix matrix)
        {
            var height = matrix.Height;
            var width = matrix.Width;

            var bmp = new Bitmap(width, height);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var ptr = (int*)bmpData.Scan0;

            for (var j = 0; j < bmp.Height; j++)
            {
                for(var i = 0; i < bmp.Width; i++)
                {
                    var pixel = matrix.Pixels[j, i];
                    var pix = 0;
                    pix += pixel.C1;
                    pix = pix << 8;
                    pix += pixel.C2;
                    pix = pix << 8;
                    pix += pixel.C3;
                    *ptr = pix;
                    ptr += 1;
                }
            }

            bmp.UnlockBits(bmpData);

            return bmp;
        }
    }
}