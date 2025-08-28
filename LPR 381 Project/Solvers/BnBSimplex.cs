using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using LPR_381_Project.Models;
using System.Xml.Linq;

namespace LPR_381_Project.Solvers
{
    public class BranchAndBoundSimplex
    {
        private const int MAX_NODES = 10_000;

        // Seed info carried from a solved parent into its children
        private class Seed
        {
            public double[,] ParentLast;
            public List<string> ParentHeaders = new List<string>();
            public BnBSimplexBound NewBound;
        }

        // For passing seeds into SolveNodeLP
        private readonly Dictionary<BnBSimplexNode, Seed> _seeds = new Dictionary<BnBSimplexNode, Seed>();

        public BnBSimplexResult Solve(LinearModel baseModel)
        {
            if (baseModel == null) throw new ArgumentNullException(nameof(baseModel));
            if (baseModel.Variables == null || baseModel.Variables.Count == 0)
                throw new ArgumentException("Model has no variables.");

            // Root: x <= 1 for binaries
            var root = new BnBSimplexNode { Label = "Root", Depth = 0 };
            for (int j = 0; j < baseModel.Variables.Count; j++)
            {
                if (baseModel.Variables[j].Type == VarType.Binary)
                    root.Bounds.Add(new BnBSimplexBound { VarIndex = j, IsUpper = true, Value = 1.0 });
            }

            var best = new BnBSimplexResult();
            var pending = new List<BnBSimplexNode> { root };
            int explored = 0;
            var sbLog = new StringBuilder();

            Directory.CreateDirectory("Output");
            var unifiedPath = Path.Combine("Output", "branch_and_bound_nodes.txt");
            File.WriteAllText(unifiedPath,
                "==== Branch & Bound Nodes ====" + Environment.NewLine +
                "Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                "Objective: " + baseModel.Obj + Environment.NewLine + Environment.NewLine);

            while (pending.Count > 0 && explored < MAX_NODES)
            {
                pending = SortByBound(pending, baseModel.Obj);
                var node = pending[0];
                pending.RemoveAt(0);
                explored++;

                // Solve node (uses seed if available)
                List<double[,]> iterations;
                List<string> headers;
                bool infeasible;
                SolveNodeLP(baseModel, node, out iterations, out headers, out infeasible);
                node.Infeasible = infeasible;

                var status = NodeStatus(baseModel, node);
                PrettyPrintNode(node, iterations, headers, status);
                AppendNodeToUnifiedFile(unifiedPath, node, iterations, headers, status);

                sbLog.AppendLine("Node[" + node.Label + "] obj=" + (node.Infeasible ? "INF" : node.Objective.ToString("0.##")) + " status=" + status);

                if (node.Infeasible) continue;

                // Bound pruning
                if (baseModel.Obj == Objective.Maximize)
                {
                    if (best.Feasible && node.Objective + 1e-12 < best.BestObjective) continue;
                }
                else
                {
                    if (best.Feasible && node.Objective - 1e-12 > best.BestObjective) continue;
                }

                // Integrality check
                node.IsInteger = IsIntegerFeasible(node.X, baseModel);
                if (node.IsInteger)
                {
                    if (!best.Feasible || Better(baseModel.Obj, node.Objective, best.BestObjective))
                    {
                        best.Feasible = true;
                        best.BestObjective = node.Objective;
                        best.BestX = new Dictionary<string, double>(node.X);
                    }
                    continue;
                }

                // Choose branching variable (largest fractional among integer/binary)
                int branchIndex = ChooseBranchVariable(baseModel, node.X);
                if (branchIndex < 0) continue;

                double xval = node.X[baseModel.Variables[branchIndex].Name];
                double floorV = Math.Floor(xval + 1e-12);
                double ceilV = Math.Ceiling(xval - 1e-12);

                // Children with numeric labels
                var left = new BnBSimplexNode
                {
                    Label = node.Label == "Root" ? "1" : node.Label + ".1",
                    Depth = node.Depth + 1,
                    Bounds = new List<BnBSimplexBound>(node.Bounds)
                    { new BnBSimplexBound { VarIndex = branchIndex, IsUpper = true,  Value = floorV } }
                };

                var right = new BnBSimplexNode
                {
                    Label = node.Label == "Root" ? "2" : node.Label + ".2",
                    Depth = node.Depth + 1,
                    Bounds = new List<BnBSimplexBound>(node.Bounds)
                    { new BnBSimplexBound { VarIndex = branchIndex, IsUpper = false, Value = ceilV } }
                };

                // Seed both children with this node's final tableau + appropriate new bound
                double[,] parentLast = (iterations != null && iterations.Count > 0) ? iterations[iterations.Count - 1] : null;
                if (parentLast != null)
                {
                    _seeds[left] = new Seed { ParentLast = parentLast, ParentHeaders = new List<string>(headers), NewBound = left.Bounds[left.Bounds.Count - 1] };
                    _seeds[right] = new Seed { ParentLast = parentLast, ParentHeaders = new List<string>(headers), NewBound = right.Bounds[right.Bounds.Count - 1] };
                }

                pending.Add(left);
                pending.Add(right);
            }

            best.NodesExplored = explored;
            best.Log = sbLog.ToString();
            return best;
        }

        private static List<BnBSimplexNode> SortByBound(List<BnBSimplexNode> nodes, Objective obj)
        {
            if (obj == Objective.Maximize)
                return nodes.OrderByDescending(n => n.Objective).ToList();
            return nodes.OrderBy(n => n.Objective == 0 ? double.PositiveInfinity : n.Objective).ToList();
        }

        // === Node solve: starts from parent's tableau + new bound if seeded; else from a fresh tableau ===
        private void SolveNodeLP(
            LinearModel baseModel,
            BnBSimplexNode node,
            out List<double[,]> iterations,
            out List<string> headers,
            out bool infeasible)
        {
            iterations = new List<double[,]>();
            headers = new List<string>();
            infeasible = false;

            // Build node-local model (normalized ≤), remember row types if we need a fresh tableau.
            List<char> rowTypes;
            LinearModel model = BuildNodeModel(baseModel, node.Bounds, out rowTypes);

            double[,] T;

            // If we have a seed (parent’s last tableau), build Iteration 0 from it.
            Seed seed;
            if (_seeds.TryGetValue(node, out seed) && seed != null && seed.ParentLast != null && seed.ParentHeaders != null && seed.ParentHeaders.Count > 0)
            {
                T = CreateSeededTableau(seed.ParentLast, seed.ParentHeaders, seed.NewBound, model.Variables.Count, out headers);
                iterations.Add(Clone(T)); // Iteration 0 == parent's last + new row/col
            }
            else
            {
                // Fallback: fresh tableau as Iteration 0
                T = BuildTableau(model, rowTypes, out headers);
                iterations.Add(Clone(T));
            }

            bool usedDual = false;
            try
            {
                if (HasNegativeRHS(T))
                {
                    var dual = new DualSimplex();
                    var dualIters = dual.Solve(T);
                    if (dualIters != null && dualIters.Count > 0)
                    {
                        for (int k = 1; k < dualIters.Count; k++) iterations.Add(Clone(dualIters[k]));
                        T = dualIters[dualIters.Count - 1];
                        usedDual = true;
                    }
                }
            }
            catch
            {
                node.Infeasible = true;
                node.SolverUsed = "DualSimplex (failed)";
                infeasible = true;
                return;
            }

            try
            {
                var primalIters = RunPrimalSimplexOnTableau(T, model.Obj);
                if (primalIters != null && primalIters.Count > 0)
                {
                    for (int k = 1; k < primalIters.Count; k++) iterations.Add(Clone(primalIters[k]));
                    T = primalIters[primalIters.Count - 1];
                }
                node.SolverUsed = usedDual ? "Dual+Primal" : "PrimalSimplex";
            }
            catch
            {
                try
                {
                    var primal = new PrimalSimplexSolver();
                    var primIters = primal.Solve(model);
                    foreach (var tab in primIters) iterations.Add(Clone(tab));
                    T = primIters[primIters.Count - 1];
                    node.SolverUsed = usedDual ? "Dual+Primal(fallback)" : "PrimalSimplex";
                }
                catch
                {
                    node.Infeasible = true;
                    node.SolverUsed = usedDual ? "Dual then Primal (failed)" : "PrimalSimplex (failed)";
                    infeasible = true;
                    return;
                }
            }

            // Extract solution and objective on original coefficients
            var xvec = ExtractX(T, model.Variables.Count);
            var xdict = new Dictionary<string, double>();
            for (int j = 0; j < model.Variables.Count; j++)
                xdict[model.Variables[j].Name] = xvec[j];

            node.X = xdict;
            node.Objective = EvaluateObjective(baseModel, xdict);
        }

        // ----- Build Iteration 0 from parent's optimal tableau + new bound row -----

        private static double[,] CreateSeededTableau(
    double[,] parentLast,
    List<string> parentHeaders,
    BnBSimplexBound bound,
    int numVars,
    out List<string> newHeaders)
        {
            int pr = parentLast.GetLength(0);
            int pc = parentLast.GetLength(1);
            int parentM = pr - 1;
            int parentRhs = pc - 1;

            // New dims: +1 row, +1 column (the new S/E). Place S/E where RHS used to be.
            int rows = pr + 1;
            int cols = pc + 1;
            int newSlackCol = pc - 1;   // insert BEFORE RHS
            int newRhsCol = cols - 1; // move RHS to last column

            var T = new double[rows, cols];

            // Copy parent's non-RHS columns unchanged (0..pc-2)
            for (int i = 0; i < pr; i++)
            {
                for (int j = 0; j < pc - 1; j++)
                    T[i, j] = parentLast[i, j];

                // Move parent's RHS to the new last column
                T[i, newRhsCol] = parentLast[i, parentRhs];

                // Initialize the inserted S/E column to 0
                T[i, newSlackCol] = 0.0;
            }

            // Objective row: cost of new S/E col is 0
            T[0, newSlackCol] = 0.0;

            // --- Build the new constraint row in ORIGINAL variable space ---
            // x_j <= floor  -> +1 * x_j + S = floor
            // x_j >= ceil   -> -1 * x_j + E = -ceil
            double[] newRow = new double[cols];

            // Decision variable coefficients first
            for (int j = 0; j < numVars; j++) newRow[j] = 0.0;
            newRow[bound.VarIndex] = bound.IsUpper ? 1.0 : -1.0;

            // Existing slack/excess columns (copied area) default to 0; set the new S/E column to 1
            newRow[newSlackCol] = 1.0;

            // RHS
            newRow[newRhsCol] = bound.IsUpper ? bound.Value : -bound.Value;

            // Reduce the new row against the parent's basis:
            // eliminate entries in columns that are basic in the parent tableau.
            // IMPORTANT: map parent's RHS (pc-1) into our new RHS column (newRhsCol).
            for (int j = 0; j < pc - 1; j++) // only old non-RHS columns
            {
                int basicRow = GetBasicRow(parentLast, j);
                if (basicRow != -1 && Math.Abs(newRow[j]) > 1e-12)
                {
                    double factor = newRow[j];
                    for (int k = 0; k < pc - 1; k++)
                        newRow[k] -= factor * parentLast[basicRow, k];

                    // RHS mapping
                    newRow[newRhsCol] -= factor * parentLast[basicRow, parentRhs];
                    // new S/E column stays as set (it wasn't in the parent)
                }
            }

            // Write the reduced new row into the last row of T
            for (int j = 0; j < cols; j++) T[rows - 1, j] = newRow[j];

            // --- Headers: [vars][old S/E][new S/E][RHS] ---
            newHeaders = new List<string>();

            // Vars
            for (int j = 0; j < numVars; j++) newHeaders.Add(parentHeaders[j]);

            // Old slack/excess names (everything between vars and RHS from parent)
            for (int j = numVars; j < parentHeaders.Count - 1; j++)
                newHeaders.Add(parentHeaders[j]);

            // New slack/excess name
            string seName = (bound.IsUpper ? "S" : "E") + (parentM + 1);
            newHeaders.Add(seName);

            // RHS
            newHeaders.Add("RHS");

            return T;
        }

        // Return row index (>=1) if column 'col' is a unit vector among constraint rows; else -1
        private static int GetBasicRow(double[,] T, int col)
        {
            int rows = T.GetLength(0);
            int oneRow = -1;
            for (int i = 1; i < rows; i++)
            {
                double v = T[i, col];
                if (Math.Abs(v - 1.0) <= 1e-9)
                {
                    if (oneRow == -1) oneRow = i; else return -1; // more than one 1 -> not basic
                }
                else if (Math.Abs(v) > 1e-9)
                {
                    return -1; // nonzero outside -> not basic
                }
            }
            return oneRow;
        }

        // ----- Primal simplex loop -----

        private List<double[,]> RunPrimalSimplexOnTableau(double[,] start, Objective obj)
        {
            var iters = new List<double[,]>();
            var T = Clone(start);
            iters.Add(Clone(T));

            while (true)
            {
                int pivotCol = SelectPrimalPivotColumn(T, obj);
                if (pivotCol == -1) break;

                int pivotRow = SelectPrimalPivotRow(T, pivotCol);
                if (pivotRow == -1)
                    throw new InvalidOperationException("Unbounded primal during BnB node optimization.");

                Pivot(T, pivotRow, pivotCol);
                iters.Add(Clone(T));

                if (iters.Count > 10_000)
                    throw new InvalidOperationException("Too many primal iterations (possible cycling).");
            }
            return iters;
        }

        private static int SelectPrimalPivotColumn(double[,] T, Objective obj)
        {
            int cols = T.GetLength(1);
            int last = cols - 1;
            int bestCol = -1;
            double bestVal = 0.0;

            for (int j = 0; j < last; j++)
            {
                double v = T[0, j];
                if (obj == Objective.Maximize)
                {
                    if (v < bestVal - 1e-12 || (bestCol == -1 && v < -1e-12)) { bestVal = v; bestCol = j; }
                }
                else
                {
                    if (v > bestVal + 1e-12 || (bestCol == -1 && v > 1e-12)) { bestVal = v; bestCol = j; }
                }
            }
            if (bestCol == -1) return -1;
            if (obj == Objective.Maximize && T[0, bestCol] >= -1e-12) return -1;
            if (obj == Objective.Minimize && T[0, bestCol] <= 1e-12) return -1;
            return bestCol;
        }

        private static int SelectPrimalPivotRow(double[,] T, int pivotCol)
        {
            int rows = T.GetLength(0);
            int rhs = T.GetLength(1) - 1;
            int bestRow = -1;
            double bestRatio = double.PositiveInfinity;
            for (int i = 1; i < rows; i++)
            {
                double aij = T[i, pivotCol];
                if (aij > 1e-12)
                {
                    double ratio = T[i, rhs] / aij;
                    if (ratio >= -1e-12 && ratio < bestRatio - 1e-12)
                    {
                        bestRatio = ratio; bestRow = i;
                    }
                }
            }
            return bestRow;
        }

        private static void Pivot(double[,] T, int pr, int pc)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            double piv = T[pr, pc];
            if (Math.Abs(piv) < 1e-15) throw new InvalidOperationException("Zero pivot.");

            for (int j = 0; j < cols; j++) T[pr, j] /= piv;

            for (int i = 0; i < rows; i++)
            {
                if (i == pr) continue;
                double factor = T[i, pc];
                if (Math.Abs(factor) < 1e-15) continue;
                for (int j = 0; j < cols; j++) T[i, j] -= factor * T[pr, j];
            }
        }

        // ----- Output helpers -----

        private static string NodeStatus(LinearModel baseModel, BnBSimplexNode node)
        {
            if (node.Infeasible) return "Infeasible";
            if (IsIntegerFeasible(node.X, baseModel)) return "Candidate";
            return "Branched";
        }

        private static void PrettyPrintNode(BnBSimplexNode node, List<double[,]> iterations, List<string> headers, string status)
        {
            Console.WriteLine("---- Node " + node.Label + " | " + status + " ----");

            if (iterations == null || iterations.Count == 0)
            {
                Console.WriteLine("(no tableau)");
            }
            else
            {
                for (int k = 0; k < iterations.Count; k++)
                {
                    Console.WriteLine("-- Iteration " + k + " --");
                    Console.WriteLine(HeaderRow(headers));
                    PrintTableauToConsole(iterations[k]);
                    Console.WriteLine();
                }
            }

            if (!node.Infeasible)
                Console.WriteLine("z = " + FormatNumber2OrInt(node.Objective) + " (" + node.SolverUsed + ")\n");
            else
                Console.WriteLine("z = INF (" + node.SolverUsed + ")\n");
        }

        private static void AppendNodeToUnifiedFile(string path, BnBSimplexNode node, List<double[,]> iterations, List<string> headers, string status)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("---- Node " + node.Label + " | " + status + " ----");

                if (iterations == null || iterations.Count == 0)
                {
                    sb.AppendLine("(no tableau)");
                }
                else
                {
                    for (int k = 0; k < iterations.Count; k++)
                    {
                        sb.AppendLine("-- Iteration " + k + " --");
                        sb.AppendLine(HeaderRow(headers));
                        var T = iterations[k];
                        int r = T.GetLength(0), c = T.GetLength(1);
                        for (int i = 0; i < r; i++)
                        {
                            for (int j = 0; j < c; j++) sb.Append(FormatCell(T[i, j]));
                            sb.AppendLine();
                        }
                        sb.AppendLine();
                    }
                }

                if (!node.Infeasible) sb.AppendLine("z = " + FormatNumber2OrInt(node.Objective) + " (" + node.SolverUsed + ")");
                else sb.AppendLine("z = INF (" + node.SolverUsed + ")");
                if (node.Bounds.Count > 0) sb.AppendLine("Bounds: " + string.Join(", ", node.Bounds.Select(b => b.ToString())));
                sb.AppendLine();

                File.AppendAllText(path, sb.ToString());
            }
            catch { /* ignore IO errors */ }
        }

        private static string HeaderRow(List<string> headers)
        {
            if (headers == null || headers.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < headers.Count; i++) sb.Append(FormatHeaderCell(headers[i]));
            return sb.ToString();
        }

        private static string FormatHeaderCell(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "?";
            if (name.Length < 8) return name.PadLeft(8, ' ');
            return " " + name + " ";
        }

        private static void PrintTableauToConsole(double[,] T)
        {
            int r = T.GetLength(0);
            int c = T.GetLength(1);
            for (int i = 0; i < r; i++)
            {
                var line = new StringBuilder();
                for (int j = 0; j < c; j++) line.Append(FormatCell(T[i, j]));
                Console.WriteLine(line.ToString());
            }
        }

        // integer if near-integer, otherwise 2 decimals; fix -0.00 => 0
        private static string FormatCell(double v)
        {
            double r2 = Math.Round(v, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(r2) < 0.005) r2 = 0;

            string s = (Math.Abs(r2 - Math.Round(r2)) <= 1e-9)
                ? Math.Round(r2).ToString("0")
                : r2.ToString("0.00");

            if (s.Length < 8) s = s.PadLeft(8, ' ');
            else s = " " + s + " ";
            return s;
        }

        private static string FormatNumber2OrInt(double v)
        {
            double r2 = Math.Round(v, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(r2) < 0.005) r2 = 0;
            return (Math.Abs(r2 - Math.Round(r2)) <= 1e-9)
                ? Math.Round(r2).ToString("0")
                : r2.ToString("0.00");
        }

        // ----- Model/tableau helpers -----

        private static bool HasNegativeRHS(double[,] T)
        {
            int rows = T.GetLength(0);
            int rhs = T.GetLength(1) - 1;
            for (int i = 1; i < rows; i++) if (T[i, rhs] < -1e-12) return true;
            return false;
        }

        private static bool Better(Objective obj, double a, double b)
        {
            if (obj == Objective.Maximize) return a > b + 1e-12;
            return a < b - 1e-12;
        }

        private static int ChooseBranchVariable(LinearModel model, Dictionary<string, double> x)
        {
            int bestIdx = -1; double bestFrac = -1.0;
            for (int j = 0; j < model.Variables.Count; j++)
            {
                var v = model.Variables[j];
                if (v.Type != VarType.Integer && v.Type != VarType.Binary) continue;

                double val = x.ContainsKey(v.Name) ? x[v.Name] : 0.0;
                double frac = Math.Abs(val - Math.Round(val));
                if (frac > 1e-7 && frac > bestFrac)
                {
                    bestFrac = frac; bestIdx = j;
                }
            }
            return bestIdx;
        }

        private static bool IsIntegerFeasible(Dictionary<string, double> x, LinearModel model)
        {
            for (int j = 0; j < model.Variables.Count; j++)
            {
                var v = model.Variables[j];
                if (v.Type == VarType.Integer || v.Type == VarType.Binary)
                {
                    double val = x.ContainsKey(v.Name) ? x[v.Name] : 0.0;
                    if (Math.Abs(val - Math.Round(val)) > 1e-6) return false;
                    if (v.Type == VarType.Binary && (val < -1e-9 || val > 1 + 1e-9)) return false;
                }
            }
            return true;
        }

        
        // Normalize to ≤ and append branching bounds. Also emit row types (S/E) for header naming when building fresh.
        private static LinearModel BuildNodeModel(LinearModel baseModel, List<BnBSimplexBound> bounds, out List<char> rowTypes)
        {
            var m = new LinearModel { Obj = baseModel.Obj };
            for (int i = 0; i < baseModel.Variables.Count; i++)
            {
                var v = baseModel.Variables[i];
                m.Variables.Add(new Variable(v.Name, v.Coefficient, v.Type));
            }

            rowTypes = new List<char>();

            for (int i = 0; i < baseModel.Constraints.Count; i++)
            {
                var c = baseModel.Constraints[i];
                if (c.Rel == Relation.LessThanOrEqual)
                {
                    m.Constraints.Add(new Constraint(new List<double>(c.Coefficients), Relation.LessThanOrEqual, c.RHS));
                    rowTypes.Add('S');
                }
                else if (c.Rel == Relation.GreaterThanOrEqual)
                {
                    var neg = c.Coefficients.Select(x => -x).ToList();
                    m.Constraints.Add(new Constraint(neg, Relation.LessThanOrEqual, -c.RHS));
                    rowTypes.Add('E');
                }
                else // Equal
                {
                    m.Constraints.Add(new Constraint(new List<double>(c.Coefficients), Relation.LessThanOrEqual, c.RHS));
                    rowTypes.Add('S');
                    m.Constraints.Add(new Constraint(c.Coefficients.Select(x => -x).ToList(), Relation.LessThanOrEqual, -c.RHS));
                    rowTypes.Add('E');
                }
            }

            for (int i = 0; i < bounds.Count; i++)
            {
                var b = bounds[i];
                var coeffs = new List<double>(new double[m.Variables.Count]);
                if (b.IsUpper)
                {
                    coeffs[b.VarIndex] = 1.0; // x_j <= value
                    m.Constraints.Add(new Constraint(coeffs, Relation.LessThanOrEqual, b.Value));
                    rowTypes.Add('S');
                }
                else
                {
                    coeffs[b.VarIndex] = -1.0; // -x_j <= -value (x_j >= value)
                    m.Constraints.Add(new Constraint(coeffs, Relation.LessThanOrEqual, -b.Value));
                    rowTypes.Add('E');
                }
            }

            return m;
        }

        // Build a fresh tableau and headers (decision vars + S#/E# per row + RHS).
     
        private static double[,] BuildTableau(LinearModel model, List<char> rowTypes, out List<string> headers)
        {
            int m = model.Constraints.Count;
            int n = model.Variables.Count;
            int rows = m + 1;
            int cols = n + m + 1;

            double[,] T = new double[rows, cols];

            // Objective
            for (int j = 0; j < n; j++)
                T[0, j] = (model.Obj == Objective.Maximize ? -1 : 1) * model.Variables[j].Coefficient;

            // Constraints
            for (int i = 0; i < m; i++)
            {
                var c = model.Constraints[i];
                for (int j = 0; j < n; j++) T[i + 1, j] = c.Coefficients[j];
                T[i + 1, n + i] = 1.0;
                T[i + 1, cols - 1] = c.RHS;
            }

            // Headers
            headers = new List<string>();
            for (int j = 0; j < n; j++) headers.Add(model.Variables[j].Name);
            for (int i = 0; i < m; i++)
            {
                string name = (rowTypes != null && rowTypes[i] == 'E') ? "E" + (i + 1) : "S" + (i + 1);
                headers.Add(name);
            }
            headers.Add("RHS");

            return T;
        }

        private static double[] ExtractX(double[,] T, int numVars)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            int rhs = cols - 1;
            var x = new double[numVars];

            for (int j = 0; j < numVars; j++)
            {
                int oneRow = -1; bool isBasic = true;
                for (int i = 1; i < rows; i++)
                {
                    double v = T[i, j];
                    if (Math.Abs(v - 1.0) <= 1e-8)
                    {
                        if (oneRow == -1) oneRow = i; else { isBasic = false; break; }
                    }
                    else if (Math.Abs(v) > 1e-8) { isBasic = false; break; }
                }
                if (isBasic && oneRow != -1)
                {
                    x[j] = T[oneRow, rhs];
                    if (Math.Abs(x[j]) < 1e-12) x[j] = 0;
                }
                else x[j] = 0;
            }
            return x;
        }

        private static double EvaluateObjective(LinearModel baseModel, Dictionary<string, double> x)
        {
            double z = 0;
            for (int j = 0; j < baseModel.Variables.Count; j++)
            {
                var v = baseModel.Variables[j];
                double val = x.ContainsKey(v.Name) ? x[v.Name] : 0.0;
                z += v.Coefficient * val;
            }
            return z;
        }

        private static double[,] Clone(double[,] A) { return (double[,])A.Clone(); }
    }
}