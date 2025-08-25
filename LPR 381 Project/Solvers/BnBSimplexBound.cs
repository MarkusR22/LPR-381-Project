using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Solvers
{
    //Bound applied during branching: either x <= value or x >= value
    internal class BnBSimplexBound
    {
        public int VarIndex;   // 0-based index into model.Variables
        public bool IsUpper;   // true => x_j <= Value ; false => x_j >= Value
        public double Value;

        public override string ToString() =>
            IsUpper ? $"x{VarIndex + 1} <= {Value}" : $"x{VarIndex + 1} >= {Value}";
    }
}
