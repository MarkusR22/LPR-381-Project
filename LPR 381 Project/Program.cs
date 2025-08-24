using LPR_381_Project.Models;
using LPR_381_Project.Parsers;
using LPR_381_Project.Solvers;
using LPR_381_Project.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace LPR_381_Project
{
    class Program
    {
        static void Main(string[] args)
        {

            //Testing Branch and Bound Simplex

            Console.WriteLine("This is Simplex Branch and Bound:");
            TestBnBSimplex();
            Console.WriteLine("=================================================");


            //Testing Dual Simplex Method
            Console.WriteLine("This is Dual:");
            TestDual();
            Console.WriteLine("=================================================");


            //Testing Knapsack Method
            Console.WriteLine("This is Knapsack:");
            TestKnapsack();
            Console.WriteLine("=================================================");

            //Testing Cutting Plane Method
            Console.WriteLine("This is Cutting Plane:");
            TestCuttingPlane();
            Console.WriteLine("=================================================");


            // --- Primal and Revised Simplex test using Korean Auto LP ---
            Console.WriteLine("\n--- Testing Primal and Revised Simplex ---");
            TestPrimalAndRevised();

            

            
        }

        static void TestCuttingPlane()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            string inputPath = Path.Combine(projectRoot, "Input", "sample.txt");
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Input file not found: {inputPath}");
                return;
            }

            Console.WriteLine("Reading: " + Path.GetFullPath(inputPath));
            string[] lines = File.ReadAllLines(inputPath);
            Console.WriteLine("(debug) file contents:");
            for (int i = 0; i < lines.Length; i++)
                Console.WriteLine($"[{i + 1}] {lines[i]}");

            var solver = new CuttingPlane();
            var result = solver.SolveFromBriefFormat(lines);
            Console.WriteLine($"(debug) read {lines.Length} raw lines from {inputPath}");

            Console.WriteLine("=== Cutting Plane Result ===");
            Console.WriteLine($"Z* = {result.ZOpt:0.###}");
            Console.WriteLine("x* = " + string.Join(", ", result.XOpt.Select(v => v.ToString("0.###"))));
            Console.WriteLine($"Cuts added: {result.CutsAdded}");

            // Project root -> Output\output.txt
            string outputPath = Path.Combine(projectRoot, "Output", "output.txt");

            using (var sw = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                sw.WriteLine("=== Cutting Plane Solver Output ===");
                sw.WriteLine($"Z* = {result.ZOpt:0.###}");
                sw.WriteLine("x* = " + string.Join(", ", result.XOpt.Select(v => v.ToString("0.###"))));
                sw.WriteLine($"Cuts added: {result.CutsAdded}");
                sw.WriteLine();

                sw.WriteLine("=== Iteration Log ===");
                foreach (var log in result.Logs ?? Enumerable.Empty<string>())
                    sw.WriteLine(log);

                sw.WriteLine();
                sw.WriteLine("=== Tableaus ===");
                if (result.Tableaus != null)
                {
                    for (int k = 0; k < result.Tableaus.Count; k++)
                    {
                        sw.WriteLine($"-- Tableau {k} --");
                        PrintTableau(result.Tableaus[k], sw);
                        sw.WriteLine();
                    }
                }
                sw.Flush(); // force write now
            }

            // Prove bytes exist
            long len = new FileInfo(outputPath).Length;
            Console.WriteLine($"Wrote {len} bytes to: {outputPath}");
        }
            for (int i = 0; i < iterations.Count; i++)
            {
                Console.WriteLine($"--- Iteration {i} ---");
                PrintTableau(iterations[i]);
                Console.WriteLine();
            }

            
        }


        static void PrintTableau(double[,] T, TextWriter w)
        {
            int r = T.GetLength(0), c = T.GetLength(1);
            for (int i = 0; i < r; i++)
            {
                for (int j = 0; j < c; j++) w.Write($"{T[i, j],8:0.###} ");
                w.WriteLine();
            }
        static void TestBnBSimplex()
        {
            try
            {
                // 1) Load a simple ILP (see sample content below)
                var inputPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\Input\sample.txt"));
                var model = InputFileParser.ParseFile(inputPath);

                // 2) Run Branch & Bound (Dual → Primal at each node)
                var bnb = new BranchAndBoundSimplex();
                var result = bnb.Solve(model);

                // 3) Print results
                Console.WriteLine("== Branch & Bound Result ==");
                Console.WriteLine($"Feasible: {result.Feasible}");
                Console.WriteLine($"Nodes explored: {result.NodesExplored}");
                Console.WriteLine($"Best objective: {result.BestObjective:0.###}");
                foreach (var kv in result.BestX.OrderBy(k => k.Key))
                    Console.WriteLine($"{kv.Key} = {kv.Value:0.###}");

                Console.WriteLine("\n== Branch Log ==");
                Console.WriteLine(result.Log);

                Console.WriteLine("\nPer-node tableaux were written to: bin\\Debug\\Output\\node_*.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error running Branch & Bound:");
                Console.WriteLine(ex.ToString());
            }
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

