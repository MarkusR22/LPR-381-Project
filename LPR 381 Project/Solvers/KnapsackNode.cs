using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Solvers
{
    class KnapsackNode
    {
        public string Name { get; set; }
        public string Parent { get; set; }
       
        public string Status { get; set; }

        public Dictionary<int, int> Order { get; set; }

        public Dictionary<int, double> XValues { get; set; }

        public double Objective { get; set; }  
        public double WeightUsed { get; set; }    
        public int? FractionalVar { get; set; }

        // NEW: exact order the decisions were made along the branch path
        public List<int> DecisionOrder { get; set; }

        // NEW: snapshot of rank used when solving this node
        public List<int> RankSnapshot { get; set; }

        public KnapsackNode(string name, string parent, string status, Dictionary<int, int> order)
        {
            Name = name;
            Parent = parent;
            Status = status;
            Order = order ?? new Dictionary<int, int>();
            XValues = new Dictionary<int, double>();
            Objective = 0.0;
            WeightUsed = 0.0;
            FractionalVar = null;

            DecisionOrder = new List<int>();
            RankSnapshot = new List<int>();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            // --- Header with node info ---
            sb.AppendLine($"Node {Name} | Status: {Status} | Objective: {Objective}");

            // Build the display order = DecisionOrder (unique, in order) + remaining by RankSnapshot
            var seen = new HashSet<int>();
            var orderList = new List<int>();

            for (int i = 0; i < DecisionOrder.Count; i++)
            {
                int v = DecisionOrder[i];
                if (!seen.Contains(v))
                {
                    orderList.Add(v);
                    seen.Add(v);
                }
            }

            for (int i = 0; i < RankSnapshot.Count; i++)
            {
                int v = RankSnapshot[i];
                if (!seen.Contains(v))
                {
                    orderList.Add(v);
                    seen.Add(v);
                }
            }

            // Fallback if RankSnapshot is empty (e.g., node never solved)
            if (orderList.Count == 0 && XValues.Count > 0)
            {
                foreach (var k in XValues.Keys.OrderBy(k => k))
                    orderList.Add(k);
            }

            // Build rows like "x5=0", "x3=1", "x1=2/11"
            var rows = new List<string>();
            for (int i = 0; i < orderList.Count; i++)
            {
                int varIdx = orderList[i];
                double val;
                if (!XValues.TryGetValue(varIdx, out val))
                    val = 0.0;

                string cell;
                if (Math.Abs(val - Math.Round(val)) < 1e-9)
                {
                    cell = "x" + varIdx + "=" + ((int)Math.Round(val)).ToString();
                }
                else
                {
                    // Fraction approximation (denom <= 100), then reduce
                    const int maxDen = 100;
                    int bestNum = 0, bestDen = 1;
                    double bestErr = double.MaxValue;
                    for (int den = 1; den <= maxDen; den++)
                    {
                        int num = (int)Math.Round(val * den);
                        double err = Math.Abs(val - (double)num / den);
                        if (err < bestErr)
                        {
                            bestErr = err;
                            bestNum = num;
                            bestDen = den;
                        }
                    }
                    int g = GCD(Math.Abs(bestNum), bestDen);
                    bestNum /= g;
                    bestDen /= g;
                    cell = "x" + varIdx + "=" + bestNum + "/" + bestDen;
                }

                rows.Add(cell);
            }

            // width and border
            int maxLen = rows.Count > 0 ? rows.Max(r => r.Length) : 0;
            string border = "+" + new string('-', maxLen + 2) + "+";

            sb.AppendLine(border);
            for (int i = 0; i < rows.Count; i++)
                sb.AppendLine("| " + rows[i].PadRight(maxLen) + " |");
            sb.AppendLine(border);

            return sb.ToString();
        }

        private static int GCD(int a, int b)
        {
            while (b != 0)
            {
                int t = b;
                b = a % b;
                a = t;
            }
            return (a < 0) ? -a : a;
        }
    }
}
