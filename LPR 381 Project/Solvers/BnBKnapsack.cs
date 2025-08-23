using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LPR_381_Project.Solvers
{
    class BnBKnapsack
    {
        public int capacity;

        public int[] z;

        public int[] c;

        public List<int> rank;

        public List<KnapsackNode> iterations;

        public Dictionary<int, int> weights;

        public BnBKnapsack(int capacity, int[] z, int[] c)
        {
            this.capacity = capacity;
            this.z = z;
            this.c = c;
        }

        public List<KnapsackNode> Solve()
        {
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

            // When loop ends, no nodes remain with "Unsolved" or "Unbranched"
            return iterations;
        }


        public List<int> DetermineRank()
        {
            List<int> orderedRank = new List<int>();

            Dictionary<int, double> ratios = new Dictionary<int, double>();

            for (int i = 0; i < z.GetLength(0); i++)
            {
                ratios.Add(i + 1, (double)z[i] / c[i]);
            }

            var sortedRatios = ratios.OrderByDescending(kvp => kvp.Value);

            foreach (var kvp in sortedRatios)
            {
                orderedRank.Add(kvp.Key);
            }

            return orderedRank;
        }

        public KnapsackNode SolveNode(KnapsackNode node, int cap)
        {
            if (rank == null || rank.Count == 0)
            {
                rank = DetermineRank();
            }

            node.RankSnapshot = new List<int>(rank);

            if (weights == null || weights.Count == 0)
            {
                weights = new Dictionary<int, int>();
                for (int i = 0; i < c.Length; i++)
                    weights[i + 1] = c[i];
            }

            if (node.Order == null)
            {
                node.Order = new Dictionary<int, int>();
            }
                

            // reset X-values
            node.XValues = new Dictionary<int, double>();
            node.FractionalVar = null;

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
                        {
                            if (!node.XValues.ContainsKey(v))
                            {
                                node.XValues[v] = 0.0;
                            }
                        }
                            

                        node.Objective = 0.0;
                        node.WeightUsed = 0.0;
                        node.Status = "Infeasible";
                        return node;
                    }
                }
                else
                {
                    node.XValues[varIdx] = 0.0;
                }
            }


            var remaining = rank.Where(v => !node.Order.ContainsKey(v)).ToList();

  
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
                    {
                        if (!node.XValues.ContainsKey(u))
                        {
                            node.XValues[u] = 0.0;
                        }
                            
                    }
                        


                    for (int idx = 1; idx <= z.Length; idx++)
                    {
                        if (!node.XValues.ContainsKey(idx))
                        {
                            node.XValues[idx] = 0.0;
                        }
                    }

                    node.FractionalVar = v;
                    node.Objective = ComputeObjective(node);
                    node.WeightUsed = ComputeWeight(node);
                    node.Status = "Unbranched";
                    return node;
                }
            }

            for (int idx = 1; idx <= z.Length; idx++)
            {
                if (!node.XValues.ContainsKey(idx))
                {
                    node.XValues[idx] = 0.0;
                }
            }

            node.Objective = ComputeObjective(node);
            node.WeightUsed = ComputeWeight(node);
            node.Status = "Candidate";
            return node;
        }

        public void Branch(KnapsackNode node)
        {
            if (node.Status != "Unbranched") return;

            // Use stored fractional var if set; otherwise find the first fractional by rank
            int j = node.FractionalVar ?? 0;
            if (j == 0)
            {
                foreach (var v in rank)
                {
                    if (!node.XValues.TryGetValue(v, out var x)) continue;
                    if (x > 0.0 && x < 1.0) { j = v; break; }
                }
                if (j == 0) { node.Status = "Candidate"; return; } // nothing fractional
            }

            // Left child: x_j = 0  -> ".1"
            var leftOrder = new Dictionary<int, int>(node.Order) { [j] = 0 };
            var leftName = $"{node.Name}.1";
            var left = new KnapsackNode(leftName, node.Name, "Unsolved", leftOrder);
            left.DecisionOrder = new List<int>(node.DecisionOrder);
            left.DecisionOrder.Add(j);

            // Right child: x_j = 1 -> ".2"
            var rightOrder = new Dictionary<int, int>(node.Order) { [j] = 1 };
            var rightName = $"{node.Name}.2";
            var right = new KnapsackNode(rightName, node.Name, "Unsolved", rightOrder);
            right.DecisionOrder = new List<int>(node.DecisionOrder);
            right.DecisionOrder.Add(j);

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

    }
}
