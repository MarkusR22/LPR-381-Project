using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LPR_381_Project.Models;

namespace LPR_381_Project.Solvers
{
    class BnBKnapsack
    {
        public int capacity;
        public int[] z;                 // profits (objective coefficients)
        public int[] c;                 // weights (constraint coefficients)
        public List<int> rank;
        public List<KnapsackNode> iterations;
        public Dictionary<int, int> weights;

        // validity state when loading from LinearModel
        private bool _modelValid = true;
        private string _modelInvalidReason = null;

        // Build directly from a LinearModel (no exceptions)
        public BnBKnapsack(LinearModel model)
        {
            LoadFromModel(model); // sets _modelValid / _modelInvalidReason
        }

        // Keep the original ctor (arrays)
        public BnBKnapsack(int capacity, int[] z, int[] c)
        {
            this.capacity = capacity;
            this.z = z;
            this.c = c;
        }

        // Convenience: load from model then solve (no exceptions)
        public List<KnapsackNode> Solve(LinearModel model)
        {
            LoadFromModel(model);
            return Solve();
        }

        // ---- Validation (no throws) ----
        private static bool ValidateModelForKnapsack(LinearModel model, out string reason)
        {
            if (model == null) { reason = "Model is null."; return false; }
            if (model.Variables == null || model.Variables.Count == 0)
            { reason = "Knapsack model needs variables."; return false; }

            if (model.Obj != Objective.Maximize)
            { reason = "Knapsack expects a Maximize objective."; return false; }

            if (model.Constraints == null || model.Constraints.Count != 1)
            { reason = "Knapsack requires exactly one ≤ capacity constraint."; return false; }

            var con = model.Constraints[0];
            if (con.Rel != Relation.LessThanOrEqual)
            { reason = "Capacity constraint must be ≤."; return false; }

            if (con.Coefficients == null || con.Coefficients.Count != model.Variables.Count)
            { reason = "Capacity constraint must have one weight coefficient per decision variable."; return false; }

            for (int i = 0; i < model.Variables.Count; i++)
            {
                if (model.Variables[i].Type != VarType.Binary)
                {
                    reason = $"All decision variables must be Binary. Variable '{model.Variables[i].Name}' is {model.Variables[i].Type}.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        // Extract capacity/weights/profits; never throws
        private void LoadFromModel(LinearModel model)
        {
            _modelValid = ValidateModelForKnapsack(model, out _modelInvalidReason);
            if (!_modelValid)
            {
                // leave fields unset; Solve() will print and return cleanly
                z = null; c = null; capacity = 0;
                return;
            }

            int n = model.Variables.Count;

            // profits z: from objective coefficients
            z = new int[n];
            for (int i = 0; i < n; i++)
                z[i] = (int)Math.Round(model.Variables[i].Coefficient);

            // single <= capacity constraint
            var capCon = model.Constraints[0];

            // weights c
            c = new int[n];
            for (int i = 0; i < n; i++)
            {
                c[i] = (int)Math.Round(capCon.Coefficients[i]);
                if (c[i] < 0) { _modelValid = false; _modelInvalidReason = "Knapsack weights must be non-negative."; return; }
            }

            capacity = (int)Math.Round(capCon.RHS);
            if (capacity < 0) { _modelValid = false; _modelInvalidReason = "Knapsack capacity must be non-negative."; return; }
        }

        public List<KnapsackNode> Solve()
        {
            // If constructed with a LinearModel and it wasn't valid, exit gracefully.
            if (!_modelValid || z == null || c == null)
            {
                PrintNotApplicableMessage();
                return new List<KnapsackNode>();
            }

            // prep rank and weights
            rank = DetermineRank();
            weights = new Dictionary<int, int>();
            for (int i = 0; i < c.Length; i++)
                weights[i + 1] = c[i];

            iterations = new List<KnapsackNode>();

            // Start with an origin node (no fixed decisions yet)
            var origin = new KnapsackNode("0", "None", "Unsolved", new Dictionary<int, int>());
            iterations.Add(origin);

            // BFS-like loop that grows as we branch
            for (int i = 0; i < iterations.Count; i++)
            {
                var node = iterations[i];

                if (node.Status == "Unsolved")
                {
                    // Solve LP at this node
                    SolveNode(node, capacity);
                }

                if (node.Status == "Unbranched")
                {
                    // Fractional found -> branch
                    Branch(node);
                }
            }

            return iterations;
        }

        private void PrintNotApplicableMessage()
        {
            Console.WriteLine("== Knapsack not applicable for this model ==");
            Console.WriteLine("Requirements:");
            Console.WriteLine(" - Maximize objective");
            Console.WriteLine(" - Exactly one capacity constraint of the form  Σ w_i x_i ≤ C");
            Console.WriteLine(" - All decision variables must be Binary (0/1)");
            if (!string.IsNullOrWhiteSpace(_modelInvalidReason))
                Console.WriteLine("Details: " + _modelInvalidReason);
            Console.WriteLine("No knapsack solved for this input.\n");
        }

        public List<int> DetermineRank()
        {
            var orderedRank = new List<int>();
            var ratios = new Dictionary<int, double>();

            for (int i = 0; i < z.Length; i++)
            {
                if (c[i] == 0)
                    ratios.Add(i + 1, double.PositiveInfinity);
                else
                    ratios.Add(i + 1, (double)z[i] / c[i]);
            }

            foreach (var kvp in ratios.OrderByDescending(kvp => kvp.Value))
                orderedRank.Add(kvp.Key);

            return orderedRank;
        }

        public KnapsackNode SolveNode(KnapsackNode node, int cap)
        {
            if (rank == null || rank.Count == 0)
                rank = DetermineRank();

            node.RankSnapshot = new List<int>(rank);

            if (weights == null || weights.Count == 0)
            {
                weights = new Dictionary<int, int>();
                for (int i = 0; i < c.Length; i++)
                    weights[i + 1] = c[i];
            }

            if (node.Order == null)
                node.Order = new Dictionary<int, int>();

            // reset X-values
            node.XValues = new Dictionary<int, double>();
            node.FractionalVar = null;

            // apply fixed decisions on this node
            foreach (var kvp in node.Order)
            {
                int varIdx = kvp.Key;
                int fixedVal = kvp.Value;

                if (fixedVal == 1)
                {
                    cap -= weights[varIdx];
                    node.XValues[varIdx] = 1.0;
                    if (cap < 0)
                    {
                        node.Status = "Infeasible";
                        for (int v = 1; v <= z.Length; v++)
                            if (!node.XValues.ContainsKey(v))
                                node.XValues[v] = 0.0;

                        node.Objective = 0.0;
                        node.WeightUsed = 0.0;
                        return node;
                    }
                }
                else
                {
                    node.XValues[varIdx] = 0.0;
                }
            }

            var remaining = rank.Where(v => !node.Order.ContainsKey(v)).ToList();

            // greedy fill by rank
            foreach (var v in remaining)
            {
                if (cap <= 0)
                {
                    node.XValues[v] = 0.0;
                    continue;
                }

                int w = weights[v];

                if (w <= cap)
                {
                    node.XValues[v] = 1.0;
                    cap -= w;
                }
                else
                {
                    node.XValues[v] = (double)cap / w;
                    cap = 0;

                    foreach (var u in remaining)
                        if (!node.XValues.ContainsKey(u))
                            node.XValues[u] = 0.0;

                    for (int idx = 1; idx <= z.Length; idx++)
                        if (!node.XValues.ContainsKey(idx))
                            node.XValues[idx] = 0.0;

                    node.FractionalVar = v;
                    node.Objective = ComputeObjective(node);
                    node.WeightUsed = ComputeWeight(node);
                    node.Status = "Unbranched";
                    return node;
                }
            }

            // no fractional -> candidate
            for (int idx = 1; idx <= z.Length; idx++)
                if (!node.XValues.ContainsKey(idx))
                    node.XValues[idx] = 0.0;

            node.Objective = ComputeObjective(node);
            node.WeightUsed = ComputeWeight(node);
            node.Status = "Candidate";
            return node;
        }

        public void Branch(KnapsackNode node)
        {
            if (node.Status != "Unbranched") return;

            int j = node.FractionalVar ?? 0;
            if (j == 0)
            {
                foreach (var v in rank)
                {
                    if (!node.XValues.TryGetValue(v, out var x)) continue;
                    if (x > 0.0 && x < 1.0) { j = v; break; }
                }
                if (j == 0) { node.Status = "Candidate"; return; }
            }

            // drop leading "0." for first-level children
            var leftOrder = new Dictionary<int, int>(node.Order) { [j] = 0 };
            var rightOrder = new Dictionary<int, int>(node.Order) { [j] = 1 };

            string leftName = node.Name == "0" ? "1" : node.Name + ".1";
            string rightName = node.Name == "0" ? "2" : node.Name + ".2";

            var left = new KnapsackNode(leftName, node.Name, "Unsolved", leftOrder);
            var right = new KnapsackNode(rightName, node.Name, "Unsolved", rightOrder);

            left.DecisionOrder = new List<int>(node.DecisionOrder); left.DecisionOrder.Add(j);
            right.DecisionOrder = new List<int>(node.DecisionOrder); right.DecisionOrder.Add(j);

            iterations.Add(left);
            iterations.Add(right);
            node.Status = "Branched";
        }

        private double ComputeObjective(KnapsackNode n)
        {
            double obj = 0.0;
            foreach (var kvp in n.XValues)
                obj += z[kvp.Key - 1] * kvp.Value;
            return obj;
        }

        private double ComputeWeight(KnapsackNode n)
        {
            double w = 0.0;
            foreach (var kvp in n.XValues)
                w += c[kvp.Key - 1] * kvp.Value;
            return w;
        }

        public KnapsackNode GetBestCandidate()
        {
            return iterations
                .Where(n => n.Status == "Candidate")
                .OrderByDescending(n => n.Objective)
                .FirstOrDefault();
        }

        public void PrintAllIterations(string outputFileName = "knapsack_bnb_iterations.txt", bool sortByName = false)
        {
            if (iterations == null || iterations.Count == 0)
            {
                Console.WriteLine("No iterations to print.");
                return;
            }

            // Optional ordering
            var nodes = sortByName
                ? iterations.OrderBy(n => n.Name, StringComparer.Ordinal).ToList()
                : iterations;

            // Console header
            Console.WriteLine("==== BnB Knapsack Iterations ====");
            Console.WriteLine("Capacity: " + capacity);
            Console.WriteLine();

            // Build one big buffer for the file
            var fileSb = new System.Text.StringBuilder();
            fileSb.AppendLine("==== BnB Knapsack Iterations ====");
            fileSb.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            fileSb.AppendLine("Capacity: " + capacity);
            fileSb.AppendLine();

            foreach (var node in nodes)
            {
                // Ensure non-null collections so ToString doesn't choke
                if (node.Order == null) node.Order = new Dictionary<int, int>();
                if (node.XValues == null) node.XValues = new Dictionary<int, double>();
                if (node.DecisionOrder == null) node.DecisionOrder = new List<int>();
                if (node.RankSnapshot == null) node.RankSnapshot = new List<int>();

                // Console
                Console.Write(node.ToString());
                Console.WriteLine("Weight: " + node.WeightUsed + " / " + capacity);
                Console.WriteLine();

                // File
                fileSb.Append(node.ToString());
                fileSb.AppendLine("Weight: " + node.WeightUsed + " / " + capacity);
                fileSb.AppendLine();
            }

            // === Best Candidate ===
            var best = GetBestCandidate();

            Console.WriteLine("=== Best Candidate ===");
            if (best != null)
            {
                Console.Write(best.ToString());
                Console.WriteLine("Weight: " + best.WeightUsed + " / " + capacity);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("None");
                Console.WriteLine();
            }

            fileSb.AppendLine("=== Best Candidate ===");
            if (best != null)
            {
                fileSb.Append(best.ToString());
                fileSb.AppendLine("Weight: " + best.WeightUsed + " / " + capacity);
            }
            else
            {
                fileSb.AppendLine("None");
            }
            fileSb.AppendLine();

            // Write one unified file to the project ROOT Output folder
            try
            {
                // Find the project root by locating the 'Input' folder specifically (ignores any Output under bin)
                string root = AppDomain.CurrentDomain.BaseDirectory;
                while (root != null && !System.IO.Directory.Exists(System.IO.Path.Combine(root, "Input")))
                {
                    var parent = System.IO.Directory.GetParent(root);
                    if (parent == null) break;
                    root = parent.FullName;
                }

                // Fallback: if not found, use the base directory
                if (string.IsNullOrEmpty(root))
                    root = AppDomain.CurrentDomain.BaseDirectory;

                string outDir = System.IO.Path.Combine(root, "Output");
                System.IO.Directory.CreateDirectory(outDir);

                string path = System.IO.Path.Combine(outDir, outputFileName);
                System.IO.File.WriteAllText(path, fileSb.ToString(), System.Text.Encoding.UTF8);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to write iterations file: " + ex.Message);
            }
        }

    }
}
