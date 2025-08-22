using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Models
{
    // Enum to define if the objective is Maximize or Minimize
    public enum Objective { Maximize, Minimize }

    public class LinearModel
    {
        public Objective Obj { get; set; }
        public List<Variable> Variables { get; set; }
        public List<Constraint> Constraints { get; set; }

        // Constructor initializes empty lists for variables and constraint
        public LinearModel()
        {
            Variables = new List<Variable>();
            Constraints = new List<Constraint>();
        }
    }
}
