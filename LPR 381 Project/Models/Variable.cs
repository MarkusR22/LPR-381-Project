using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LPR_381_Project.Models
{
    // Variable type enum (continuous, integer, binary)
    public enum VarType { Positive, Negative, Integer, Binary }

    public class Variable
    {
        public string Name { get; set; }         // e.g., x1, x2
        public double Coefficient { get; set; }  // Objective function coefficient
        public VarType Type { get; set; }        // Variable type

        public bool IsInteger => Type == VarType.Integer || Type == VarType.Binary;

        // Parameterless constructor (needed for some deserialization/initialization)
        public Variable()
        {
        }

        // Constructor with parameters
        public Variable(string name, double coefficient, VarType type)
        {
            Name = name;
            Coefficient = coefficient;
            Type = type;
        }
    }
}
