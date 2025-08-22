using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using LPR_381_Project.Models;

namespace LPR_381_Project.Parsers
{
    public class InputFileParser
    {
        // Parses an input file and returns a LinearModel object
        public static LinearModel ParseFile(string filePath)
        {
            var model = new LinearModel();

            // Read all lines
            string[] lines = File.ReadAllLines(filePath);

            if (lines.Length < 2)
                throw new Exception("Input file must have at least an objective line and one constraint.");

            // --- Parse Objective Function ---
            // Example: max +2 +3 +3 +5 +2 +4
            string[] objParts = lines[0].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Set objective type
            model.Obj = objParts[0].ToLower() == "max" ? Objective.Maximize : Objective.Minimize;

            // Add variables
            for (int i = 1; i < objParts.Length; i++)
            {
                string part = objParts[i]; // e.g., "+2" or "-3"
                VarType type = VarType.Positive; // default

                // Check the sign
                double coeff = 0;
                if (part.StartsWith("+"))
                    coeff = Convert.ToDouble(part.Substring(1));
                else if (part.StartsWith("-"))
                    coeff = Convert.ToDouble(part.Substring(1)) * -1;
                else
                    coeff = Convert.ToDouble(part);

                // Give variable a name (x1, x2, etc.)
                string name = "x" + i;

                model.Variables.Add(new Variable(name, coeff, type));
            }

            // --- Parse Constraints ---
            // All lines between objective line and last line (last line is variable types)
            for (int i = 1; i < lines.Length - 1; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                var coeffs = new List<double>();

                // All except last two entries are coefficients
                for (int j = 0; j < parts.Length - 2; j++)
                {
                    double val = Convert.ToDouble(parts[j]);
                    coeffs.Add(val);
                }

                // Last two entries: relation and RHS
                Relation rel;
                if (parts[parts.Length - 2] == "<=")
                    rel = Relation.LessThanOrEqual;
                else if (parts[parts.Length - 2] == ">=")
                    rel = Relation.GreaterThanOrEqual;
                else if (parts[parts.Length - 2] == "=")
                    rel = Relation.Equal;
                else
                    throw new Exception("Unknown relation operator: " + parts[parts.Length - 2]);

                double rhs = Convert.ToDouble(parts[parts.Length - 1]);

                model.Constraints.Add(new Constraint(coeffs, rel, rhs));
            }

            // --- Parse Variable Types (last line) ---
            // Example: bin bin bin bin bin bin
            string[] varTypes = lines[lines.Length - 1].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (varTypes.Length != model.Variables.Count)
                throw new Exception("Variable type line length does not match number of variables.");

            for (int i = 0; i < varTypes.Length; i++)
            {
                string t = varTypes[i].ToLower();
                if (t == "bin")
                    model.Variables[i].Type = VarType.Binary;
                else if (t == "int")
                    model.Variables[i].Type = VarType.Integer;
                else if (t == "+")
                    model.Variables[i].Type = VarType.Positive;
                else if (t == "-")
                    model.Variables[i].Type = VarType.Negative;
                else
                    throw new Exception("Unknown variable type: " + t);
            }

            return model;
        }
    }
}
