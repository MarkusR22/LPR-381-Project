// File: LPR_381_Project/Sensitivity/SensitivityAnalyzer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Sensitivity
{
    
    public class SensitivityAnalyzer
    {
        private const double Tolerance = 1e-9;
        private const double LargeSearchLimit = 1e6;
        private const double PerturbationBase = 1e-6;

        private readonly StringBuilder writer = new StringBuilder();

       
        public string Writer => writer.ToString();
        
        private readonly object model;

        private readonly object solverInstance;
        private readonly MethodInfo solveMethod;

        public SensitivityAnalyzer(object linearModel)
        {
            this.model = linearModel ?? throw new ArgumentNullException(nameof(linearModel));

            (solverInstance, solveMethod) = CreateSolverInstanceAndMethod();
            if (solverInstance == null || solveMethod == null)
            {
                writer.AppendLine("Warning: Could not find a RevisedSimplexSolver or Solve(model) method via reflection.");
                writer.AppendLine("The analyzer will still try to inspect the model (values and coefficients) but cannot re-solve the model.");
            }
        }

       
        public void Analyze()
        {
            writer.Clear();
            writer.AppendLine("=== Sensitivity Analysis Report ===");
            writer.AppendLine($"Generated: {DateTime.UtcNow:u}");
            writer.AppendLine();

            
            PrintModelSummary();

            object baseSolution = null;
            if (solveMethod != null)
            {
                baseSolution = SolveModelWithReflection(model);
            }

            var varNames = TryGetVariableNames(model);
            var objectiveCoeffs = TryGetObjectiveCoefficients(model);
            var constraintRHS = TryGetConstraintRHS(model);
            var constraintNames = TryGetConstraintNames(model);

            // Try to get the solution (variable values, objective) either from baseSolution or model
            double[] variableValues = TryExtractVariableValues(baseSolution) ?? TryExtractVariableValuesFromModel(model);
            double? objectiveValue = TryExtractObjectiveValue(baseSolution); //?? TryExtractObjectiveFromModel(model);

            if (variableValues != null && variableValues.Length > 0)
            {
                writer.AppendLine("Optimal variable values:");
                for (int i = 0; i < variableValues.Length; i++)
                {
                    string name = (varNames != null && i < varNames.Length) ? varNames[i] : $"x{i + 1}";
                    writer.AppendLine($"  {name} = {variableValues[i]:F6}");
                }
                writer.AppendLine();
            }
            else
            {
                writer.AppendLine("Variable values: not available from solver/model.");
                writer.AppendLine();
            }

            if (objectiveValue.HasValue)
            {
                writer.AppendLine($"Objective value (at reported solution): {objectiveValue.Value:F6}");
                writer.AppendLine();
            }
            else
            {
                writer.AppendLine("Objective value: not available from solver/model.");
                writer.AppendLine();
            }

            // Reduced costs & shadow prices: attempt to get from baseSolution
            double[] reducedCosts = TryExtractReducedCosts(baseSolution);
            double[] shadowPrices = TryExtractShadowPrices(baseSolution);

            if (reducedCosts != null)
            {
                writer.AppendLine("Reduced costs:");
                for (int i = 0; i < reducedCosts.Length; i++)
                {
                    string name = (varNames != null && i < varNames.Length) ? varNames[i] : $"x{i + 1}";
                    writer.AppendLine($"  Reduced cost {name}: {reducedCosts[i]:F6}");
                }
                writer.AppendLine();
            }
            else
            {
                writer.AppendLine("Reduced costs: not available (couldn't extract from solver).");
                writer.AppendLine();
            }

            if (shadowPrices != null)
            {
                writer.AppendLine("Shadow prices (dual values) for constraints:");
                for (int i = 0; i < shadowPrices.Length; i++)
                {
                    string cname = (constraintNames != null && i < constraintNames.Length) ? constraintNames[i] : $"c{i + 1}";
                    writer.AppendLine($"  {cname}: {shadowPrices[i]:F6}");
                }
                writer.AppendLine();
            }
            else
            {
                writer.AppendLine("Shadow prices: not available (couldn't extract from solver).");
                writer.AppendLine();
            }

            // Allowable increase/decrease: numeric basis-preserving search via re-solving (if solver available)
            if (solveMethod != null)
            {
                writer.AppendLine("Computing basis-preserving allowable increases/decreases (numeric re-solve).");
                writer.AppendLine("This checks how much each coefficient or RHS can change before the basis changes.");
                writer.AppendLine();

                // Objective coefficients
                if (objectiveCoeffs != null && variableValues != null)
                {
                    for (int i = 0; i < objectiveCoeffs.Length; i++)
                    {
                        string varName = (varNames != null && i < varNames.Length) ? varNames[i] : $"x{i + 1}";
                        var (dec, inc) = FindCoefficientRangePreservingBasis(i, objectiveCoeffs[i], model, baseSolution);
                        writer.AppendLine($"Objective coef for {varName}: base={objectiveCoeffs[i]:F6}, allowable decrease = {dec?.ToString("F6") ?? "N/A"}, allowable increase = {inc?.ToString("F6") ?? "N/A"}");
                    }
                    writer.AppendLine();
                }
                else
                {
                    writer.AppendLine("Cannot compute objective coefficient ranges: objective coefficients or variable count not found.");
                    writer.AppendLine();
                }

                // RHS ranges
                if (constraintRHS != null)
                {
                    for (int i = 0; i < constraintRHS.Length; i++)
                    {
                        string cname = (constraintNames != null && i < constraintNames.Length) ? constraintNames[i] : $"c{i + 1}";
                        var (dec, inc) = FindRHSRangePreservingBasis(i, constraintRHS[i], model, baseSolution);
                        writer.AppendLine($"RHS for {cname}: base={constraintRHS[i]:F6}, allowable decrease = {dec?.ToString("F6") ?? "N/A"}, allowable increase = {inc?.ToString("F6") ?? "N/A"}");
                    }
                    writer.AppendLine();
                }
                else
                {
                    writer.AppendLine("Cannot compute RHS ranges: constraint RHS not found.");
                    writer.AppendLine();
                }
            }
            else
            {
                writer.AppendLine("Skipping numeric allowable-range analysis — solver not found or Solve method unavailable.");
                writer.AppendLine();
            }

            writer.AppendLine("End of report.");
        }

        #region Reflection & helper methods

        /// <summary>
        /// Try to find a RevisedSimplexSolver-like type with a parameterless ctor and a Solve(model) method.
        /// Returns tuple (instance, MethodInfo for Solve).
        /// </summary>
        private (object instance, MethodInfo solveMethod) CreateSolverInstanceAndMethod()
        {
            // Search loaded assemblies for a type named RevisedSimplexSolver or RevisedPrimalSimplex or SimplexSolver
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type solverType = null;
            foreach (var asm in assemblies)
            {
                try
                {
                    solverType = asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, "RevisedSimplexSolver", StringComparison.OrdinalIgnoreCase)
                                                                    || string.Equals(t.Name, "RevisedPrimalSimplex", StringComparison.OrdinalIgnoreCase)
                                                                    || string.Equals(t.Name, "RevisedSimplex", StringComparison.OrdinalIgnoreCase)
                                                                    || string.Equals(t.Name, "SimplexSolver", StringComparison.OrdinalIgnoreCase));
                    if (solverType != null) break;
                }
                catch
                {
                    // ignore assemblies we cannot inspect
                }
            }

            if (solverType == null) return (null, null);

            // find a parameterless constructor
            var ctor = solverType.GetConstructor(Type.EmptyTypes);
            object instance = null;
            if (ctor != null)
            {
                instance = ctor.Invoke(null);
            }
            else
            {
                // try to find any constructor and pass nulls (best-effort)
                var cinfo = solverType.GetConstructors().FirstOrDefault();
                if (cinfo != null)
                {
                    var pars = cinfo.GetParameters().Select(p => GetDefault(p.ParameterType)).ToArray();
                    try
                    {
                        instance = cinfo.Invoke(pars);
                    }
                    catch
                    {
                        instance = null;
                    }
                }
            }

            // find a Solve(...) method that accepts one parameter (the model) or a Solve() that uses previously set model
            MethodInfo solve = solverType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return string.Equals(m.Name, "Solve", StringComparison.OrdinalIgnoreCase) && (ps.Length == 1 || ps.Length == 0);
                });

            return (instance, solve);
        }

        private object GetDefault(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        private object SolveModelWithReflection(object modelToSolve)
        {
            if (solveMethod == null) return null;
            try
            {
                // If Solve has one param, invoke with model; else if parameterless, try setting a Model property then call Solve()
                var parms = solveMethod.GetParameters();
                if (parms.Length == 1)
                {
                    return solveMethod.Invoke(solverInstance, new object[] { modelToSolve });
                }
                else
                {
                    // try set property "Model" or "linearModel" on solver
                    var sType = solverInstance.GetType();
                    var modelProp = sType.GetProperty("Model") ?? sType.GetProperty("linearModel") ?? sType.GetField("Model") as MemberInfo;
                    if (modelProp != null)
                    {
                        if (modelProp is PropertyInfo pi && pi.CanWrite)
                        {
                            pi.SetValue(solverInstance, modelToSolve);
                        }
                        else if (modelProp is FieldInfo fi)
                        {
                            fi.SetValue(solverInstance, modelToSolve);
                        }
                    }

                    var ret = solveMethod.Invoke(solverInstance, null);
                    return ret;
                }
            }
            catch (TargetInvocationException tie)
            {
                writer.AppendLine("Solver invocation raised an exception: " + tie.InnerException?.Message);
                return null;
            }
            catch (Exception ex)
            {
                writer.AppendLine("Solver invocation failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Basic summary print: number of variables/constraints if discoverable.
        /// </summary>
        private void PrintModelSummary()
        {
            try
            {
                int? nVars = TryGetIntFromModel("NumberOfVariables", "NumVariables", "VariablesCount", "N");
                int? nCons = TryGetIntFromModel("NumberOfConstraints", "ConstraintsCount", "M");
                if (nVars.HasValue) writer.AppendLine($"Model: {nVars.Value} variables");
                if (nCons.HasValue) writer.AppendLine($"Model: {nCons.Value} constraints");
                if (!nVars.HasValue && !nCons.HasValue)
                {
                    writer.AppendLine("Model summary: could not detect variable/constraint counts via reflection.");
                }
                writer.AppendLine();
            }
            catch (Exception ex)
            {
                writer.AppendLine("Error while printing model summary: " + ex.Message);
            }
        }

        private int? TryGetIntFromModel(params string[] candidateNames)
        {
            foreach (var name in candidateNames)
            {
                var f = model.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(int))
                    return (int)f.GetValue(model);

                var p = model.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?)))
                    return (int?)p.GetValue(model) ?? null;
            }
            return null;
        }

        private string[] TryGetVariableNames(object modelObj)
        {
            if (modelObj == null) return null;
            // Look for property/field named VariableNames, VarNames, Names, ColumnNames
            var candidates = new[] { "VariableNames", "VarNames", "Names", "ColumnNames", "VariableLabels" };
            foreach (var name in candidates)
            {
                var p = modelObj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var val = p.GetValue(modelObj);
                    if (val is IEnumerable<string> sEnum) return sEnum.ToArray();
                    if (val is string[] sArr) return sArr;
                    if (val is object[] oArr) return oArr.Select(o => o?.ToString() ?? "").ToArray();
                }
                var f = modelObj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(modelObj);
                    if (val is IEnumerable<string> sEnum) return sEnum.ToArray();
                    if (val is string[] sArr) return sArr;
                    if (val is object[] oArr) return oArr.Select(o => o?.ToString() ?? "").ToArray();
                }
            }
            // if not found, attempt from objective coeff length
            var coeffs = TryGetObjectiveCoefficients(modelObj);
            if (coeffs != null) return Enumerable.Range(1, coeffs.Length).Select(i => $"x{i}").ToArray();
            return null;
        }

        private string[] TryGetConstraintNames(object modelObj)
        {
            if (modelObj == null) return null;
            var candidates = new[] { "ConstraintNames", "ConNames", "RowNames" };
            foreach (var name in candidates)
            {
                var p = modelObj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var val = p.GetValue(modelObj);
                    if (val is IEnumerable<string> sEnum) return sEnum.ToArray();
                    if (val is string[] sArr) return sArr;
                    if (val is object[] oArr) return oArr.Select(o => o?.ToString() ?? "").ToArray();
                }
                var f = modelObj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(modelObj);
                    if (val is IEnumerable<string> sEnum) return sEnum.ToArray();
                    if (val is string[] sArr) return sArr;
                    if (val is object[] oArr) return oArr.Select(o => o?.ToString() ?? "").ToArray();
                }
            }
            // fallback to enumerated names
            var rhs = TryGetConstraintRHS(modelObj);
            if (rhs != null) return Enumerable.Range(1, rhs.Length).Select(i => $"c{i}").ToArray();
            return null;
        }

        private double[] TryGetObjectiveCoefficients(object modelObj)
        {
            if (modelObj == null) return null;
            var candidates = new[] { "ObjectiveCoefficients", "C", "ObjCoefficients", "Objective", "ObjectiveCoeff" };
            foreach (var name in candidates)
            {
                var p = modelObj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var val = p.GetValue(modelObj);
                    var arr = ConvertToDoubleArray(val);
                    if (arr != null) return arr;
                }
                var f = modelObj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(modelObj);
                    var arr = ConvertToDoubleArray(val);
                    if (arr != null) return arr;
                }
            }
            return null;
        }

        private double[] TryGetConstraintRHS(object modelObj)
        {
            if (modelObj == null) return null;
            var candidates = new[] { "Rhs", "RHS", "ConstraintRHS", "B", "RightHandSide" };
            foreach (var name in candidates)
            {
                var p = modelObj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var val = p.GetValue(modelObj);
                    var arr = ConvertToDoubleArray(val);
                    if (arr != null) return arr;
                }
                var f = modelObj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(modelObj);
                    var arr = ConvertToDoubleArray(val);
                    if (arr != null) return arr;
                }
            }
            return null;
        }

        private double[] ConvertToDoubleArray(object val)
        {
            if (val == null) return null;
            if (val is double[] darr) return darr;
            if (val is float[] farr) return farr.Select(x => (double)x).ToArray();
            if (val is int[] iarr) return iarr.Select(x => (double)x).ToArray();
            if (val is IEnumerable<double> ede) return ede.ToArray();
            if (val is IEnumerable<object> eo) return eo.Select(o => Convert.ToDouble(o)).ToArray();
            return null;
        }

        /// <summary>
        /// Try to extract solved variable values from solver result object via reflection.
        /// Common property names: Solution, VariableValues, X, PrimalVariables, Primal
        /// If solver returns void but sets fields on solver, try to find them on the solverInstance.
        /// </summary>
        private double[] TryExtractVariableValues(object solverReturnOrResult)
        {
            // If solverReturnOrResult is null, try to check solverInstance fields
            if (solverReturnOrResult == null)
            {
                if (solverInstance != null)
                {
                    var candidates = new[] { "Solution", "VariableValues", "X", "Primal", "PrimalValues", "Variables" };
                    foreach (var name in candidates)
                    {
                        var p = solverInstance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (p != null)
                        {
                            var arr = ConvertToDoubleArray(p.GetValue(solverInstance));
                            if (arr != null) return arr;
                        }
                        var f = solverInstance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null)
                        {
                            var arr = ConvertToDoubleArray(f.GetValue(solverInstance));
                            if (arr != null) return arr;
                        }
                    }
                }
                return null;
            }

            // else inspect the return object
            var candidatesReturn = new[] { "Solution", "VariableValues", "X", "Primal", "PrimalVariables", "PrimalValues", "Variables" };
            foreach (var name in candidatesReturn)
            {
                var p = solverReturnOrResult.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var arr = ConvertToDoubleArray(p.GetValue(solverReturnOrResult));
                    if (arr != null) return arr;
                }
                var f = solverReturnOrResult.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var arr = ConvertToDoubleArray(f.GetValue(solverReturnOrResult));
                    if (arr != null) return arr;
                }
            }

            // Maybe return object itself is an enumerable of numbers
            var conv = ConvertToDoubleArray(solverReturnOrResult);
            if (conv != null) return conv;

            return null;
        }

        private double[] TryExtractVariableValuesFromModel(object modelObj)
        {
            if (modelObj == null) return null;
            // Some models store an initial or lastSolution inside them
            var candidates = new[] { "LastSolution", "InitialSolution", "Solution", "VariableValues", "X" };
            foreach (var name in candidates)
            {
                var p = modelObj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var arr = ConvertToDoubleArray(p.GetValue(modelObj));
                    if (arr != null) return arr;
                }
                var f = modelObj.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var arr = ConvertToDoubleArray(f.GetValue(modelObj));
                    if (arr != null) return arr;
                }
            }
            return null;
        }

        private double? TryExtractObjectiveValue(object solverReturnOrResult)
        {
            if (solverReturnOrResult == null)
            {
                // try solverInstance or model
                var names = new[] { "ObjectiveValue", "Objective", "ObjectiveVal", "Z", "Cost" };
                foreach (var name in names)
                {
                    var p = solverInstance?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.PropertyType == typeof(double)) return (double)p.GetValue(solverInstance);
                    var f = solverInstance?.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.FieldType == typeof(double)) return (double)f.GetValue(solverInstance);

                    var pm = model.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pm != null && pm.PropertyType == typeof(double)) return (double?)pm.GetValue(model);
                    var fm = model.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fm != null && fm.FieldType == typeof(double)) return (double)fm.GetValue(model);
                }
                return null;
            }

            var candidates = new[] { "ObjectiveValue", "Objective", "ObjectiveVal", "Z", "Cost" };
            foreach (var name in candidates)
            {
                var p = solverReturnOrResult.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var v = p.GetValue(solverReturnOrResult);
                    if (v is double d) return d;
                    if (v is float x) return (double)x;
                    if (v != null && Double.TryParse(v.ToString(), out var parsed)) return parsed;
                }
                var f = solverReturnOrResult.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var vv = f.GetValue(solverReturnOrResult);
                    if (vv is double d) return d;
                    if (vv is float f2) return (double)f2;
                    if (vv != null && Double.TryParse(vv.ToString(), out var parsed)) return parsed;
                }
            }
            return null;
        }

        private double[] TryExtractReducedCosts(object solverReturnOrResult)
        {
            // Common property names: ReducedCosts, ReducedCost, DualReducedCosts, RC
            var names = new[] { "ReducedCosts", "ReducedCost", "RC", "DualReducedCosts", "Reduced" };
            // Check solver return, solver instance, model
            var sources = new[] { solverReturnOrResult, solverInstance, model };
            foreach (var src in sources)
            {
                if (src == null) continue;
                foreach (var name in names)
                {
                    var p = src.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        var arr = ConvertToDoubleArray(p.GetValue(src));
                        if (arr != null) return arr;
                    }
                    var f = src.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        var arr = ConvertToDoubleArray(f.GetValue(src));
                        if (arr != null) return arr;
                    }
                }
            }

            return null;
        }

        private double[] TryExtractShadowPrices(object solverReturnOrResult)
        {
            // Common property names: ShadowPrices, Duals, Pi, DualValues, LagrangeMultipliers
            var names = new[] { "ShadowPrices", "Duals", "DualValues", "Pi", "LagrangeMultipliers", "Multipliers" };
            var sources = new[] { solverReturnOrResult, solverInstance, model };
            foreach (var src in sources)
            {
                if (src == null) continue;
                foreach (var name in names)
                {
                    var p = src.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        var arr = ConvertToDoubleArray(p.GetValue(src));
                        if (arr != null) return arr;
                    }
                    var f = src.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        var arr = ConvertToDoubleArray(f.GetValue(src));
                        if (arr != null) return arr;
                    }
                }
            }
            return null;
        }

        #endregion

        #region Basis-preserving numeric search helpers

        /// <summary>
        /// Compare if two solver results share the same basis.
        /// Attempts multiple heuristics:
        ///  - look for explicit Basis or BasisIndices arrays in objects
        ///  - try to detect basic columns by searching for unit columns in returned tableau / matrix
        ///  - else fallback to comparing variable values (rounded) — not ideal but a last resort
        /// </summary>
        private bool IsSameBasis(object baseResult, object newResult)
        {
            if (baseResult == null || newResult == null) return false;

            // 1) Try to find BasisIndices arrays
            var basisNames = new[] { "Basis", "BasisIndices", "BasicIndexes", "BasicVariables", "BasisVars" };
            foreach (var name in basisNames)
            {
                var p1 = baseResult.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var p2 = newResult.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p1 != null && p2 != null)
                {
                    var a1 = ConvertToIntArray(p1.GetValue(baseResult));
                    var a2 = ConvertToIntArray(p2.GetValue(newResult));
                    if (a1 != null && a2 != null)
                    {
                        return a1.SequenceEqual(a2);
                    }
                }

                var f1 = baseResult.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var f2 = newResult.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f1 != null && f2 != null)
                {
                    var a1 = ConvertToIntArray(f1.GetValue(baseResult));
                    var a2 = ConvertToIntArray(f2.GetValue(newResult));
                    if (a1 != null && a2 != null)
                    {
                        return a1.SequenceEqual(a2);
                    }
                }
            }

            // 2) Try to find tableau matrices and detect unit columns
            var tableauNames = new[] { "FinalTableau", "Tableau", "Tableaux", "Table" };
            foreach (var name in tableauNames)
            {
                var p1 = baseResult.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var p2 = newResult.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p1 != null && p2 != null)
                {
                    var t1 = p1.GetValue(baseResult);
                    var t2 = p2.GetValue(newResult);
                    var mat1 = ConvertTo2DDoubleArray(t1);
                    var mat2 = ConvertTo2DDoubleArray(t2);
                    if (mat1 != null && mat2 != null)
                    {
                        // derive basis columns (indices) by finding unit columns in mat (excluding objective row if present)
                        var b1 = FindUnitColumnIndices(mat1);
                        var b2 = FindUnitColumnIndices(mat2);
                        if (b1 != null && b2 != null)
                            return b1.SequenceEqual(b2);
                    }
                }
            }

            // 3) Fallback: compare rounded variable indices (which variables are nonzero)
            var v1 = TryExtractVariableValues(baseResult) ?? TryExtractVariableValuesFromModel(model);
            var v2 = TryExtractVariableValues(newResult) ?? TryExtractVariableValuesFromModel(model);
            if (v1 != null && v2 != null && v1.Length == v2.Length)
            {
                for (int i = 0; i < v1.Length; i++)
                {
                    double a = Math.Round(v1[i], 6);
                    double b = Math.Round(v2[i], 6);
                    if (!a.Equals(b)) return false;
                }
                return true;
            }

            // If nothing matched conservatively assume changed basis
            return false;
        }

        private int[] ConvertToIntArray(object val)
        {
            if (val == null) return null;
            if (val is int[] ia) return ia;
            if (val is IEnumerable<int> ie) return ie.ToArray();
            if (val is IEnumerable<object> eo) return eo.Select(o => Convert.ToInt32(o)).ToArray();
            if (val is IEnumerable<long> el) return el.Select(x => (int)x).ToArray();
            return null;
        }

        private double[,] ConvertTo2DDoubleArray(object val)
        {
            if (val == null) return null;
            // try double[][]
            if (val is double[][] darr) // jagged
            {
                int r = darr.Length;
                int c = darr[0].Length;
                var mat = new double[r, c];
                for (int i = 0; i < r; i++)
                    for (int j = 0; j < c; j++)
                        mat[i, j] = darr[i][j];
                return mat;
            }

            if (val is double[,] d2)
            {
                return d2;
            }

            // try object[][] or IEnumerable<IEnumerable<object>>
            if (val is IEnumerable<IEnumerable<object>> e)
            {
                var rows = e.Select(row => row.Select(o => Convert.ToDouble(o)).ToArray()).ToArray();
                int r = rows.Length, c = rows[0].Length;
                var mat = new double[r, c];
                for (int i = 0; i < r; i++)
                    for (int j = 0; j < c; j++)
                        mat[i, j] = rows[i][j];
                return mat;
            }

            return null;
        }

        private int[] FindUnitColumnIndices(double[,] mat)
        {
            if (mat == null) return null;
            int rows = mat.GetLength(0);
            int cols = mat.GetLength(1);
            var unitCols = new List<int>();
            for (int c = 0; c < cols; c++)
            {
                int oneCount = 0;
                int idxOne = -1;
                int nonZeroCount = 0;
                for (int r = 0; r < rows; r++)
                {
                    double v = mat[r, c];
                    if (Math.Abs(v - 1.0) < 1e-6) { oneCount++; idxOne = r; }
                    if (Math.Abs(v) > 1e-9) nonZeroCount++;
                }
                if (oneCount == 1 && nonZeroCount == 1)
                {
                    unitCols.Add(c);
                }
            }
            return unitCols.Count > 0 ? unitCols.ToArray() : null;
        }

        /// <summary>
        /// Find allowable decrease and increase for objective coefficient at index varIndex by numeric perturbation while preserving basis.
        /// Returns (decrease, increase) where decrease is amount you can subtract (>=0), increase is amount you can add (>=0).
        /// If we cannot determine, returns (null, null).
        /// </summary>
        private (double? decrease, double? increase) FindCoefficientRangePreservingBasis(int varIndex, double baseCoef, object modelObject, object baseSolution)
        {
            try
            {
                // We need a copy of the model so we don't mutate original. Try to clone (shallow) or create copy by reflection.
                var modelCopy = TryShallowCloneModel(modelObject);
                if (modelCopy == null)
                {
                    writer.AppendLine($"Warning: could not clone model to check coefficient ranges for variable index {varIndex}.");
                    return (null, null);
                }

                // find property or field for objective coefficients
                var (prop, field) = FindModelMemberForArray(modelCopy, new[] { "ObjectiveCoefficients", "C", "ObjCoefficients", "Objective", "ObjectiveCoeff" });
                if (prop == null && field == null) return (null, null);

                // get array and ensure double[]
                var arrObj = prop != null ? prop.GetValue(modelCopy) : field.GetValue(modelCopy);
                var arr = ConvertToDoubleArray(arrObj);
                if (arr == null || varIndex < 0 || varIndex >= arr.Length) return (null, null);

                // helper to set coefficient and re-solve
                Func<double, object> reSolve = (newCoef) =>
                {
                    var arr2 = (double[])arr.Clone();
                    arr2[varIndex] = newCoef;
                    if (prop != null)
                    {
                        if (prop.PropertyType == typeof(double[]))
                            prop.SetValue(modelCopy, arr2);
                        else
                        {
                            // try convert to expected array type via reflection if necessary
                            prop.SetValue(modelCopy, arr2);
                        }
                    }
                    else
                    {
                        field.SetValue(modelCopy, arr2);
                    }
                    var sol = SolveModelWithReflection(modelCopy);
                    return sol;
                };

                // Find decrease: search until basis changes
                double? decLimit = SearchDirectionUntilBasisChanges(baseCoef, -1, reSolve, baseSolution);
                // Find increase
                double? incLimit = SearchDirectionUntilBasisChanges(baseCoef, +1, reSolve, baseSolution);

                return (decLimit, incLimit);
            }
            catch (Exception ex)
            {
                writer.AppendLine($"Error while computing coef range for var {varIndex}: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// Find allowable decrease/increase for RHS at constraint index by re-solving.
        /// </summary>
        private (double? decrease, double? increase) FindRHSRangePreservingBasis(int consIndex, double baseRhs, object modelObject, object baseSolution)
        {
            try
            {
                var modelCopy = TryShallowCloneModel(modelObject);
                if (modelCopy == null)
                {
                    writer.AppendLine($"Warning: could not clone model to check RHS ranges for constraint index {consIndex}.");
                    return (null, null);
                }

                var (prop, field) = FindModelMemberForArray(modelCopy, new[] { "Rhs", "RHS", "ConstraintRHS", "B", "RightHandSide" });
                if (prop == null && field == null) return (null, null);

                var arrObj = prop != null ? prop.GetValue(modelCopy) : field.GetValue(modelCopy);
                var arr = ConvertToDoubleArray(arrObj);
                if (arr == null || consIndex < 0 || consIndex >= arr.Length) return (null, null);

                Func<double, object> reSolve = (newRhs) =>
                {
                    var arr2 = (double[])arr.Clone();
                    arr2[consIndex] = newRhs;
                    if (prop != null) prop.SetValue(modelCopy, arr2); else field.SetValue(modelCopy, arr2);
                    var sol = SolveModelWithReflection(modelCopy);
                    return sol;
                };

                double? decLimit = SearchDirectionUntilBasisChanges(baseRhs, -1, reSolve, baseSolution);
                double? incLimit = SearchDirectionUntilBasisChanges(baseRhs, +1, reSolve, baseSolution);

                return (decLimit, incLimit);
            }
            catch (Exception ex)
            {
                writer.AppendLine($"Error while computing RHS range for cons {consIndex}: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// Searches by multiplying a step until basis changes; returns the magnitude allowed (>=0) or null if undetermined.
        /// direction = +1 or -1
        /// </summary>
        private double? SearchDirectionUntilBasisChanges(double baseValue, int direction, Func<double, object> reSolve, object baseSolution)
        {
            try
            {
                double step = PerturbationBase;
                double lastSafe = 0.0;
                // First do linear expansion until basis changes or limit reached
                for (int iter = 0; iter < 80; iter++)
                {
                    double candidate = baseValue + direction * step;
                    var newSol = reSolve(candidate);
                    if (newSol == null)
                    {
                        // If solver failed, treat as change (conservative)
                        break;
                    }
                    bool sameBasis = IsSameBasis(baseSolution, newSol);
                    if (sameBasis)
                    {
                        lastSafe = step;
                        step *= 2;
                        if (Math.Abs(step) > LargeSearchLimit) break;
                        continue;
                    }
                    else
                    {
                        // basis changed at step, now binary search between [lastSafe, step] to find threshold
                        double low = lastSafe;
                        double high = step;
                        for (int bs = 0; bs < 40; bs++)
                        {
                            double mid = (low + high) / 2.0;
                            double candidate2 = baseValue + direction * mid;
                            var solMid = reSolve(candidate2);
                            if (solMid == null) // treat as change
                            {
                                high = mid;
                                continue;
                            }
                            if (IsSameBasis(baseSolution, solMid))
                            {
                                low = mid; // safe
                            }
                            else
                            {
                                high = mid;
                            }
                        }
                        return low;
                    }
                }

                // If we never found a change and reached limit, return lastSafe (possibly large)
                if (Math.Abs(lastSafe) > 0)
                {
                    return lastSafe;
                }

                // If nothing conclusive
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to clone model shallowly by creating a new instance via parameterless ctor and copying public fields/properties.
        /// If that fails, attempt to serialize/deserialize is not performed (keeps simple).
        /// </summary>
        private object TryShallowCloneModel(object sourceModel)
        {
            if (sourceModel == null) return null;
            var t = sourceModel.GetType();
            try
            {
                var ctor = t.GetConstructor(Type.EmptyTypes);
                object copy = null;
                if (ctor != null)
                {
                    copy = ctor.Invoke(null);
                }
                else
                {
                    // try to find copy constructor (same type param)
                    var copyCtor = t.GetConstructors().FirstOrDefault(ci =>
                    {
                        var ps = ci.GetParameters();
                        return ps.Length == 1 && ps[0].ParameterType == t;
                    });
                    if (copyCtor != null)
                    {
                        copy = copyCtor.Invoke(new object[] { sourceModel });
                    }
                }

                if (copy == null) return null;

                // Copy public writable properties and fields shallowly
                foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
                {
                    try
                    {
                        var val = prop.GetValue(sourceModel);
                        // If array, clone array to avoid mutating original
                        if (val is Array arr)
                        {
                            var cloned = (Array)arr.Clone();
                            prop.SetValue(copy, cloned);
                        }
                        else
                        {
                            prop.SetValue(copy, val);
                        }
                    }
                    catch { /* ignore copy failures for individual props */ }
                }

                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var val = field.GetValue(sourceModel);
                        if (val is Array arr)
                        {
                            var cloned = (Array)arr.Clone();
                            field.SetValue(copy, cloned);
                        }
                        else
                        {
                            field.SetValue(copy, val);
                        }
                    }
                    catch { }
                }

                return copy;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Find a property or field that holds an array using candidate names. Returns (PropertyInfo, FieldInfo)
        /// </summary>
        private (PropertyInfo prop, FieldInfo field) FindModelMemberForArray(object modelCopy, string[] candidates)
        {
            foreach (var name in candidates)
            {
                var p = modelCopy.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    var val = p.GetValue(modelCopy);
                    if (val is Array) return (p, null);
                    // maybe property typed object -> still attempt
                    return (p, null);
                }
                var f = modelCopy.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var val = f.GetValue(modelCopy);
                    if (val is Array) return (null, f);
                    return (null, f);
                }
            }
            // as last resort attempt to find first double[] property or field
            var doubleProp = modelCopy.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(p => p.PropertyType == typeof(double[]) || p.PropertyType == typeof(float[]) || p.PropertyType == typeof(int[]));
            if (doubleProp != null) return (doubleProp, null);
            var doubleField = modelCopy.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.FieldType == typeof(double[]) || f.FieldType == typeof(float[]) || f.FieldType == typeof(int[]));
            if (doubleField != null) return (null, doubleField);
            return (null, null);
        }

        #endregion
    }
}
