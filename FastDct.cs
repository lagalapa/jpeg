using System;

namespace JPEG
{
    internal class FastDct
    {
        private const int DctSize = 8;
        private static readonly double[,] DctKernel = GenerateDctMatrix();
        private static readonly double[,] DctKernelTransposed = Transpose(DctKernel);

        public static double[,] Dct(double[,] input)
        {
            var temp = Multiply(DctKernel, input);
            var dctCoeffs = Multiply(temp, DctKernelTransposed);

            return dctCoeffs;
        }

        public static void InverseDct(double[,] coeffs, double[,] output)
        {
            var temp = Multiply(DctKernelTransposed, coeffs);
            var idctCoeffs = Multiply(temp, DctKernel);
            Array.Copy(idctCoeffs, output, idctCoeffs.Length);
        }

        public static double[,] GenerateDctMatrix()
        {
            var dctCoeffs = new double[DctSize, DctSize];
            var alpha = Math.Sqrt(2.0 / DctSize);
            const int denominator = 2 * DctSize;

            for (var j = 0; j < DctSize; j++)
            {
                dctCoeffs[0, j] = Math.Sqrt(1.0 / DctSize);
            }

            for (var j = 0; j < DctSize; j++)
            {
                for (var i = 1; i < DctSize; i++)
                {
                    dctCoeffs[i, j] = alpha * Math.Cos((2 * j + 1) * i * 3.14159 / denominator);
                }
            }

            return (dctCoeffs);
        }

        private static double[,] Multiply(double[,] m1, double[,] m2)
        {
            var row = m1.GetLength(0);
            var col = m1.GetLength(0);
            var m3 = new double[row, col];

            for (var i = 0; i < row; i++)
            {
                for (var j = 0; j < col; j++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < row; k++)
                    {
                        sum = sum + m1[i, k] * m2[k, j];
                    }
                    m3[i, j] = sum;
                }
            }

            return m3;
        }

        private static double[,] Transpose(double[,] m)
        {
            var height = m.GetLength(0);
            var width = m.GetLength(1);
            var mt = new double[height, width];

            for (var i = 0; i < height; i++)
                for (var j = 0; j < width; j++)
                {
                    mt[j, i] = m[i, j];
                }

            return (mt);
        }
    }
}
