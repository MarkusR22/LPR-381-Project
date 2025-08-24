using System;
using System.Collections.Generic;
using System.Linq;
using LPR_381_Project.Models;
using System.IO;
using System.Text;

namespace LPR_381_Project.Solvers
{
    /// <summary>
    /// Branch-and-Bound over LP relaxations using your existing Primal & Dual Simplex solvers.
    /// For each node: (1) Dual Simplex if any RHS < 0 to repair feasibility, then (2) Primal Simplex to optimize.
    /// - Writes ALL nodes to one file: Output/branch_and_bound_nodes.txt
    /// - Prints each node’s final (optimal) tableau + status to the Console
    /// - Integrality enforced only for Integer/Binary variables
    /// </summary>
    public class BranchAndBoundSimplex
    {
        private const double TOL = 1e-9;               // numeric noise tolerance
        private const int MAX_NODES = 10_000;          // safety

        public class BranchAndBoundResult
        {
            public Dictionary<string, double> BestX { get; set; } = new Dictionary<string, double>();
            public double BestObjective { get; set; } = double.NegativeInfinity;
            public bool Feasible { get; set; }
            public int NodesExplored { get; set; }
            public string Log { get; set; } = string.Empty;
        }

        private class Node
        {
            public string Label;                              // e.g., "Root", "L", "R.L", etc.
            public List<Bound> Bounds = new List<Bound>();    // branching bounds accumulated from root
            public int Depth;

            // Relaxation info
            public Dictionary<string, double> X = new Dictionary<string, double>();
            public double Objective;                          // objective value of LP relaxation
            public bool IsInteger;                            // current relaxation is integer-feasible for integer/binary vars
            public bool Infeasible;                           // LP infeasible/unbounded
            public string SolverUsed;                         // "Dual+Primal", "PrimalSimplex", etc.

            public override string ToString()
            {
                var b = Bounds.Count == 0 ? "(none)" : string.Join(", ", Bounds.Select(x => x.ToString()));
                return $"Node[{Label}] depth={Depth}, bounds={b}, obj={(Infeasible ? "INF" : Objective.ToString("0.###"))}, integer={IsInteger}, solver={SolverUsed}";
            }
        }

        private class Bound
        {
            public int VarIndex; // 0-based in model.Variables list
            public bool IsUpper; // true => x_j <= value; false => x_j >= value
            public double Value;
            public override string ToString() => IsUpper ? $"x{VarIndex + 1} <= {Value}" : $"x{VarIndex + 1} >= {Value}";
        }

        /// <summary>
        /// Solve integer program by Branch-and-Bound. The model can contain <=, >=, = constraints.
        /// Variable integrality is read from Variable.Type (Integer/Binary).
        /// </summary>
        public BranchAndBoundResult Solve(LinearModel baseModel)
        {
            if (baseModel == null) throw new ArgumentNullException(nameof(baseModel));
            if (baseModel.Variables == null || baseModel.Variables.Count == 0)
                throw new ArgumentException("Model has no variables.");

            // PREP: ensure Binary variables get x <= 1 bound in the root
            var root = new Node { Label = "Root", Depth = 0 };
            for (int j = 0; j < baseModel.Variables.Count; j++)
            {
                if (baseModel.Variables[j].Type == VarType.Binary)
                    root.Bounds.Add(new Bound { VarIndex = j, IsUpper = true, Value = 1.0 });
                // (We assume non-negativity for Positive/Integer/Binary; Negative vars are not handled.)
            }

            var best = new BranchAndBoundResult();

            // Best-first search list
            var pending = new List<Node> { root };

            int explored = 0;
            var sbLog = new StringBuilder();

            // Create/overwrite the single unified output file
            Directory.CreateDirectory("Output");
            var unifiedPath = Path.Combine("Output", "branch_and_bound_nodes.txt");
            File.WriteAllText(unifiedPath,
                $"==== Branch & Bound Nodes ===={Environment.NewLine}" +
                $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"Objective: {baseModel.Obj}{Environment.NewLine}{Environment.NewLine}");

            while (pending.Count > 0 && explored < MAX_NODES)
            {
                // Best-first: choose node with best LP bound (max or min accordingly)
                pending = SortByBound(pending, baseModel.Obj);
                var node = pending[0];
                pending.RemoveAt(0);

                explored++;

                // Solve relaxation for this node
                SolveNodeLP(baseModel, node, out List<double[,]> iterations, out bool infeasible);
                node.Infeasible = infeasible;

                // Determine status & pretty-print the final/optimal tableau for THIS node
                var lastTab = (iterations != null && iterations.Count > 0) ? iterations[iterations.Count - 1] : null;
                var status = NodeStatus(baseModel, node);
                PrettyPrintNode(node, lastTab, status);                 // Console print
                AppendNodeBlock(unifiedPath, node, lastTab, status);    // Unified file

                // Log (also used by caller)
                sbLog.AppendLine(node.ToString() + $" status={status}");

                if (node.Infeasible) continue; // prune

                // Bound pruning
                if (baseModel.Obj == Objective.Maximize)
                {
                    if (node.Objective + 1e-12 < best.BestObjective) continue; // cannot beat best
                }
                else // Minimize
                {
                    if (best.Feasible && node.Objective - 1e-12 > best.BestObjective) continue;
                }

                // Check integrality
                node.IsInteger = IsIntegerFeasible(node.X, baseModel);
                if (node.IsInteger)
                {
                    // Update incumbent
                    if (!best.Feasible || Better(baseModel.Obj, node.Objective, best.BestObjective))
                    {
                        best.Feasible = true;
                        best.BestObjective = node.Objective;
                        best.BestX = new Dictionary<string, double>(node.X);
                    }
                    continue;
                }

                // Choose branching variable: integer/binary only; pick largest fractional part
                int branchIndex = ChooseBranchVariable(baseModel, node.X);
                if (branchIndex < 0) { continue; }

                double xval = node.X[baseModel.Variables[branchIndex].Name];
                double floorV = Math.Floor(xval + 1e-12);
                double ceilV = Math.Ceiling(xval - 1e-12);

                // Left child: x_j <= floor
                var left = new Node
                {
                    Label = node.Label == "Root" ? "L" : node.Label + ".L",
                    Depth = node.Depth + 1,
                    Bounds = new List<Bound>(node.Bounds)
                    { new Bound { VarIndex = branchIndex, IsUpper = true, Value = floorV } }
                };

                // Right child: x_j >= ceil
                var right = new Node
                {
                    Label = node.Label == "Root" ? "R" : node.Label + ".R",
                    Depth = node.Depth + 1,
                    Bounds = new List<Bound>(node.Bounds)
                    { new Bound { VarIndex = branchIndex, IsUpper = false, Value = ceilV } }
                };

                // Add to pending set
                pending.Add(left);
                pending.Add(right);
            }

            best.NodesExplored = explored;
            best.Log = sbLog.ToString();
            return best;
        }

        private static List<Node> SortByBound(List<Node> nodes, Objective obj)
        {
            // nodes without an objective yet stay in FIFO order
            return obj == Objective.Maximize
                ? nodes.OrderByDescending(n => n.Objective).ToList()
                : nodes.OrderBy(n => n.Objective == 0 ? double.PositiveInfinity : n.Objective).ToList();
        }

        private void SolveNodeLP(LinearModel baseModel, Node node, out List<double[,]> iterations, out bool infeasible)
        {
            iterations = new List<double[,]>();
            infeasible = false;

            // Build a normalized (<= only) model and attach node bounds
            var model = BuildNodeModel(baseModel, node.Bounds);

            // Build a tableau and repair feasibility with Dual if needed, then optimize with Primal
            double[,] T = BuildTableau(model);
            iterations.Add(Clone(T));

            bool usedDual = false;
            try
            {
                if (HasNegativeRHS(T))
                {
                    var dual = new DualSimplex();
                    var dualIters = dual.Solve(T);
                    if (dualIters != null && dualIters.Count > 0)
                    {
                        foreach (var tab in dualIters.Skip(1)) iterations.Add(Clone(tab));
                        T = dualIters[dualIters.Count - 1];
                        usedDual = true;
                    }
                }
            }
            catch
            {
                node.Infeasible = true;
                node.SolverUsed = "DualSimplex (failed)";
                infeasible = true;              // critical fix
                return;
            }

            // Now run a Primal Simplex phase from the (possibly dual-repaired) tableau until optimality
            try
            {
                var primalIters = RunPrimalSimplexOnTableau(T, model.Obj);
                if (primalIters != null && primalIters.Count > 0)
                {
                    foreach (var tab in primalIters.Skip(1)) iterations.Add(Clone(tab));
                    T = primalIters[primalIters.Count - 1];
                }
                node.SolverUsed = usedDual ? "Dual+Primal" : "PrimalSimplex";
            }
            catch
            {
                // If our internal primal fails, try user's Primal solver as a fallback from the model
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
                    infeasible = true;          // critical fix
                    return;
                }
            }

            // Extract solution and objective from last tableau safely
            var last = T;
            var xvec = ExtractX(last, model.Variables.Count);
            var xdict = new Dictionary<string, double>();
            for (int j = 0; j < model.Variables.Count; j++)
                xdict[model.Variables[j].Name] = xvec[j];

            node.X = xdict;
            node.Objective = EvaluateObjective(baseModel, xdict); // evaluate on original c's
        }

        // Run a standard primal simplex on a canonical tableau until optimal (max: no negatives in z-row; min: no positives)
        private List<double[,]> RunPrimalSimplexOnTableau(double[,] start, Objective obj)
        {
            var iters = new List<double[,]>();
            var T = Clone(start);
            iters.Add(Clone(T));

            int cols = T.GetLength(1);

            while (true)
            {
                int pivotCol = SelectPrimalPivotColumn(T, obj);
                if (pivotCol == -1) break; // optimal

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

            if (bestCol == -1) return -1; // optimal
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

        private static string NodeStatus(LinearModel baseModel, Node node)
        {
            if (node.Infeasible) return "Infeasible";
            if (IsIntegerFeasible(node.X, baseModel)) return "Candidate";
            return "Branched";
        }

        private static void PrettyPrintNode(Node node, double[,] tableau, string status)
        {
            Console.WriteLine();
            Console.WriteLine($"---- Node {node.Label} | {status} ----");
            if (tableau == null)
            {
                Console.WriteLine("(no tableau)");
            }
            else
            {
                PrintTableauToConsole(tableau);
            }
            if (!node.Infeasible)
                Console.WriteLine($"z = {node.Objective:0.###} ({node.SolverUsed})");
            else
                Console.WriteLine($"z = INF ({node.SolverUsed})");
        }

        private static void PrintTableauToConsole(double[,] T)
        {
            int r = T.GetLength(0);
            int c = T.GetLength(1);
            for (int i = 0; i < r; i++)
            {
                var line = new StringBuilder();
                for (int j = 0; j < c; j++)
                {
                    string cell = FormatCell(T[i, j]);
                    line.Append(cell);
                }
                Console.WriteLine(line.ToString());
            }
        }

        private static string FormatCell(double v)
        {
            // integer-looking numbers print without decimals; otherwise compact
            double round = Math.Round(v);
            string s = Math.Abs(v - round) <= 1e-9 ? round.ToString("0") : v.ToString("0.######");
            // pad to fixed width for alignment (like your screenshot)
            if (s.Length < 8) s = s.PadLeft(8, ' ');
            else s = " " + s + " ";
            return s;
        }

        private static bool HasNegativeRHS(double[,] T)
        {
            int rows = T.GetLength(0);
            int rhs = T.GetLength(1) - 1;
            for (int i = 1; i < rows; i++) if (T[i, rhs] < -1e-12) return true;
            return false;
        }

        private static bool Better(Objective obj, double a, double b)
            => obj == Objective.Maximize ? a > b + 1e-12 : a < b - 1e-12;

        private static int ChooseBranchVariable(LinearModel model, Dictionary<string, double> x)
        {
            int bestIdx = -1; double bestFrac = -1.0;
            for (int j = 0; j < model.Variables.Count; j++)
            {
                var v = model.Variables[j];
                if (v.Type != VarType.Integer && v.Type != VarType.Binary) continue; // only branch on integer/binary

                double val = x.TryGetValue(v.Name, out var tmp) ? tmp : 0.0;
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
                    double val = x.TryGetValue(v.Name, out var tmp) ? tmp : 0.0;
                    if (Math.Abs(val - Math.Round(val)) > 1e-6) return false;
                    if (v.Type == VarType.Binary && (val < -1e-9 || val > 1 + 1e-9)) return false;
                }
            }
            return true;
        }

        // Build a fresh model for a node: 1) normalize original constraints to <=, 2) append branching bounds
        private static LinearModel BuildNodeModel(LinearModel baseModel, List<Bound> bounds)
        {
            var m = new LinearModel { Obj = baseModel.Obj };
            foreach (var v in baseModel.Variables)
                m.Variables.Add(new Variable(v.Name, v.Coefficient, v.Type));

            foreach (var c in baseModel.Constraints)
            {
                switch (c.Rel)
                {
                    case Relation.LessThanOrEqual:
                        m.Constraints.Add(new Constraint(new List<double>(c.Coefficients), Relation.LessThanOrEqual, c.RHS));
                        break;
                    case Relation.GreaterThanOrEqual:
                        var neg = c.Coefficients.Select(x => -x).ToList();
                        m.Constraints.Add(new Constraint(neg, Relation.LessThanOrEqual, -c.RHS));
                        break;
                    case Relation.Equal:
                        m.Constraints.Add(new Constraint(new List<double>(c.Coefficients), Relation.LessThanOrEqual, c.RHS));
                        m.Constraints.Add(new Constraint(c.Coefficients.Select(x => -x).ToList(), Relation.LessThanOrEqual, -c.RHS));
                        break;
                }
            }

            foreach (var b in bounds)
            {
                var coeffs = new List<double>(new double[m.Variables.Count]);
                if (b.IsUpper)
                {
                    coeffs[b.VarIndex] = 1.0; // x_j <= value
                    m.Constraints.Add(new Constraint(coeffs, Relation.LessThanOrEqual, b.Value));
                }
                else
                {
                    coeffs[b.VarIndex] = -1.0; // -x_j <= -value
                    m.Constraints.Add(new Constraint(coeffs, Relation.LessThanOrEqual, -b.Value));
                }
            }

            return m;
        }

        // Build initial tableau consistent with PrimalSimplexSolver.CreateTableau
        private static double[,] BuildTableau(LinearModel model)
        {
            int numConstraints = model.Constraints.Count;
            int numVars = model.Variables.Count;
            int rows = numConstraints + 1;
            int cols = numVars + numConstraints + 1; // slacks + RHS

            double[,] T = new double[rows, cols];

            for (int j = 0; j < numVars; j++)
                T[0, j] = (model.Obj == Objective.Maximize ? -1 : 1) * model.Variables[j].Coefficient;

            for (int i = 0; i < numConstraints; i++)
            {
                var c = model.Constraints[i];
                for (int j = 0; j < numVars; j++) T[i + 1, j] = c.Coefficients[j];
                T[i + 1, numVars + i] = 1.0; // slack
                T[i + 1, cols - 1] = c.RHS;
            }

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
                    else if (Math.Abs(v) > 1e-8)
                    {
                        isBasic = false; break;
                    }
                }

                if (isBasic && oneRow != -1)
                {
                    x[j] = T[oneRow, rhs];
                    if (Math.Abs(x[j]) < 1e-12) x[j] = 0; // clean noise
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
                double val = x.TryGetValue(v.Name, out var tmp) ? tmp : 0.0;
                z += v.Coefficient * val;
            }
            return z;
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static double[,] Clone(double[,] A)
        {
            int r = A.GetLength(0), c = A.GetLength(1);
            var B = new double[r, c];
            for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) B[i, j] = A[i, j];
            return B;
        }

        private static void AppendNodeBlock(string path, Node node, double[,] tableau, string status)
        {
            try
            {
                var sb = new StringBuilder();

                sb.AppendLine($"---- Node {node.Label} | {status} ----");

                if (tableau == null)
                {
                    sb.AppendLine("(no tableau)");
                }
                else
                {
                    int rows = tableau.GetLength(0);
                    int cols = tableau.GetLength(1);

                    for (int i = 0; i < rows; i++)
                    {
                        var line = new StringBuilder();
                        for (int j = 0; j < cols; j++)
                        {
                            double v = tableau[i, j];
                            double r = Math.Round(v);
                            string cell = (Math.Abs(v - r) <= 1e-9 ? r.ToString("0") : v.ToString("0.######")).PadLeft(8);
                            line.Append(cell);
                        }
                        sb.AppendLine(line.ToString());
                    }
                }

                if (!node.Infeasible)
                    sb.AppendLine($"z = {node.Objective:0.###} ({node.SolverUsed})");
                else
                    sb.AppendLine($"z = INF ({node.SolverUsed})");

                if (node.Bounds != null && node.Bounds.Count > 0)
                    sb.AppendLine("Bounds: " + string.Join(", ", node.Bounds.Select(b => b.ToString())));

                sb.AppendLine(); // blank line between nodes

                File.AppendAllText(path, sb.ToString());
            }
            catch
            {
                // Swallow file append errors so they don't crash the solver
            }
        }
    }
}
