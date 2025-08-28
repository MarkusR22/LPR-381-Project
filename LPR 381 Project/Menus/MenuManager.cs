using System;
using System.IO;
using System.Linq;
using System.Text;
using LPR_381_Project.Parsers;
using LPR_381_Project.Solvers;
using LPR_381_Project.Utils;

namespace LPR_381_Project.Menus
{
    public static class MenuManager
    {
        public static void Run()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== LPR 381 Project ===");
                Console.WriteLine("1) Primal Simplex");
                Console.WriteLine("2) Revised Primal Simplex");
                Console.WriteLine("3) Branch & Bound (Simplex)");
                Console.WriteLine("4) Cutting Plane (Gomory)");
                Console.WriteLine("5) Branch & Bound Knapsack");
                Console.WriteLine("6) Sensitivity (basic placeholders)");
                Console.WriteLine("0) Exit");
                Console.Write("Select (0-6): ");
                var key = Console.ReadLine();

                try
                {
                    switch (key)
                    {
                        case "1": RunPrimal(); break;
                        case "2": RunRevised(); break;
                        case "3": RunBnBSimplex(); break;
                        case "4": RunCuttingPlane(); break;
                        case "5": RunKnapsack(); break;
                        case "6": RunSensitivity(); break;
                        case "0": return;
                        default:
                            Console.WriteLine("Invalid option. Press any key...");
                            Console.ReadKey();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("\n[ERROR]");
                    Console.WriteLine(ex.Message);
                    Console.WriteLine("\nPress any key...");
                    Console.ReadKey();
                }
            }
        }

        // ---------- helpers ----------
        static string ProjectRoot()
        {
            // walk up until we find the folder that contains "Input"
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Input")))
            {
                var p = Directory.GetParent(dir);
                if (p == null) break;
                dir = p.FullName;
            }
            return dir ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        static string AskInputPath(string defaultRelative = @"Input\")
        {
            var root = ProjectRoot();
            var def = Path.Combine(root, defaultRelative);
            Console.Write($"Input file [{def}]: ");
            var entered = Console.ReadLine();
            var path = string.IsNullOrWhiteSpace(entered) ? def : entered.Trim('"');
            if (!Path.IsPathRooted(path)) path = Path.Combine(root, path);
            if (!File.Exists(path)) throw new FileNotFoundException("Input not found", path);
            return path;
        }

        static string NewOutputPath(string alg)
        {
            var root = ProjectRoot();
            var outDir = Path.Combine(root, @"Output\output");
            Directory.CreateDirectory(outDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(outDir, $"{alg}-output-{stamp}.txt");
        }

        static void PauseDone(string path)
        {
            Console.WriteLine($"\nDone. Output → {path}");
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }

        static void PrintTableau(double[,] T, TextWriter w)
        {
            int r = T.GetLength(0), c = T.GetLength(1);
            for (int i = 0; i < r; i++)
            {
                for (int j = 0; j < c; j++) w.Write($"{T[i, j],8:0.###} ");
                w.WriteLine();
            }
        }

        // ---------- actions ----------
        static void RunPrimal()
        {
            // Expect your general InputFileParser to return a LinearModel the primal solver accepts
            var inputPath = AskInputPath();
            var model = InputFileParser.ParseFile(inputPath);

            var solver = new PrimalSimplexSolver();
            var iters = solver.Solve(model);

            var outPath = NewOutputPath("PrimalSimplex");
            using (var sw = new StreamWriter(outPath, false, Encoding.UTF8))
            {
                sw.WriteLine("=== Canonical Form & Tableaus (Primal Simplex) ===");
                for (int i = 0; i < iters.Count; i++)
                {
                    sw.WriteLine($"-- Tableau {i} --");
                    PrintTableau(iters[i], sw);
                    sw.WriteLine();
                }
            }
            PauseDone(outPath);
        }

        static void RunRevised()
        {
            var inputPath = AskInputPath();
            var model = InputFileParser.ParseFile(inputPath);

            var solver = new RevisedSimplexSolver();
            var iters = solver.Solve(model);

            var outPath = NewOutputPath("RevisedSimplex");
            using (var sw = new StreamWriter(outPath, false, Encoding.UTF8))
            {
                sw.WriteLine("=== Product Form / Price-Out (Revised Primal Simplex) ===");
                for (int i = 0; i < iters.Count; i++)
                {
                    sw.WriteLine($"-- Iteration {i} --");
                    PrintTableau(iters[i], sw);
                    sw.WriteLine();
                }
            }
            PauseDone(outPath);
        }

        static void RunBnBSimplex()
        {
            try
                {
                string inputPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\Input\sample.txt"));
                LinearModel model = InputFileParser.ParseFile(inputPath);
                    
                var solver = new BranchAndBoundSimplex();
                var result = solver.Solve(model);

                // Summary (solver already prints per-iteration tableaus and writes Output/branch_and_bound_nodes.txt)
                Console.WriteLine("== Branch & Bound Result ==");
                Console.WriteLine($"Input: {inputPath}");
                Console.WriteLine($"Feasible: {result.Feasible}");
                Console.WriteLine($"Nodes explored: {result.NodesExplored}");
                Console.WriteLine($"Best objective: {result.BestObjective:0.##}");

                foreach (var kv in result.BestX.OrderBy(k => k.Key))
                Console.WriteLine($"{kv.Key} = {kv.Value:0.##}");
                Console.WriteLine("\n== Branch Log ==");
                Console.WriteLine(result.Log);

             // Point to the unified node output file if present
             string nodeOut = Path.GetFullPath("Output/branch_and_bound_nodes.txt");
             if (File.Exists(nodeOut))
                        Console.WriteLine($"\nAll node tableaus were saved to: {nodeOut}");
             }
                catch (Exception ex)
                {
                    Console.WriteLine("Error running Branch & Bound:");
                    Console.WriteLine(ex);
                }
            
        }

        static void RunCuttingPlane()
        {
            var inputPath = AskInputPath();
            var lines = File.ReadAllLines(inputPath);
            var solver = new CuttingPlane();
            var res = solver.SolveFromBriefFormat(lines);

            var outPath = NewOutputPath("CuttingPlane");
            using (var sw = new StreamWriter(outPath, false, Encoding.UTF8))
            {
                sw.WriteLine("=== Cutting Plane (Gomory) ===");
                sw.WriteLine($"Z* = {res.ZOpt:0.###}");
                sw.WriteLine("x* = " + string.Join(", ", res.XOpt.Select(v => v.ToString("0.###"))));
                sw.WriteLine($"Cuts added: {res.CutsAdded}");
                sw.WriteLine();

                sw.WriteLine("=== Iterations / B^-1 & Reduced Costs ===");
                foreach (var log in res.Logs) sw.WriteLine(log);

                sw.WriteLine();
                sw.WriteLine("=== Tableaus ===");
                for (int i = 0; i < res.Tableaus.Count; i++)
                {
                    sw.WriteLine($"-- Tableau {i} --");
                    PrintTableau(res.Tableaus[i], sw);
                    sw.WriteLine();
                }
            }
            PauseDone(outPath);
        }

        static void RunKnapsack()
        {
            // For the project’s binary knapsack IP (from the brief)
            // You can also parse different instances from file if desired.
            int capacity = 40;
            int[] profit = { 2, 3, 3, 5, 2, 4 };
            int[] weight = { 11, 8, 6, 14, 10, 10 };

            var knap = new BnBKnapsack(capacity, profit, weight);
            var nodes = knap.Solve().ToList();
            var best = knap.GetBestCandidate();

            var outPath = NewOutputPath("BnB-Knapsack");
            using (var sw = new StreamWriter(outPath, false, Encoding.UTF8))
            {
                sw.WriteLine("=== Branch & Bound Knapsack ===");
                foreach (var node in nodes)
                {
                    sw.WriteLine(node.ToString());
                    sw.WriteLine();
                }
                sw.WriteLine("=== Best Candidate ===");
                sw.WriteLine(best != null ? best.ToString() : "None");
            }
            PauseDone(outPath);
        }

        static void RunSensitivity()
        {
            // Minimal placeholders so the menu option exists; wire to your Sensitivity utilities later.
            var outPath = NewOutputPath("Sensitivity");
            using (var sw = new StreamWriter(outPath, false, Encoding.UTF8))
            {
                sw.WriteLine("Sensitivity Analysis (placeholders)");
                sw.WriteLine("- Range of a selected non-basic variable");
                sw.WriteLine("- Change a selected non-basic variable");
                sw.WriteLine("- Range of a selected basic variable");
                sw.WriteLine("- RHS ranges & changes");
                sw.WriteLine("- Add new activity/constraint");
                sw.WriteLine("- Shadow prices");
                sw.WriteLine("- Dual model / Strong–Weak duality checks");
            }
            PauseDone(outPath);
        }
    }
}