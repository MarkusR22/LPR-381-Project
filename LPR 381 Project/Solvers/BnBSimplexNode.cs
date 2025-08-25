using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Solvers
{
    //A single Branch and Bound node
    internal class BnBSimplexNode
    {
        // Identity / tree position
        public string Label;                           // e.g., "Root", "L", "R.L", etc.
        public int Depth;

        // Accumulated branching bounds from the root to this node
        public List<BnBSimplexBound> Bounds = new List<BnBSimplexBound>();

        // LP relaxation solution at this node
        public Dictionary<string, double> X = new Dictionary<string, double>();
        public double Objective;                       // objective value of LP relaxation
        public bool IsInteger;                         // true if all integer/binary vars are integral
        public bool Infeasible;                        // LP infeasible/unbounded
        public string SolverUsed;                      // "Dual+Primal", "PrimalSimplex", etc.

        public override string ToString()
        {
            var b = Bounds.Count == 0 ? "(none)" : string.Join(", ", Bounds.Select(x => x.ToString()));
            return $"Node[{Label}] depth={Depth}, bounds={b}, obj={(Infeasible ? "INF" : Objective.ToString("0.###"))}, integer={IsInteger}, solver={SolverUsed}";
        }
    }
}
