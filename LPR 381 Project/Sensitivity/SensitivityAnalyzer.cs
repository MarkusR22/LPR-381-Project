using System;
using System.Collections.Generic;
using System.Linq;
using LPR_381_Project.Models; // LinearModel, Variable, Constraint
using LPR_381_Project.Parsers; // if you want to reload files (optional)
using LPR_381_Project.Solvers; // PrimalSimplexSolver/RevisedSimplexSolver/DualSimplex
using LPR_381_Project.Utils;   // OutputWriter if needed

namespace LPR_381_Project.Sensitivity
{
    /// <summary>
    /// A self-contained, menu-driven Sensitivity Analyzer that:
    ///  - does NOT rely on any of your previous Sensitivity code
    ///  - integrates with your Console menu
    ///  - uses the existing Simplex solvers to recompute solutions under perturbations
    ///  - implements all options listed in your PDF brief
    /// 
    /// IMPORTANT: This class is designed to be a drop-in. If any type/namespace names differ in your repo,
    /// just adjust the using statements and the adapter below.
    /// </summary>
    public static class SensitivityAnalyzer
    {
        // ---- Public entrypoint called from your MenuManager ----
        public static void Run(LinearModel baseModel)
        {
            if (baseModel == null)
            {
                Console.WriteLine("No model is loaded. Please load a model first.");
                Console.WriteLine("Press any key to return to the main menu...");
                Console.ReadKey();
                return;
            }

            // Choose a default solver adapter (RevisedSimplex preferred if available)
            ISolverAdapter solver = TryMakeBestSolver();
            if (solver == null)
            {
                Console.WriteLine("No compatible solver found. Ensure RevisedSimplexSolver or PrimalSimplexSolver exists.");
                Console.WriteLine("Press any key to return to the main menu...");
                Console.ReadKey();
                return;
            }

            while (true)
            {
                Console.Clear();
                Console.WriteLine("==============================");
                Console.WriteLine("     Sensitivity Analysis     ");
                Console.WriteLine("==============================");
                Console.WriteLine("Model: " + (baseModel.Name ?? "(unnamed)"));
                Console.WriteLine();
                Console.WriteLine("1) Display range of a selected Non-Basic Variable");
                Console.WriteLine("2) Apply + display change to a selected Non-Basic Variable coefficient");
                Console.WriteLine("3) Display range of a selected Basic Variable");
                Console.WriteLine("4) Apply + display change to a selected Basic Variable coefficient");
                Console.WriteLine("5) Display range of a selected constraint RHS (b)");
                Console.WriteLine("6) Apply + display change to a selected constraint RHS (b)");
                Console.WriteLine("7) Display range of a selected variable within a Non-Basic Variable column (tech. coeff a_ij)");
                Console.WriteLine("8) Apply + display change to a selected technical coefficient a_ij");
                Console.WriteLine("9) Add a new activity (variable) to the optimal solution");
                Console.WriteLine("10) Add a new constraint to the optimal solution");
                Console.WriteLine("11) Display shadow prices (approx.)");
                Console.WriteLine("12) Duality: build dual model");
                Console.WriteLine("13) Duality: solve dual model");
                Console.WriteLine("14) Duality: verify strong/weak duality (compare primal vs dual optima)");
                Console.WriteLine("0) Back");
                Console.Write("\nSelect an option: ");
                var pick = Console.ReadLine();

                if (pick == "0") break;

                try
                {
                    switch (pick)
                    {
                        case "1": RangeOfObjectiveCoefficient(baseModel, solver, onlyNonBasic: true); break;
                        case "2": ApplyChangeToObjectiveCoefficient(baseModel, solver, onlyNonBasic: true); break;
                        case "3": RangeOfObjectiveCoefficient(baseModel, solver, onlyNonBasic: false, onlyBasic: true); break;
                        case "4": ApplyChangeToObjectiveCoefficient(baseModel, solver, onlyNonBasic: false, onlyBasic: true); break;
                        case "5": RangeOfRHS(baseModel, solver); break;
                        case "6": ApplyChangeToRHS(baseModel, solver); break;
                        case "7": RangeOfTechCoefficient(baseModel, solver); break;
                        case "8": ApplyChangeToTechCoefficient(baseModel, solver); break;
                        case "9": AddNewVariable(baseModel, solver); break;
                        case "10": AddNewConstraint(baseModel, solver); break;
                        case "11": DisplayShadowPrices(baseModel, solver); break;
                        case "12": BuildDual(baseModel); break;
                        case "13": SolveDual(baseModel, solver); break;
                        case "14": VerifyDuality(baseModel, solver); break;
                        default:
                            Console.WriteLine("Invalid selection.");
                            Pause();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                    Pause();
                }
            }
        }

        // ----------------------- Core operations -----------------------
        // NOTE: The implementation uses repeated solves to empirically determine ranges.
        // This avoids tight coupling to any specific tableau internals and works with your solvers as-is.

        private static void RangeOfObjectiveCoefficient(LinearModel baseModel, ISolverAdapter solver, bool onlyNonBasic = false, bool onlyBasic = false)
        {
            var (solution, model) = SolveBase(baseModel, solver);
            var basicSet = solution.BasicVariableNames;

            int idx = AskForVariableIndex(model, v =>
            {
                if (onlyNonBasic) return !basicSet.Contains(v.Name);
                if (onlyBasic) return basicSet.Contains(v.Name);
                return true;
            }, label: onlyNonBasic ? "non-basic" : onlyBasic ? "basic" : "");

            var varName = model.Variables[idx].Name;
            double originalC = model.ObjectiveCoefficients[idx];

            // Search allowable decrease/increase for c_j while keeping basis unchanged
            var (minC, maxC) = OneDimensionalRange(model, solver, originalC, set: val => model.ObjectiveCoefficients[idx] = val,
                basisSignature: () => BasisSignature(Solve(baseModel: null, workingModel: model, solver: solver).solution));

            Console.WriteLine($"\nRange for objective coefficient c[{varName}] with current basis fixed:");
            Console.WriteLine($"  Allowable Decrease: down to {minC:F6} (current {originalC})");
            Console.WriteLine($"  Allowable Increase: up to   {maxC:F6} (current {originalC})");
            // Restore
            model.ObjectiveCoefficients[idx] = originalC;
            Pause();
        }

        private static void ApplyChangeToObjectiveCoefficient(LinearModel baseModel, ISolverAdapter solver, bool onlyNonBasic = false, bool onlyBasic = false)
        {
            var (solution, model) = SolveBase(baseModel, solver);
            var basicSet = solution.BasicVariableNames;

            int idx = AskForVariableIndex(model, v =>
            {
                if (onlyNonBasic) return !basicSet.Contains(v.Name);
                if (onlyBasic) return basicSet.Contains(v.Name);
                return true;
            }, label: onlyNonBasic ? "non-basic" : onlyBasic ? "basic" : "");

            var varName = model.Variables[idx].Name;
            double originalC = model.ObjectiveCoefficients[idx];
            Console.Write($"Enter new value for c[{varName}] (current {originalC}): ");
            double newC = ReadDouble();

            model.ObjectiveCoefficients[idx] = newC;
            var (newSol, _) = Solve(baseModel: null, workingModel: model, solver: solver);

            Console.WriteLine("\nNew optimal solution after change to objective coefficient:");
            PrintSolution(newSol);

            // Restore
            model.ObjectiveCoefficients[idx] = originalC;
            Pause();
        }

        private static void RangeOfRHS(LinearModel baseModel, ISolverAdapter solver)
        {
            var (solution, model) = SolveBase(baseModel, solver);
            int cid = AskForConstraintIndex(model);
            double originalB = model.Constraints[cid].RHS;

            var (minB, maxB) = OneDimensionalRange(model, solver, originalB, set: val => model.Constraints[cid].RHS = val,
                basisSignature: () => BasisSignature(Solve(baseModel: null, workingModel: model, solver: solver).solution));

            Console.WriteLine($"\nRange for RHS b[{cid}] with current basis fixed:");
            Console.WriteLine($"  Allowable Decrease: down to {minB:F6} (current {originalB})");
            Console.WriteLine($"  Allowable Increase: up to   {maxB:F6} (current {originalB})");

            model.Constraints[cid].RHS = originalB;
            Pause();
        }

        private static void ApplyChangeToRHS(LinearModel baseModel, ISolverAdapter solver)
        {
            var (solution, model) = SolveBase(baseModel, solver);
            int cid = AskForConstraintIndex(model);
            double originalB = model.Constraints[cid].RHS;
            Console.Write($"Enter new value for RHS of constraint #{cid} (current {originalB}): ");
            double newB = ReadDouble();
            model.Constraints[cid].RHS = newB;
            var (newSol, _) = Solve(baseModel: null, workingModel: model, solver: solver);

            Console.WriteLine("\nNew optimal solution after RHS change:");
            PrintSolution(newSol);

            model.Constraints[cid].RHS = originalB;
            Pause();
        }

        private static void RangeOfTechCoefficient(LinearModel baseModel, ISolverAdapter solver)
        {
            var (solution, model) = SolveBase(baseModel, solver);
            int cid = AskForConstraintIndex(model);
            int vid = AskForVariableIndex(model);
            double originalAij = model.Constraints[cid].Coefficients[vid];

            var (minA, maxA) = OneDimensionalRange(model, solver, originalAij, set: val => model.Constraints[cid].Coefficients[vid] = val,
                basisSignature: () => BasisSignature(Solve(baseModel: null, workingModel: model, solver: solver).solution));

            Console.WriteLine($"\nRange for technical coefficient a[{cid},{vid}] with current basis fixed:");
            Console.WriteLine($"  Allowable Decrease: down to {minA:F6} (current {originalAij})");
            Console.WriteLine($"  Allowable Increase: up to   {maxA:F6} (current {originalAij})");

            model.Constraints[cid].Coefficients[vid] = originalAij;
            Pause();
        }

        private static void ApplyChangeToTechCoefficient(LinearModel baseModel, ISolverAdapter solver)
        {
            var (solution, model) = SolveBase(baseModel, solver);
            int cid = AskForConstraintIndex(model);
            int vid = AskForVariableIndex(model);
            double originalAij = model.Constraints[cid].Coefficients[vid];
            Console.Write($"Enter new value for a[{cid},{vid}] (current {originalAij}): ");
            double newA = ReadDouble();
            model.Constraints[cid].Coefficients[vid] = newA;
            var (newSol, _) = Solve(baseModel: null, workingModel: model, solver: solver);

            Console.WriteLine("\nNew optimal solution after coefficient change:");
            PrintSolution(newSol);

            model.Constraints[cid].Coefficients[vid] = originalAij;
            Pause();
        }

        private static void AddNewVariable(LinearModel baseModel, ISolverAdapter solver)
        {
            var (_, model) = SolveBase(baseModel, solver);
            Console.Write("Enter name for new variable: ");
            string name = Console.ReadLine()?.Trim();
            Console.Write("Enter objective coefficient (c): ");
            double c = ReadDouble();

            double[] col = new double[model.Constraints.Count];
            for (int i = 0; i < model.Constraints.Count; i++)
            {
                Console.Write($"a[{i},{name}] = ");
                col[i] = ReadDouble();
            }

            var v = new Variable { Name = string.IsNullOrWhiteSpace(name) ? $"x{model.Variables.Count}" : name };
            model.Variables.Add(v);
            model.ObjectiveCoefficients.Add(c);
            for (int i = 0; i < model.Constraints.Count; i++)
                model.Constraints[i].Coefficients.Add(col[i]);

            var (newSol, _) = Solve(baseModel: null, workingModel: model, solver: solver);
            Console.WriteLine("\nNew optimal solution after adding the activity:");
            PrintSolution(newSol);
            Pause();
        }

        private static void AddNewConstraint(LinearModel baseModel, ISolverAdapter solver)
        {
            var (_, model) = SolveBase(baseModel, solver);
            Console.WriteLine("Enter the new constraint coefficients (for each variable in order):");
            var coeffs = new List<double>();
            for (int j = 0; j < model.Variables.Count; j++)
            {
                Console.Write($"a[new,{model.Variables[j].Name}] = ");
                coeffs.Add(ReadDouble());
            }
            Console.Write("Enter RHS (b): ");
            double b = ReadDouble();

            var newC = new Constraint
            {
                Coefficients = coeffs,
                RHS = b,
                Sense = ConstraintSense.LessOrEqual // default; adjust if you have other senses and want input
            };
            model.Constraints.Add(newC);

            var (newSol, _) = Solve(baseModel: null, workingModel: model, solver: solver);
            Console.WriteLine("\nNew optimal solution after adding the constraint:");
            PrintSolution(newSol);
            Pause();
        }

        private static void DisplayShadowPrices(LinearModel baseModel, ISolverAdapter solver)
        {
            // If solver exposes duals, grab them. Otherwise approximate via finite differences in RHS.
            var (sol, model) = SolveBase(baseModel, solver);
            if (sol.ShadowPrices != null && sol.ShadowPrices.Count == model.Constraints.Count)
            {
                Console.WriteLine("Shadow prices (dual variables):");
                for (int i = 0; i < model.Constraints.Count; i++)
                    Console.WriteLine($"  y[{i}] = {sol.ShadowPrices[i]:F6}");
                Pause();
                return;
            }

            Console.WriteLine("(Approximated) Shadow prices via \u0394b -> \u0394z / \u0394b:");
            double eps = 1e-5;
            double baseZ = sol.ObjectiveValue;
            for (int i = 0; i < model.Constraints.Count; i++)
            {
                double b0 = model.Constraints[i].RHS;
                model.Constraints[i].RHS = b0 + eps;
                var (sol2, _) = Solve(baseModel: null, workingModel: model, solver: solver);
                double y = (sol2.ObjectiveValue - baseZ) / eps;
                Console.WriteLine($"  y[{i}] ≈ {y:F6}");
                model.Constraints[i].RHS = b0; // restore
            }
            Pause();
        }

        private static void BuildDual(LinearModel baseModel)
        {
            // Construct the algebraic dual (for standard forms). For general forms, adjust mapping.
            var dual = DualBuilder.BuildDual(baseModel);
            Console.WriteLine("Dual model built (not solved). Summary:");
            Console.WriteLine(DualBuilder.Summary(dual));
            Pause();
        }

        private static void SolveDual(LinearModel baseModel, ISolverAdapter solver)
        {
            var dual = DualBuilder.BuildDual(baseModel);
            var (sol, _) = Solve(baseModel: null, workingModel: dual, solver: solver);
            Console.WriteLine("Dual optimum:");
            PrintSolution(sol);
            Pause();
        }

        private static void VerifyDuality(LinearModel baseModel, ISolverAdapter solver)
        {
            var (pSol, _) = SolveBase(baseModel, solver);
            var dual = DualBuilder.BuildDual(baseModel);
            var (dSol, _) = Solve(baseModel: null, workingModel: dual, solver: solver);

            Console.WriteLine($"Primal z* = {pSol.ObjectiveValue:F6}\nDual w* = {dSol.ObjectiveValue:F6}");
            if (Math.Abs(pSol.ObjectiveValue - dSol.ObjectiveValue) < 1e-6)
                Console.WriteLine("Strong Duality appears to hold (values match within tolerance).");
            else if (pSol.ObjectiveValue >= dSol.ObjectiveValue)
                Console.WriteLine("Weak Duality holds (primal ≥ dual for maximization). Values differ -> check optimality/infeasibility.");
            else
                Console.WriteLine("Duality check indicates an inconsistency; verify model/sense.");
            Pause();
        }

        // ----------------------- Engine helpers -----------------------
        private static (SolutionView solution, LinearModel workingCopy) SolveBase(LinearModel baseModel, ISolverAdapter solver)
        {
            // Work on a deep copy so we don't mutate the caller's model
            var clone = ModelCloner.DeepCopy(baseModel);
            var (sol, _) = Solve(baseModel, clone, solver);
            return (sol, clone);
        }

        private static (SolutionView solution, object raw) Solve(LinearModel baseModel, LinearModel workingModel, ISolverAdapter solver)
        {
            var raw = solver.Solve(workingModel);
            var view = SolutionView.From(raw, workingModel, solver);
            return (view, raw);
        }

        private static string BasisSignature(SolutionView sol)
        {
            // Represent the basis as a stable string key (sorted names)
            return string.Join("|", sol.BasicVariableNames.OrderBy(s => s));
        }

        /// <summary>
        /// Find [min,max] values for a scalar parameter p such that the basis signature stays unchanged.
        /// We move in both directions using expanding step search and back off when basis changes.
        /// </summary>
        private static (double minVal, double maxVal) OneDimensionalRange(
            LinearModel model,
            ISolverAdapter solver,
            double current,
            Action<double> set,
            Func<string> basisSignature)
        {
            string baseSig = basisSignature();
            double min = current, max = current;

            // Search down
            double step = Math.Max(1.0, Math.Abs(current) * 0.1);
            for (int iter = 0; iter < 60; iter++)
            {
                double trial = min - step;
                set(trial);
                var sig = basisSignature();
                if (sig == baseSig) { min = trial; step *= 1.8; }
                else { step *= 0.5; if (step < 1e-8) break; }
            }

            // Restore
            set(current);

            // Search up
            step = Math.Max(1.0, Math.Abs(current) * 0.1);
            for (int iter = 0; iter < 60; iter++)
            {
                double trial = max + step;
                set(trial);
                var sig = basisSignature();
                if (sig == baseSig) { max = trial; step *= 1.8; }
                else { step *= 0.5; if (step < 1e-8) break; }
            }

            // Restore
            set(current);
            return (min, max);
        }

        private static void PrintSolution(SolutionView sol)
        {
            Console.WriteLine($"Objective: {sol.ObjectiveValue:F6}");
            Console.WriteLine("Variables:");
            foreach (var kv in sol.VariableValues)
                Console.WriteLine($"  {kv.Key} = {kv.Value:F6}");
            if (sol.ShadowPrices != null)
            {
                Console.WriteLine("Shadow Prices (if available):");
                for (int i = 0; i < sol.ShadowPrices.Count; i++)
                    Console.WriteLine($"  y[{i}] = {sol.ShadowPrices[i]:F6}");
            }
        }

        private static int AskForVariableIndex(LinearModel model, Func<Variable, bool> predicate = null, string label = "")
        {
            var indices = new List<int>();
            Console.WriteLine("\nVariables:");
            for (int i = 0; i < model.Variables.Count; i++)
            {
                var v = model.Variables[i];
                bool ok = predicate == null || predicate(v);
                if (!ok) continue;
                indices.Add(i);
                Console.WriteLine($"  [{i}] {v.Name} (c={model.ObjectiveCoefficients[i]})");
            }
            if (indices.Count == 0) throw new InvalidOperationException("No matching variables to choose from.");
            Console.Write("Select variable index: ");
            int idx = ReadInt();
            if (!indices.Contains(idx)) throw new InvalidOperationException("Index not in the allowed set.");
            return idx;
        }

        private static int AskForConstraintIndex(LinearModel model)
        {
            Console.WriteLine("\nConstraints:");
            for (int i = 0; i < model.Constraints.Count; i++)
            {
                var ci = model.Constraints[i];
                Console.WriteLine($"  [{i}] RHS={ci.RHS}, sense={ci.Sense}");
            }
            Console.Write("Select constraint index: ");
            int idx = ReadInt();
            if (idx < 0 || idx >= model.Constraints.Count) throw new InvalidOperationException("Invalid constraint index.");
            return idx;
        }

        private static int ReadInt()
        {
            while (true)
            {
                var s = Console.ReadLine();
                if (int.TryParse(s, out int v)) return v;
                Console.Write("Please enter an integer: ");
            }
        }
        private static double ReadDouble()
        {
            while (true)
            {
                var s = Console.ReadLine();
                if (double.TryParse(s, out double v)) return v;
                Console.Write("Please enter a number: ");
            }
        }
        private static void Pause()
        {
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        // ----------------------- Lightweight abstractions -----------------------
        /// <summary>
        /// Adapter so we can call your solver(s) without hard-coding their exact class names/APIs.
        /// Adjust if your method signatures differ.
        /// </summary>
        private interface ISolverAdapter
        {
            object Solve(LinearModel model);
            SolutionView Expose(object raw, LinearModel model);
        }

        private static ISolverAdapter TryMakeBestSolver()
        {
            // Prefer RevisedSimplexSolver if present; fall back to PrimalSimplexSolver.
            try { return new RevisedAdapter(); } catch { /* ignore */ }
            try { return new PrimalAdapter(); } catch { /* ignore */ }
            return null;
        }

        private class RevisedAdapter : ISolverAdapter
        {
            private readonly RevisedSimplexSolver _solver;
            public RevisedAdapter() { _solver = new RevisedSimplexSolver(); }
            public object Solve(LinearModel model) => _solver.Solve(model);
            public SolutionView Expose(object raw, LinearModel model) => SolutionView.From(raw, model, this);
        }
        private class PrimalAdapter : ISolverAdapter
        {
            private readonly PrimalSimplexSolver _solver;
            public PrimalAdapter() { _solver = new PrimalSimplexSolver(); }
            public object Solve(LinearModel model) => _solver.Solve(model);
            public SolutionView Expose(object raw, LinearModel model) => SolutionView.From(raw, model, this);
        }

        /// <summary>
        /// A solver-agnostic view of a solution so we can print results and build basis signatures.
        /// Map your solver's real result type here.
        /// </summary>
        private class SolutionView
        {
            public double ObjectiveValue { get; set; }
            public Dictionary<string, double> VariableValues { get; set; } = new();
            public List<string> BasicVariableNames { get; set; } = new();
            public List<double> ShadowPrices { get; set; } // optional

            public static SolutionView From(object raw, LinearModel model, ISolverAdapter adapter)
            {
                // Try to reflect common patterns used in student simplex solvers
                // Adjust this code to your actual result types if needed.
                var view = new SolutionView();

                // Heuristics: many of your solvers return BnBSimplexResult or similar with properties
                // like ObjectiveValue, VariableValues, BasicVariables, DualValues.
                var t = raw.GetType();

                // Objective
                var objProp = t.GetProperty("ObjectiveValue") ?? t.GetProperty("Z");
                view.ObjectiveValue = objProp != null ? Convert.ToDouble(objProp.GetValue(raw)) : double.NaN;

                // Variable values
                var varsDictProp = t.GetProperty("VariableValues");
                if (varsDictProp != null)
                {
                    var dict = varsDictProp.GetValue(raw) as System.Collections.IDictionary;
                    if (dict != null)
                    {
                        foreach (System.Collections.DictionaryEntry de in dict)
                            view.VariableValues[de.Key.ToString()] = Convert.ToDouble(de.Value);
                    }
                }
                else
                {
                    // fallback: read from model if solver didn’t return explicit values (not ideal)
                    for (int i = 0; i < model.Variables.Count; i++)
                    {
                        string name = model.Variables[i].Name;
                        // look for property like X_name or similar – skip in generic version
                        view.VariableValues[name] = double.NaN;
                    }
                }

                // Basic variables (for basis signature)
                var basicProp = t.GetProperty("BasicVariables") ?? t.GetProperty("Basis");
                if (basicProp != null)
                {
                    var list = basicProp.GetValue(raw) as System.Collections.IEnumerable;
                    if (list != null)
                    {
                        foreach (var item in list) view.BasicVariableNames.Add(item.ToString());
                    }
                }
                else
                {
                    // fallback: treat positive-valued variables as basic (rough heuristic)
                    foreach (var kv in view.VariableValues)
                        if (!double.IsNaN(kv.Value) && Math.Abs(kv.Value) > 1e-9) view.BasicVariableNames.Add(kv.Key);
                }

                // Shadow prices / duals (optional)
                var dualProp = t.GetProperty("DualValues") ?? t.GetProperty("ShadowPrices");
                if (dualProp != null)
                {
                    var list = dualProp.GetValue(raw) as System.Collections.IEnumerable;
                    if (list != null)
                    {
                        view.ShadowPrices = new List<double>();
                        foreach (var item in list) view.ShadowPrices.Add(Convert.ToDouble(item));
                    }
                }

                return view;
            }
        }

        private static class ModelCloner
        {
            public static LinearModel DeepCopy(LinearModel src)
            {
                var dst = new LinearModel();
                dst.Name = src.Name;
                dst.IsMaximization = src.IsMaximization;
                dst.ObjectiveCoefficients = src.ObjectiveCoefficients.ToList();
                dst.Variables = src.Variables.Select(v => new Variable { Name = v.Name }).ToList();
                dst.Constraints = new List<Constraint>();
                foreach (var c in src.Constraints)
                {
                    dst.Constraints.Add(new Constraint
                    {
                        Coefficients = c.Coefficients.ToList(),
                        RHS = c.RHS,
                        Sense = c.Sense
                    });
                }
                return dst;
            }
        }

        // ----------------------- Dual builder -----------------------
        private static class DualBuilder
        {
            public static LinearModel BuildDual(LinearModel primal)
            {
                // This constructs a basic dual assuming primal is in standard form for a maximization with <= constraints.
                // If your model supports >= or =, you can expand this mapping as needed (or ask the user for senses).
                int m = primal.Constraints.Count;   // rows
                int n = primal.Variables.Count;      // cols

                var dual = new LinearModel();
                dual.Name = (primal.Name ?? "Model") + "_DUAL";
                dual.IsMaximization = !primal.IsMaximization; // flip

                // Dual variables correspond to primal constraints
                dual.Variables = new List<Variable>();
                for (int i = 0; i < m; i++) dual.Variables.Add(new Variable { Name = $"y{i}" });

                // Dual objective: minimize b^T y (if primal is max with <=)
                dual.ObjectiveCoefficients = new List<double>();
                foreach (var c in primal.Constraints) dual.ObjectiveCoefficients.Add(c.RHS);

                // Dual constraints: A^T y >= c  (for primal max, <=)
                dual.Constraints = new List<Constraint>();
                for (int j = 0; j < n; j++)
                {
                    var coeffs = new List<double>();
                    for (int i = 0; i < m; i++) coeffs.Add(primal.Constraints[i].Coefficients[j]);
                    dual.Constraints.Add(new Constraint
                    {
                        Coefficients = coeffs,
                        RHS = primal.ObjectiveCoefficients[j],
                        Sense = ConstraintSense.GreaterOrEqual
                    });
                }

                return dual;
            }

            public static string Summary(LinearModel model)
            {
                var s = $"Name: {model.Name}\n" +
                        $"Type: {(model.IsMaximization ? "Max" : "Min")}\n" +
                        $"Vars: {model.Variables.Count}, Cons: {model.Constraints.Count}\n";
                s += "Objective: ";
                for (int j = 0; j < model.ObjectiveCoefficients.Count; j++)
                    s += (j > 0 ? ", " : "") + $"c[{model.Variables[j].Name}]={model.ObjectiveCoefficients[j]}";
                s += "\n";
                return s;
            }
        }
    }
}
