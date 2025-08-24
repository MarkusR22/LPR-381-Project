using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using LPR_381_Project.Models;
using LPR_381_Project.Parsers;
using LPR_381_Project.Solvers;
using LPR_381_Project.Utils;

namespace LPR_381_Project
{
    class Program
    {
        static void Main(string[] args)
        {
            // Testing Branch and Bound Simplex
            Console.WriteLine("This is Simplex Branch and Bound:");
            TestBnBSimplex();
            Console.WriteLine("=================================================");

            // Testing Dual Simplex
            Console.WriteLine("This is Dual:");
            TestDual();
            Console.WriteLine("=================================================");

            // Testing Knapsack
            Console.WriteLine("This is Knapsack:");
            TestKnapsack();
            Console.WriteLine("=================================================");

            // Testing Cutting Plane
            Console.WriteLine("This is Cutting Plane:");
            TestCuttingPlane();
            Console.WriteLine("=================================================");

            // Primal & Revised Simplex
            Console.WriteLine("\n--- Testing Primal and Revised Simplex ---");
            TestPrimalAndRevised();

            Console.WriteLine("\nDone. Press any key to exit...");
            Console.ReadKey();
        }

        // ---------- CUTTING PLANE ----------
        static void TestCuttingPlane()
        {
            // Find project root by walking up until we see the Input folder
            string dir = AppDomain.CurrentDomain.BaseDirectory; // bin\Debug\...
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Input")))
            {
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            if (dir == null || !Directory.Exists(Path.Combine(dir, "Input")))
            {
                Console.WriteLine("Could not locate project root (folder with 'Input').");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            string inputPath = Path.Combine(projectRoot, "Input", "sample.txt");

            Console.WriteLine("Reading: " + inputPath);
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Input file not found: {inputPath}");
                return;
            }

            string[] lines = File.ReadAllLines(inputPath);
            Console.WriteLine("(debug) file contents:");
            for (int i = 0; i < lines.Length; i++)
                Console.WriteLine($"[{i + 1}] {lines[i]}");

            var solver = new CuttingPlane();
            var result = solver.SolveFromBriefFormat(lines);

            Console.WriteLine("=== Cutting Plane Result ===");
            Console.WriteLine($"Z* = {result.ZOpt:0.###}");
            Console.WriteLine("x* = " + string.Join(", ", result.XOpt.Select(v => v.ToString("0.###"))));
            Console.WriteLine($"Cuts added: {result.CutsAdded}");

            // Output: projectRoot\Output\output.txt
            string outDir = Path.Combine(projectRoot, "Output");
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, "output.txt");
            Console.WriteLine("Writing output to: " + outputPath);

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
            }

            long len = new FileInfo(outputPath).Length;
            Console.WriteLine($"Wrote {len} bytes to: {outputPath}");
        }

        // ---------- BRANCH & BOUND (Simplex) ----------
        static void TestBnBSimplex()
        {
            try
            {
                string inputPath = Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Input\sample.txt"));
                var model = InputFileParser.ParseFile(inputPath);

                var bnb = new BranchAndBoundSimplex();
                var result = bnb.Solve(model);

                Console.WriteLine("== Branch & Bound Result ==");
                Console.WriteLine($"Feasible: {result.Feasible}");
                Console.WriteLine($"Nodes explored: {result.NodesExplored}");
                Console.WriteLine($"Best objective: {result.BestObjective:0.###}");
                foreach (var kv in result.BestX.OrderBy(k => k.Key))
                    Console.WriteLine($"{kv.Key} = {kv.Value:0.###}");

                Console.WriteLine("\n== Branch Log ==");
                Console.WriteLine(result.Log);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error running Branch & Bound:");
                Console.WriteLine(ex.ToString());
            }
        }

        // ---------- KNAPSACK (B&B) ----------
        static void TestKnapsack()
        {
            int capacity = 40;
            int[] z = { 2, 3, 3, 5, 2, 4 };
            int[] c = { 11, 8, 6, 14, 10, 10 };

            var knapsack = new BnBKnapsack(capacity, z, c);
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

        // ---------- DUAL SIMPLEX ----------
        static void TestDual()
        {
            double[,] initialT =
            {
                // Korean Auto LP example
                { -50, -100, 0, 0, 0 },
                {  -7,   -2, 1, 0, -28 },
                {  -2,  -12, 0, 1, -24 },
            };

            var dual = new DualSimplex();
            var iterations = dual.Solve(initialT);

            for (int i = 0; i < iterations.Count; i++)
            {
                Console.WriteLine($"--- Iteration {i} ---");
                PrintTableau(iterations[i], Console.Out);
                Console.WriteLine();
            }
        }

        // ---------- PRIMAL & REVISED SIMPLEX ----------
        static void TestPrimalAndRevised()
        {
            var model = new Models.LinearModel { Obj = Models.Objective.Maximize };

            model.Variables.Add(new Models.Variable("x1", 50, Models.VarType.Positive));
            model.Variables.Add(new Models.Variable("x2", 100, Models.VarType.Positive));

            model.Constraints.Add(new Models.Constraint(
                new List<double> { 7, 2 }, Models.Relation.GreaterThanOrEqual, 28));
            model.Constraints.Add(new Models.Constraint(
                new List<double> { 2, 12 }, Models.Relation.GreaterThanOrEqual, 24));

            var primalSolver = new PrimalSimplexSolver();
            var primalIterations = primalSolver.Solve(model);
            Console.WriteLine("\nPrimal Simplex Iterations:");
            PrintIterations(primalIterations);

            var revisedSolver = new RevisedSimplexSolver();
            var revisedIterations = revisedSolver.Solve(model);
            Console.WriteLine("\nRevised Simplex Iterations:");
            PrintIterations(revisedIterations);
        }

        // ---------- helpers ----------
        static void PrintIterations(List<double[,]> iterations)
        {
            for (int i = 0; i < iterations.Count; i++)
            {
                Console.WriteLine($"--- Iteration {i} ---");
                PrintTableau(iterations[i], Console.Out);
                Console.WriteLine();
            }
        }

        static void PrintTableau(double[,] T, TextWriter w)
        {
            int r = T.GetLength(0), c = T.GetLength(1);
            for (int i = 0; i < r; i++)
            {
                for (int j = 0; j < c; j++)
                    w.Write($"{T[i, j],8:0.###} ");
                w.WriteLine();
            }
        }
    }
}
