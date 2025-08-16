using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LPR_381_Project.Solvers;

namespace LPR_381_Project
{
    class Program
    {
        static void Main(string[] args)
        {
            TestDual();
            
        }

        static void TestDual()
        {
            double[,] initialT =
                {
                    //Korean Auto LP
                    { -50, -100, 0, 0, 0 },
                    { -7, -2, 1, 0, -28 },
                    { -2, -12, 0, 1, -24 },
                };

            var dual = new DualSimplex();

            var iterations = dual.Solve(initialT);

            for (int i = 0; i < iterations.Count; i++)
            {
                Console.WriteLine($"--- Iteration {i} ---");
                PrintTableau(iterations[i]);
                Console.WriteLine();
            }
        }

        static void PrintTableau(double[,] tableau)
        {
            int rows = tableau.GetLength(0);
            int cols = tableau.GetLength(1);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    Console.Write($"{tableau[i, j],8:0.###} "); 
                }
                Console.WriteLine();
            }
        }
    }
}
