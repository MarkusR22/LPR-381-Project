using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Solvers
{
    //Result container for the Branch and Bound Simplex search
    public class BnBSimplexResult
    {
        public Dictionary<string, double> BestX { get; set; } = new Dictionary<string, double>();
        public double BestObjective { get; set; } = double.NegativeInfinity;
        public bool Feasible { get; set; }
        public int NodesExplored { get; set; }
        public string Log { get; set; } = string.Empty;
    }
}
