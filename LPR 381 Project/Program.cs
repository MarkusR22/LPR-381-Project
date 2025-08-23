using LPR_381_Project.Models;
using LPR_381_Project.Solvers;
using LPR_381_Project.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project
{
    class Program
    {
        static void Main(string[] args)
        {
            //Testing Dual Simplex Method
            Console.WriteLine("This is Dual:");
            TestDual();
            Console.WriteLine("=================================================");


            //Testing Knapsack Method
            Console.WriteLine("This is Knapsack:");
            TestKnapsack();
            Console.WriteLine("=================================================");


            // --- Primal and Revised Simplex test using Korean Auto LP ---
            Console.WriteLine("\n--- Testing Primal and Revised Simplex ---");
            TestPrimalAndRevised();

            

            
        }

        static void TestKnapsack()
        {
            //Problem from Assignment
            int capacity = 40;
            int[] z = { 2, 3, 3, 5, 2, 4 };
            int[] c = { 11, 8, 6, 14, 10, 10 };

            BnBKnapsack knapsack = new BnBKnapsack(capacity, z, c);

            foreach (var node in knapsack.Solve())
            {
                Console.WriteLine(node.ToString());
                Console.WriteLine();
            } 

            var best = knapsack.GetBestCandidate();
            if (best != null)
            {
                Console.WriteLine("Best candidate found:");
                Console.WriteLine(best.ToString());
            }
            else
            {
                Console.WriteLine("No feasible candidate found.");
            }
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

        static void TestPrimalAndRevised()
        {
            // Build LinearModel for Korean Auto LP
            var model = new Models.LinearModel
            {
                Obj = Models.Objective.Maximize
            };

            // Variables: x1 (comedy), x2 (football)
            model.Variables.Add(new Models.Variable("x1", 50, Models.VarType.Positive));
            model.Variables.Add(new Models.Variable("x2", 100, Models.VarType.Positive));

            // Constraints
            model.Constraints.Add(new Models.Constraint(
                new List<double> { 7, 2 }, Models.Relation.GreaterThanOrEqual, 28));
            model.Constraints.Add(new Models.Constraint(
                new List<double> { 2, 12 }, Models.Relation.GreaterThanOrEqual, 24));

            // Solve with Primal Simplex
            var primalSolver = new PrimalSimplexSolver();
            var primalIterations = primalSolver.Solve(model);
            Console.WriteLine("\nPrimal Simplex Iterations:");
            PrintIterations(primalIterations);

            // Solve with Revised Simplex
            var revisedSolver = new RevisedSimplexSolver();
            var revisedIterations = revisedSolver.Solve(model);
            Console.WriteLine("\nRevised Simplex Iterations:");
            PrintIterations(revisedIterations);
        }


        // Helper to print all iterations
        static void PrintIterations(List<double[,]> iterations)
        {
            for (int i = 0; i < iterations.Count; i++)
            {
                Console.WriteLine($"--- Iteration {i} ---");
                PrintTableau(iterations[i]);
                Console.WriteLine();
            }
        }


    }

}

