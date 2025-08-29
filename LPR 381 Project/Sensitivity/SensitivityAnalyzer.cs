using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Sensitivity
{
    public class SensitivityAnalyser
    {
        public string SensitivityMenu(Models model, Output result)
        {
            var writer = new StringBuilder();
            writer.AppendLine("\nSensitivity Analysis");

            if (!result.Optimal)
            {
                writer.AppendLine("Sensitivity analysis only works with optimal solutions. The solution you have selected is not optimal");
                return writer.ToString();
            }

            // This menu is the first thing the user will interact with
            while (true)
            {
                Console.WriteLine("\nSensitivity Analysis:");
                Console.WriteLine("1 Range of Non-Basic Variables");
                Console.WriteLine("2 Change Non-Basic Variables");
                Console.WriteLine("3 Range of Basic Variables");            
                Console.WriteLine("4 Change Basic Variables");
                Console.WriteLine("5 Range of RHS Values");
                Console.WriteLine("6 Change RHS Values");
                Console.WriteLine("7 Add New Activity");
                Console.WriteLine("8 Add New Constraint");
                Console.WriteLine("9 Shadow Prices");
                Console.WriteLine("10 Duality Analysis");
                Console.WriteLine("11 Close");

                var choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                       writer.AppendLine(NonBasicVariableRangeCalculator(model, result));
                        break;
                    case "2":
                        writer.AppendLine(ChangeNonBasicVariable(model, result));
                        break;
                    case "3":
                        writer.AppendLine(BasicVariableRangeCalculator(model, result));
                        break;
                    case "4":
                        writer.AppendLine(ChangeBasicVariable(model, result));
                        break;
                    case "5":
                         writer.AppendLine(RHSRangeCalculator(model, result));
                        break;
                    case "6":
                         writer.AppendLine(ChangeRHSValue(model, result));
                        break;
                    case "7":
                       writer.AppendLine(AddNewActivity(model, result));
                        break;
                    case "8":
                        writer.AppendLine(AddNewConstraint(model, result));
                        break;
                    case "9":
                         writer.AppendLine(shadowPricesCalculator(model, result));
                        break;
                    case "10":
                        writer.AppendLine(DualityAnalysis(model, result));
                        break;
                    case "0":
                        return writer.ToString();
                    default:
                        Console.WriteLine("Please choose a valid option from the menu");
                        break;
                }
            }
        }
        //this is the method that calculates the shadow prices
        private string shadowPricesCalculator(Models model, Output result)
        {
            var writer = new StringBuilder();
            writer.AppendLine("Shadow Prices");
            
            for (int i = 0; i < model.Constraints.Count; i++)
            {
                var constraint = model.Constraints[i];
                double shadowPrice = CalculateApproximateShadowPrice(model, result, i);
                
                writer.AppendLine($"Constraint {i + 1}: Shadow Price = {Math.Round(shadowPrice, 3)}");
                writer.AppendLine($"Increasing RHS value by 1 unit changes objective by {Math.Round(shadowPrice, 3)}");
            }
            
            return sb.ToString();
        }
        private const double eps = 1e-6;//this value helps us distinguish between basic and non-basic variables
        //this method shows the range of non-basic variables
        private string AnalyzeNonBasicVariableRange(Models model, Output result)
        {
            Console.Write("Enter variable name: ");
            string variableName = Console.ReadLine();
            
            var writer = new StringBuilder();
            writer.AppendLine($"--- Range Analysis for {variableName} ---");
            
            if (!result.Solution.ContainsKey(variableName))
            {
                writer.AppendLine($"Variable {variableName} was not found in the solution.");
                return writer.ToString();
            }

            double currentValue = result.Solution[varName];
            if (Math.Abs(currentValue) > eps)
            {
                writer.AppendLine($"{variableName} is basic with a value of {Math.Round(currentValue, 3)}");
                writer.AppendLine("Use the 'Range of Basic Variables' option instead.");
            }
            else
            {
                writer.AppendLine($"{variableName} is non-basic (value = 0)");
                writer.AppendLine("Range analysis: The reduced cost must remain non-positive for optimality.");
                writer.AppendLine("Allowable range: [current_coefficient - ∞, current_coefficient + reduced_cost]");
            }
            
            return writer.ToString();
        }
        //this method shows the range of basic variables
        private string BasicVariableRangeCalculator(Models model, Output result)
        {
            Console.Write("Enter variable name: ");
            string variableName = Console.ReadLine();
            
            var writer = new StringBuilder();
            writer.AppendLine($"--- Range Analysis for Basic Variable {variableName} ---");
            
            if (!result.Solution.ContainsKey(variableName))
            {
                sb.AppendLine($"Variable {varName} was not found in the solution.");
                return writer.ToString();
            }

            double currentValue = result.Solution[varName];
            writer.AppendLine($"Current value: {Math.Round(currentValue, 3)}");
            writer.AppendLine("Range analysis requires dual simplex calculations.");
            writer.AppendLine("The allowable range maintains feasibility and optimality.");
            
            return writer.ToString();
        }

        //this method checks the range of right hand side variables
        private string RHSRangeCalculator(Models model, Output result)
        {
            Console.Write("Enter constraint number (1-based): ");
            if (!int.TryParse(Console.ReadLine(), out int constraintNum) || 
                constraintNum < 1 || constraintNum > model.Constraints.Count)
            {
                return "Invalid constraint number.";
            }

            var writer = new StringBuilder();
            writer.AppendLine($"--- RHS Range Analysis for Constraint {constraintNum} ---");
            
            var constraint = model.Constraints[constraintNum - 1];
            writer.AppendLine($"Current RHS: {constraint.Rhs}");
            writer.AppendLine("RHS range analysis maintains feasibility.");
            writer.AppendLine("Range: [RHS - allowable_decrease, RHS + allowable_increase]");
            
            return writer.ToString();
        }
        //this method allows the user to change the coefficient of a non-basic variable
        private string ChangeNonBasicVariable(Models model, Output result)
        {
            Console.Write("Enter variable name: ");
            string variableName = Console.ReadLine();
            Console.Write("Enter new coefficient: ");
            if (!double.TryParse(Console.ReadLine(), out double newCoeff))//this ensures that the user knows to add a double value
            {
                return "Invalid coefficient.";
            }

            var writer = new StringBuilder();
            writer.AppendLine($"--- Change Analysis for {variableName} ---");
            writer.AppendLine($"New coefficient: {newCoeff}");
            writer.AppendLine("Effect: Changes the reduced cost of the variable.");
            
            return writer.ToString();
        }
        //this method allows the user to change the coefficient of a basic variable
         private string ChangeBasicVariable(Models model, Output result)
        {
            Console.Write("Enter variable name: ");
            string variableName = Console.ReadLine() ??;
            Console.Write("Enter new coefficient: ");
            if (!double.TryParse(Console.ReadLine(), out double newCoeff))
            {
                return "Invalid coefficient.";
            }

            var writer = new StringBuilder();
            writer.AppendLine($"--- Change Analysis for Basic Variable {variableName} ---");
            writer.AppendLine($"New coefficient: {newCoeff}");
            writer.AppendLine("Effect: Changes objective value and may affect optimality.");
            writer.AppendLine("New objective = old_objective + change_in_coeff * variable_value");
            
            if (result.Solution.TryGetValue(varName, out double value))
            {
                int varIndex = Array.IndexOf(model.VariableNames, varName);
                if (varIndex >= 0)
                {
                    double oldCoeff = model.ObjectiveCoeffs[varIndex];
                    double change = newCoeff - oldCoeff;
                    double newObjective = result.ObjectiveValue + change * value;
                    writer.AppendLine($"Estimated new objective: {Math.Round(newObjective, 3)}");
                }
            }
            
            return writer.ToString();
        }
        //this method allows the user to change the right hand side value
        private string ChangeRHSValue(Models model, Output result)
        {
            Console.Write("Enter constraint number (1-based): ");
            if (!int.TryParse(Console.ReadLine(), out int constraintNum) || 
                constraintNum < 1 || constraintNum > model.Constraints.Count)
            {
                return "Invalid constraint number.";
            }

            Console.Write("Enter new RHS value: ");
            if (!double.TryParse(Console.ReadLine(), out double newRHS))
            {
                return "Invalid RHS value.";
            }

            var writer = new StringBuilder();
            writer.AppendLine($"RHS Change Analysis for Constraint {constraintNum}");
            
            var constraint = model.Constraints[constraintNum - 1];
            double change = newRHS - constraint.Rhs;
            double shadowPrice = CalculateApproximateShadowPrice(model, result, constraintNum - 1);
            
            writer.AppendLine($"Old RHS: {constraint.Rhs}");
            writer.AppendLine($"New RHS: {newRHS}");
            writer.AppendLine($"Change: {Math.Round(change, 3)}");
            writer.AppendLine($"Estimated objective change: {Math.Round(change * shadowPrice, 3)}");
            writer.AppendLine($"Estimated new objective: {Math.Round(result.ObjectiveValue + change * shadowPrice, 3)}");
            
            return writer.ToString();
        }
        /*//this is where the user gets to 
        private string AddNewActivity(Models model, Output result)
        {
            var writer = new StringBuilder();
            writer.AppendLine("New Activity");
            writer.AppendLine("Please select what you would like to add below");
            writer.AppendLine("1 Objective function coefficient");
            writer.AppendLine("2 Constraint coefficients");
            
            return writer.ToString();
        }*/
    }
}
