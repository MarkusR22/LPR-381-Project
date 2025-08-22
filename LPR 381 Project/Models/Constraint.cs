using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Models
{
    public enum Relation { LessThanOrEqual, GreaterThanOrEqual, Equal }

    public class Constraint
    {
        public List<double> Coefficients { get; set; }  // Coefficients of each variable
        public Relation Rel { get; set; }      // <=, >=, =
        public double RHS { get; set; }       // RHS value

        // Parameterless constructor
        public Constraint()
        {
            Coefficients = new List<double>();
        }

        // Constructor with parameters
        public Constraint(List<double> coefficients, Relation rel, double rhs)
        {
            Coefficients = coefficients;
            Rel = rel;
            RHS = rhs;
        }
    }
}
