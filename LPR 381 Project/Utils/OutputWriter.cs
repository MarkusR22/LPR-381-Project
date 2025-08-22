using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Utils
{
    public static class OutputWriter
    {
        // Write all iterations of a solver to a file
        public static void WriteIterations(string fileName, List<double[,]> iterations)
        {
            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string filePath = Path.Combine(folderPath, fileName);

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                for (int k = 0; k < iterations.Count; k++)
                {
                    writer.WriteLine("--- Iteration " + k + " ---");
                    WriteTable(writer, iterations[k]);
                    writer.WriteLine();
                }
            }
        }

        // Helper to write a single tableau
        private static void WriteTable(StreamWriter writer, double[,] table)
        {
            int rows = table.GetLength(0);
            int cols = table.GetLength(1);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    writer.Write(string.Format("{0,8:0.###} ", table[i, j]));
                }
                writer.WriteLine();
            }
        }
    }
}
